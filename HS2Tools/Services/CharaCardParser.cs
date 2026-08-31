using System.Buffers;
using System.Text;
using MessagePack;

namespace HS2Tools.Services;

/// <summary>
/// 卡片/场景数据区结构化解析（基准：BepisPlugins ExtensibleSaveFormat + Sideloader UAR）。
/// 只使用底层 MessagePackReader 步行结构，不用类型 resolver，避免格式变体风险。
///
/// ChaFile blob 布局（BinaryWriter 序列化）：
/// int32 loadProductNo → 7bit 前缀字符串【AIS_Chara】→ 7bit 前缀版本字符串
/// → int32 lang → 7bit 前缀 userID → 7bit 前缀 dataID
/// → int32 BlockHeader 字节数 → BlockHeader msgpack（{ "lstInfo": [ [name, version, pos, size], ... ] }）
/// → int64 块数据总长度 → 各块数据（pos/size 相对于块数据区起点）。
/// 角色名在 Parameter/Parameter2 块 msgpack map 的 "fullname" 键；
/// Mod GUID 在 KKEx 块（或文件尾 KKEx trailer）的 UAR 插件数据里。
/// </summary>
internal static class CharaCardParser
{
    // 卡头标记（HS2 沿用 AI 格式）：角色卡 15 字节、坐标卡 17 字节
    private static readonly byte[] CharaMarker = "【AIS_Chara】"u8.ToArray();
    private static readonly byte[] ClothesMarker = "【AIS_Clothes】"u8.ToArray();
    private static readonly byte[] KkexMark = "KKEx"u8.ToArray();

    // Sideloader UniversalAutoResolver 插件 ID（新 + 旧兼容）；其他插件数据一律忽略
    private const string UarPluginId = "com.bepis.sideloader.universalautoresolver";
    private const string UarPluginIdLegacy = "EC.Core.Sideloader.UniversalAutoResolver";

    /// <summary>
    /// 解析数据区（最后一个 IEND + 4 字节 CRC 之后的部分）。
    /// 单卡 1 个 blob、场景 N 个内嵌 blob，统一处理；末尾再尝试 KKEx trailer。
    /// 名字/ModID 按出现顺序去重；单个 blob 失败记 ErrorLog 并继续；
    /// 全部 blob 失败或无标记 → StructuralOk=false（调用方走回退路径）。
    /// </summary>
    public static (List<string> Names, List<string> ModIDs, bool StructuralOk) ParseDataRegion(ReadOnlySpan<byte> region)
    {
        var names = new List<string>();
        var modIds = new List<string>();
        var blobFound = 0;
        var blobOk = 0;

        foreach (var (pos, marker) in FindMarkers(region))
        {
            blobFound++;
            try
            {
                ParseCharaBlob(region, pos, marker, names, modIds);
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
            foreach (var id in ParseKkexTrailer(region))
                AddDistinct(modIds, id);
        }
        catch (Exception ex)
        {
            ErrorLog.Log($"CharaCardParser KKEx trailer parse failed: {ex.Message}");
        }

        return (names, modIds, blobFound > 0 && blobOk > 0);
    }

    // ==================== blob 定位 ====================

    private static List<(int Pos, byte[] Marker)> FindMarkers(ReadOnlySpan<byte> region)
    {
        var list = new List<(int Pos, byte[] Marker)>();
        FindAll(region, CharaMarker, list);
        FindAll(region, ClothesMarker, list);
        list.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        return list;
    }

    private static void FindAll(ReadOnlySpan<byte> region, byte[] marker, List<(int Pos, byte[] Marker)> list)
    {
        var pos = 0;
        while (true)
        {
            var idx = region[pos..].IndexOf(marker);
            if (idx < 0)
                break;
            list.Add((pos + idx, marker));
            pos += idx + 1;
        }
    }

    // ==================== ChaFile blob ====================

    private static void ParseCharaBlob(ReadOnlySpan<byte> region, int markerPos, byte[] marker,
        List<string> names, List<string> modIds)
    {
        // blob 起点 = 标记前 1 字节 7bit 长度前缀 + 前 4 字节 int32 loadProductNo
        if (markerPos < 5)
            throw new InvalidDataException("no room for length prefix + productNo");
        if (region[markerPos - 1] != marker.Length)
            throw new InvalidDataException("marker length prefix mismatch");

        // 坐标卡（ChaFileCoordinate）：无 Parameter/KKEx 块，mod 数据在文件尾 KKEx trailer
        if (marker == ClothesMarker)
            return;

        var p = markerPos + marker.Length;
        _ = Read7BitString(region, ref p); // ChaFileVersion
        _ = ReadInt32LE(region, ref p);    // lang
        _ = Read7BitString(region, ref p); // userID
        _ = Read7BitString(region, ref p); // dataID

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
                foreach (var id in ExtractUarModIds(seq))
                    AddDistinct(modIds, id);
            }
            else
            {
                var fullname = ReadFullname(seq);
                if (!string.IsNullOrWhiteSpace(fullname))
                    AddDistinct(names, fullname.Trim());
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

    /// <summary>Parameter/Parameter2 块：msgpack map（字符串键），取 "fullname" 的字符串值</summary>
    private static string? ReadFullname(ReadOnlySequence<byte> seq)
    {
        var reader = new MessagePackReader(seq);
        var mapCount = reader.ReadMapHeader();
        for (var i = 0; i < mapCount; i++)
        {
            var key = reader.ReadString();
            if (key == "fullname" && reader.NextMessagePackType == MessagePackType.String)
                return reader.ReadString();
            reader.Skip();
        }
        return null;
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
    private static List<string> ParseKkexTrailer(ReadOnlySpan<byte> region)
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
