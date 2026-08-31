using System.Buffers;
using System.Text;
using HS2Tools.Models;
using MessagePack;

namespace HS2Tools.Services;

/// <summary>
/// 卡片/场景数据区结构化解析（基准：BepisPlugins ExtensibleSaveFormat + Sideloader UAR）。
/// 只使用底层 MessagePackReader 步行结构，不用类型 resolver，避免格式变体风险。
///
/// ChaFile blob 布局（BinaryWriter 序列化）：
/// int32 loadProductNo → 7bit 前缀字符串卡头标记 → 7bit 前缀版本字符串
/// → HS2：int32 lang → 7bit 前缀 userID → 7bit 前缀 dataID；
///   KK/KKS：int32 facePng 长度 + 脸部 PNG 字节（无 lang/userID/dataID，真实卡实测）
/// → int32 BlockHeader 字节数 → BlockHeader msgpack（{ "lstInfo": [ [name, version, pos, size], ... ] }，
///   KK 真实卡为 map 形式 {name, version, pos, size}，两种都认）
/// → int64 块数据总长度 → 各块数据（pos/size 相对于块数据区起点）。
///
/// 多游戏（GameProfiles）：HS2 卡头【AIS_Chara】/【AIS_Clothes】，KK/KKS 卡头
/// 【KoiKatuChara】/【KoiKatuClothes】（KK 与 KKS 卡片格式相同，基准 kkloader 两者共用
/// KoikatuCharaData）；按命中的标记自动识别格式，不依赖"当前游戏"状态。
/// 角色名在 Parameter/Parameter2 块 msgpack map 的名字段键（HS2 "fullname"；
/// KK/KKS "lastname"+"firstname" 按序拼接）；Mod GUID 在 KKEx 块（或文件尾 KKEx trailer）
/// 的 UAR 插件数据里——KKEx/UAR 结构各游戏相同。
/// </summary>
internal static class CharaCardParser
{
    /// <summary>一套卡片格式：角色卡/坐标卡标记 + Parameter 块名字段键 + blob 信封差异</summary>
    private sealed class CardFormat
    {
        public required byte[] CharaMarker;
        public required byte[] ClothesMarker;
        public required string[] NameKeys;
        /// <summary>true（KK/KKS）：version 后接脸部 PNG（int32 长度 + 字节），无 lang/userID/dataID</summary>
        public required bool HasFacePng;
    }

    // 格式表从 GameProfiles 派生（KK/KKS 标记相同，按 CharaMarker 去重）
    private static readonly CardFormat[] Formats = BuildFormats();

    private static CardFormat[] BuildFormats()
    {
        var list = new List<CardFormat>();
        foreach (var p in GameProfiles.All)
        {
            var charaMarker = Encoding.UTF8.GetBytes(p.CharaMarker);
            if (list.Any(f => f.CharaMarker.AsSpan().SequenceEqual(charaMarker)))
                continue;
            list.Add(new CardFormat
            {
                CharaMarker = charaMarker,
                ClothesMarker = Encoding.UTF8.GetBytes(p.ClothesMarker),
                NameKeys = p.NameKeys,
                HasFacePng = p.CharaBlobHasFacePng,
            });
        }
        return list.ToArray();
    }

    // Sideloader UniversalAutoResolver 插件 ID（新 + 旧兼容）；其他插件数据一律忽略
    private const string UarPluginId = "com.bepis.sideloader.universalautoresolver";
    private const string UarPluginIdLegacy = "EC.Core.Sideloader.UniversalAutoResolver";

    // KKEx trailer 定位标记（块名固定为 "KKEx"，各游戏相同）
    private static readonly byte[] KkexMark = "KKEx"u8.ToArray();

    /// <summary>
    /// 解析数据区（真 IEND chunk 之后的部分，见 ScannerService.GetDataRegionOffset）。
    /// 单卡 1 个 blob、场景 N 个内嵌 blob，统一处理；末尾再尝试 KKEx trailer。
    /// 名字/ModID 按出现顺序去重；单个 blob 失败记 ErrorLog 并继续；
    /// 全部 blob 失败或无标记 → StructuralOk=false（调用方走回退路径）。
    /// kkexBlobs 非 null 时顺带收集 KKEx 原始字节（块 + trailer 的 msgpack map 部分，
    /// 供调用方做 Material Editor shader 名等内容级子串匹配，解析器本身不感知 shader）。
    /// </summary>
    public static (List<string> Names, List<string> ModIDs, bool StructuralOk) ParseDataRegion(
        ReadOnlySpan<byte> region, List<byte[]>? kkexBlobs = null)
    {
        var names = new List<string>();
        var modIds = new List<string>();
        var blobFound = 0;
        var blobOk = 0;

        foreach (var (pos, marker, format) in FindMarkers(region))
        {
            blobFound++;
            try
            {
                ParseCharaBlob(region, pos, marker, format, names, modIds, kkexBlobs);
                blobOk++;
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"CharaCardParser blob parse failed at +{pos}: {ex.Message}");
            }
        }

        // 场景/坐标卡级扩展数据 trailer（解析失败不视为整体失败）
        try
        {
            foreach (var id in ParseKkexTrailer(region, kkexBlobs))
                AddDistinct(modIds, id);
        }
        catch (Exception ex)
        {
            ErrorLog.Log($"CharaCardParser KKEx trailer parse failed: {ex.Message}");
        }

        return (names, modIds, blobFound > 0 && blobOk > 0);
    }

    // ==================== blob 定位 ====================

    /// <summary>扫描所有已注册格式的角色卡/坐标卡标记（多游戏自动识别）</summary>
    private static List<(int Pos, byte[] Marker, CardFormat Format)> FindMarkers(ReadOnlySpan<byte> region)
    {
        var list = new List<(int Pos, byte[] Marker, CardFormat Format)>();
        foreach (var format in Formats)
        {
            FindAll(region, format.CharaMarker, format, list);
            FindAll(region, format.ClothesMarker, format, list);
        }
        list.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        return list;
    }

    private static void FindAll(ReadOnlySpan<byte> region, byte[] marker, CardFormat format,
        List<(int Pos, byte[] Marker, CardFormat Format)> list)
    {
        var pos = 0;
        while (true)
        {
            var idx = region[pos..].IndexOf(marker);
            if (idx < 0)
                break;
            list.Add((pos + idx, marker, format));
            pos += idx + 1;
        }
    }

    // ==================== ChaFile blob ====================

    private static void ParseCharaBlob(ReadOnlySpan<byte> region, int markerPos, byte[] marker, CardFormat format,
        List<string> names, List<string> modIds, List<byte[]>? kkexBlobs = null)
    {
        // blob 起点 = 标记前 1 字节 7bit 长度前缀 + 前 4 字节 int32 loadProductNo
        if (markerPos < 5)
            throw new InvalidDataException("no room for length prefix + productNo");
        if (region[markerPos - 1] != marker.Length)
            throw new InvalidDataException("marker length prefix mismatch");

        // 坐标卡（ChaFileCoordinate）：无 Parameter/KKEx 块，mod 数据在文件尾 KKEx trailer
        if (marker == format.ClothesMarker)
            return;

        var p = markerPos + marker.Length;
        _ = Read7BitString(region, ref p); // ChaFileVersion
        if (format.HasFacePng)
        {
            // KK/KKS：version 之后是脸部特写 PNG（int32 长度 + 字节），随后直接是 BlockHeader，
            // 无 HS2 的 lang/userID/dataID（基准 kkloader KoikatuCharaData；真实卡实测确认）
            var faceLen = ReadInt32LE(region, ref p);
            if (faceLen < 0 || faceLen > region.Length - p)
                throw new InvalidDataException("face png length out of bounds");
            p += faceLen;
        }
        else
        {
            _ = ReadInt32LE(region, ref p);    // lang
            _ = Read7BitString(region, ref p); // userID
            _ = Read7BitString(region, ref p); // dataID
        }

        var headerLen = ReadInt32LE(region, ref p);
        if (headerLen <= 0 || headerLen > region.Length - p)
            throw new InvalidDataException("block header length out of bounds");
        var infos = ParseBlockHeader(new ReadOnlySequence<byte>(region.Slice(p, headerLen).ToArray()));
        p += headerLen;

        if (p + 8 > region.Length)
            throw new InvalidDataException("no room for block data length");
        p += 8; // int64 块数据总长度（仅校验存在，不取值）
        var blocksStart = p;

        foreach (var info in infos)
        {
            if (info.Name is not ("Parameter" or "Parameter2" or "KKEx"))
                continue;
            if (info.Pos < 0 || info.Size < 0 || info.Size > region.Length - blocksStart - info.Pos)
                throw new InvalidDataException($"block {info.Name} pos/size out of bounds");
            var seq = new ReadOnlySequence<byte>(region.Slice(blocksStart + (int)info.Pos, (int)info.Size).ToArray());

            if (info.Name == "KKEx")
            {
                // 结构化解析前保留原始字节（Material Editor shader 名等按内容子串匹配用）
                kkexBlobs?.Add(region.Slice(blocksStart + (int)info.Pos, (int)info.Size).ToArray());
                foreach (var id in ExtractUarModIds(seq))
                    AddDistinct(modIds, id);
            }
            else
            {
                var name = ReadName(seq, format.NameKeys);
                if (!string.IsNullOrWhiteSpace(name))
                    AddDistinct(names, name);
            }
        }
    }

    /// <summary>BlockHeader msgpack：map { "lstInfo": [ Info, ... ] }；Info 为数组式 4 元素 [name, version, pos, size]（兼容 map 形式）</summary>
    private static List<(string Name, long Pos, long Size)> ParseBlockHeader(ReadOnlySequence<byte> seq)
    {
        var infos = new List<(string, long, long)>();
        var reader = new MessagePackReader(seq);
        var mapCount = reader.ReadMapHeader();
        for (var i = 0; i < mapCount; i++)
        {
            var key = reader.ReadString();
            if (key != "lstInfo" || reader.NextMessagePackType != MessagePackType.Array)
            {
                reader.Skip();
                continue;
            }

            var arrCount = reader.ReadArrayHeader();
            for (var j = 0; j < arrCount; j++)
            {
                if (reader.NextMessagePackType == MessagePackType.Array)
                {
                    var fieldCount = reader.ReadArrayHeader();
                    if (fieldCount < 4)
                        throw new InvalidDataException("BlockHeader.Info array too short");
                    var name = reader.ReadString() ?? "";
                    _ = reader.ReadString(); // version
                    var pos = reader.ReadInt64();
                    var size = reader.ReadInt64();
                    for (var k = 4; k < fieldCount; k++)
                        reader.Skip();
                    infos.Add((name, pos, size));
                }
                else if (reader.NextMessagePackType == MessagePackType.Map)
                {
                    var fieldCount = reader.ReadMapHeader();
                    string name = "";
                    long pos = 0, size = 0;
                    for (var k = 0; k < fieldCount; k++)
                    {
                        switch (reader.ReadString())
                        {
                            case "name": name = reader.ReadString() ?? ""; break;
                            case "pos": pos = reader.ReadInt64(); break;
                            case "size": size = reader.ReadInt64(); break;
                            default: reader.Skip(); break;
                        }
                    }
                    infos.Add((name, pos, size));
                }
                else
                {
                    throw new InvalidDataException("unexpected BlockHeader.Info encoding");
                }
            }
        }
        return infos;
    }

    /// <summary>
    /// Parameter/Parameter2 块：msgpack map（字符串键），按 NameKeys 取字符串值并顺序拼接
    /// （HS2 单键 "fullname"；KK/KKS "lastname"+"firstname"，基准 ChaFileParameter 字段）。
    /// </summary>
    private static string? ReadName(ReadOnlySequence<byte> seq, string[] nameKeys)
    {
        var reader = new MessagePackReader(seq);
        var mapCount = reader.ReadMapHeader();
        var values = new string?[nameKeys.Length];
        for (var i = 0; i < mapCount; i++)
        {
            var key = reader.ReadString();
            var idx = Array.IndexOf(nameKeys, key);
            if (idx >= 0 && reader.NextMessagePackType == MessagePackType.String)
                values[idx] = reader.ReadString();
            else
                reader.Skip();
        }
        var name = string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));
        return name == "" ? null : name;
    }

    // ==================== KKEx（块 + trailer 共用） ====================

    /// <summary>
    /// KKEx msgpack：map&lt;插件ID, [version:int, data:map]&gt;。
    /// 只取 UAR 插件（新/旧 ID）：data["info"] = array of bin，每个 bin 是一条 ResolveInfo
    /// 的 msgpack map（字符串键），取 "ModID" 的字符串值。
    /// </summary>
    private static List<string> ExtractUarModIds(ReadOnlySequence<byte> seq)
    {
        var result = new List<string>();
        var reader = new MessagePackReader(seq);
        var pluginCount = reader.ReadMapHeader();
        for (var i = 0; i < pluginCount; i++)
        {
            var pluginId = reader.ReadString();
            var isUar = pluginId is UarPluginId or UarPluginIdLegacy;
            if (reader.NextMessagePackType != MessagePackType.Array)
            {
                reader.Skip();
                continue;
            }

            var fieldCount = reader.ReadArrayHeader();
            for (var k = 0; k < fieldCount; k++)
            {
                if (isUar && k == 1 && reader.NextMessagePackType == MessagePackType.Map)
                    ReadResolveInfos(ref reader, result);
                else
                    reader.Skip();
            }
        }
        return result;
    }

    private static void ReadResolveInfos(ref MessagePackReader reader, List<string> result)
    {
        var mapCount = reader.ReadMapHeader();
        for (var i = 0; i < mapCount; i++)
        {
            var key = reader.ReadString();
            if (key != "info" || reader.NextMessagePackType != MessagePackType.Array)
            {
                reader.Skip();
                continue;
            }

            var infoCount = reader.ReadArrayHeader();
            for (var j = 0; j < infoCount; j++)
            {
                if (reader.NextMessagePackType != MessagePackType.Binary)
                {
                    reader.Skip();
                    continue;
                }
                var bin = reader.ReadBytes();
                if (bin == null)
                    continue;
                try
                {
                    var modId = ReadResolveInfoModId(bin.Value);
                    if (!string.IsNullOrEmpty(modId))
                        AddDistinct(result, modId);
                }
                catch (Exception ex)
                {
                    ErrorLog.Log($"CharaCardParser ResolveInfo parse failed: {ex.Message}");
                }
            }
        }
    }

    private static string? ReadResolveInfoModId(ReadOnlySequence<byte> seq)
    {
        var reader = new MessagePackReader(seq);
        var mapCount = reader.ReadMapHeader();
        for (var i = 0; i < mapCount; i++)
        {
            var key = reader.ReadString();
            if (key == "ModID" && reader.NextMessagePackType == MessagePackType.String)
                return reader.ReadString();
            reader.Skip();
        }
        return null;
    }

    // ==================== KKEx trailer ====================

    /// <summary>
    /// 文件尾扩展数据 trailer：7bit 前缀字符串 "KKEx" + int32 version + int32 length + msgpack map。
    /// 在数据区内暴力扫描 "KKEx" 字节出现处（参考实现的场景导入同样扫字节），
    /// 校验 7bit 前缀、version 合理、length 终点与数据区末尾吻合才算命中。
    /// </summary>
    private static List<string> ParseKkexTrailer(ReadOnlySpan<byte> region, List<byte[]>? kkexBlobs = null)
    {
        var result = new List<string>();
        var pos = 0;
        while (true)
        {
            var idx = region[pos..].IndexOf(KkexMark);
            if (idx < 0)
                break;
            idx += pos;
            pos = idx + 1;

            // 7bit 长度前缀（"KKEx" 长度 4，单字节前缀）
            if (idx < 1 || region[idx - 1] != KkexMark.Length)
                continue;
            var p = idx + KkexMark.Length;
            if (p + 8 > region.Length)
                continue;
            var version = BitConverter.ToInt32(region.Slice(p, 4));
            var length = BitConverter.ToInt32(region.Slice(p + 4, 4));
            p += 8;
            if (version < 0 || version > 1000)
                continue;
            // length 终点必须与数据区末尾吻合
            if (length <= 0 || p + length != region.Length)
                continue;

            var seq = new ReadOnlySequence<byte>(region.Slice(p, length).ToArray());
            kkexBlobs?.Add(region.Slice(p, length).ToArray());
            foreach (var id in ExtractUarModIds(seq))
                AddDistinct(result, id);
            break; // 命中合法 trailer 即停
        }
        return result;
    }

    // ==================== 基础读取 ====================

    /// <summary>BinaryWriter 风格 7bit 长度前缀字符串</summary>
    private static string Read7BitString(ReadOnlySpan<byte> region, ref int p)
    {
        var len = Read7BitInt(region, ref p);
        if (len < 0 || len > region.Length - p)
            throw new InvalidDataException("string length out of bounds");
        var s = Encoding.UTF8.GetString(region.Slice(p, len));
        p += len;
        return s;
    }

    private static int Read7BitInt(ReadOnlySpan<byte> region, ref int p)
    {
        var result = 0;
        var shift = 0;
        while (true)
        {
            if (p >= region.Length || shift >= 35)
                throw new InvalidDataException("bad 7bit encoded int");
            var b = region[p++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
        }
    }

    private static int ReadInt32LE(ReadOnlySpan<byte> region, ref int p)
    {
        if (p + 4 > region.Length)
            throw new InvalidDataException("int32 out of bounds");
        var v = BitConverter.ToInt32(region.Slice(p, 4));
        p += 4;
        return v;
    }

    /// <summary>按出现顺序去重（不用 HashSet 无序语义）</summary>
    private static void AddDistinct(List<string> list, string value)
    {
        if (!list.Contains(value))
            list.Add(value);
    }
}
