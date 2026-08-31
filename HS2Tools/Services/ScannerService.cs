using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using HS2Tools.Models;
using System.Collections.Concurrent;

namespace HS2Tools.Services;

/// <summary>
/// 扫描与解析服务。
/// 目录扫描 / zipmod / 文件操作是 Go internal/scanner 的 1:1 移植（含字节级 hack）；
/// 卡片/场景数据区解析以 IllusionModdingAPI/BepisPlugins 为基准做结构化解析
/// （CharaCardParser），旧字节扫描（SearchBuffer）仅作数据区内的回退路径。
/// </summary>
public class ScannerService
{
    // 标记定义（回退路径：结构化解析失败时的旧字节扫描，Unity BinaryWriter 序列化上下文关键词）
    private static readonly byte[] NameStart = "fullname"u8.ToArray();
    private static readonly byte[] NameEnd = "personality"u8.ToArray();
    private static readonly byte[] ModStart = "ModID"u8.ToArray();
    private static readonly byte[] ModEnd = "Slot"u8.ToArray();

    // ==================== 目录扫描 ====================

    /// <summary>
    /// 递归扫描目录（对应 Go ScanDirectory）。
    /// 排除目录判定是子串匹配；扩展名大小写不敏感；无法访问的条目静默跳过；
    /// 遍历顺序与 Go filepath.Walk 一致（每目录按词典序）。
    /// </summary>
    public List<string> ScanDirectory(string dir, ScanOptions? options = null)
    {
        var opts = options ?? new ScanOptions();
        var files = new List<string>();

        FileAttributes attrs;
        try
        {
            attrs = File.GetAttributes(dir);
        }
        catch (Exception ex)
        {
            ErrorLog.Log($"ScanDirectory root inaccessible: {dir}: {ex.Message}");
            return files; // 根路径无法访问 → 空结果（Go Walk 返回 nil 错误）
        }

        if ((attrs & FileAttributes.Directory) != 0 && (attrs & FileAttributes.ReparsePoint) == 0)
        {
            // Go 对根目录同样做排除判定（SkipDir）
            var rootName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (IsExcludedDir(rootName, opts))
                return files;
            ScanDirRecursive(dir, opts, files);
        }
        else
        {
            // 根是文件：套用扩展名过滤
            if (MatchExtension(dir, opts))
                files.Add(dir);
        }

        return files;
    }

    private static void ScanDirRecursive(string dir, ScanOptions opts, List<string> files)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir);
        }
        catch (Exception ex)
        {
            ErrorLog.Log($"ScanDirectory skip unreadable dir: {dir}: {ex.Message}");
            return; // 目录不可读 → 跳过（Go walkFn 返回 nil）
        }

        // Go filepath.Walk 每目录按词典序遍历
        foreach (var entry in entries.OrderBy(e => e, StringComparer.Ordinal))
        {
            FileAttributes attrs;
            try
            {
                attrs = File.GetAttributes(entry);
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"ScanDirectory skip entry: {entry}: {ex.Message}");
                continue; // 条目无法访问 → 跳过
            }

            var isDir = (attrs & FileAttributes.Directory) != 0 && (attrs & FileAttributes.ReparsePoint) == 0;
            if (isDir)
            {
                if (!IsExcludedDir(Path.GetFileName(entry), opts))
                    ScanDirRecursive(entry, opts, files);
                continue;
            }

            if (MatchExtension(entry, opts))
                files.Add(entry);
        }
    }

    private static bool IsExcludedDir(string name, ScanOptions opts)
    {
        // 原版：strings.Contains(name, exclude) 子串匹配（exclude 为空串时恒真，照原样保留语义）
        foreach (var exclude in opts.ExcludeDir)
        {
            if (name.Contains(exclude, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool MatchExtension(string path, ScanOptions opts)
    {
        if (opts.TargetExtension.Count == 0)
            return true;
        var ext = Path.GetExtension(path);
        foreach (var targetExt in opts.TargetExtension)
        {
            if (string.Equals(ext, targetExt, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // ==================== PNG 解析 ====================

    /// <summary>从 PNG 文件中提取所有 Mod GUID（先结构化解析，失败回退数据区字节扫描）</summary>
    public List<string> ReadPngMods(string filePath)
    {
        if (!FileExists(filePath))
            return new List<string>();
        var data = File.ReadAllBytes(filePath);
        return ExtractFromGameData(data, wantNames: false);
    }

    /// <summary>从 PNG 文件中提取所有角色名称（先结构化解析，失败回退数据区字节扫描）</summary>
    public List<string> ReadPngNames(string filePath)
    {
        if (!FileExists(filePath))
            return new List<string>();
        var data = File.ReadAllBytes(filePath);
        return ExtractFromGameData(data, wantNames: true);
    }

    /// <summary>
    /// 名称/Mod 提取主路径：结构化解析数据区；StructuralOk=false 时回退旧 SearchBuffer，
    /// 扫描范围收窄为数据区（不再扫 PNG 图像字节）；无 IEND 时整体视作数据区回退扫描。
    /// </summary>
    private static List<string> ExtractFromGameData(byte[] data, bool wantNames)
    {
        var offset = GetDataRegionOffset(data);
        if (offset >= 0)
        {
            var (names, modIds, structuralOk) = CharaCardParser.ParseDataRegion(data.AsSpan(offset));
            if (structuralOk)
                return wantNames ? names : modIds;
            ErrorLog.Log($"structural parse failed, fallback to byte scan in data region ({data.Length - offset} bytes)");
            return SearchBuffer(wantNames ? NameStart : ModStart, wantNames ? NameEnd : ModEnd, data[offset..]);
        }
        // 无 IEND（非卡片文件）：保持旧行为，全文件回退扫描
        return SearchBuffer(wantNames ? NameStart : ModStart, wantNames ? NameEnd : ModEnd, data);
    }

    /// <summary>数据区起点 = 最后一个 IEND 的 'D' 之后 + 4 字节 CRC（越界钳制）；无 IEND 返回 -1</summary>
    internal static int GetDataRegionOffset(byte[] data)
    {
        var iend = FindLastIend(data);
        if (iend < 0)
            return -1;
        return Math.Min(iend + 4, data.Length);
    }

    /// <summary>
    /// 回退路径：从 Buffer 中循环提取所有 [start...end] 区间的字符串（Go searchBuffer）。
    /// 命中区间去首尾各 1 字节再 Trim；结果去重且无序。
    /// </summary>
    internal static List<string> SearchBuffer(byte[] start, byte[] end, byte[] data)
    {
        var result = new HashSet<string>();
        var pos = 0;

        while (true)
        {
            var startIndex = data.AsSpan(pos).IndexOf(start);
            if (startIndex < 0)
                break;
            startIndex += pos;

            var endIndex = data.AsSpan(startIndex + start.Length).IndexOf(end);
            if (endIndex < 0)
                break;
            endIndex += startIndex + start.Length;

            // 提取内容（长度 > 2 时去掉首尾各 1 字节）
            var contentStart = startIndex + start.Length;
            var contentEnd = endIndex;
            if (contentEnd - contentStart > 2)
            {
                contentStart += 1;
                contentEnd -= 1;
            }
            var str = BufferToString(data[contentStart..contentEnd]);
            if (str != "")
                result.Add(str);

            // 移动到 end 之后继续搜索
            pos = endIndex + end.Length;
        }

        return result.ToList();
    }

    /// <summary>
    /// Buffer 转字符串 + TrimSpace。
    /// 注意：Go string(bytes) 对无效 UTF-8 保留原始字节，.NET 默认替换为 U+FFFD——
    /// 乱码卡名输出可能与原版不同（迁移文档已记录，测试期确认可接受）。
    /// </summary>
    private static string BufferToString(byte[] buffer) => Encoding.UTF8.GetString(buffer).Trim();

    /// <summary>读取单个 PNG 文件的缩略图（Base64）。读盘失败返回空串（与原版一致）</summary>
    public string ReadPngImage(string filePath)
    {
        if (!FileExists(filePath))
            return "";

        byte[] data;
        try
        {
            data = File.ReadAllBytes(filePath);
        }
        catch
        {
            return "";
        }

        var endIndex = FindLastIend(data);
        if (endIndex < 0)
            return "";

        // 截取纯 PNG 图像数据（含 IEND 的 4 字节 CRC，得到完整 PNG；越界钳制）
        var end = Math.Min(endIndex + 4, data.Length);
        return Convert.ToBase64String(data, 0, end);
    }

    /// <summary>
    /// 从文件末尾反向搜索最后一个 "IEND"，截断点 = 'D' 之后（覆盖 CRC）。
    /// 找不到返回 -1。
    /// </summary>
    internal static int FindLastIend(byte[] data)
    {
        for (var i = data.Length - 1; i >= 3; i--)
        {
            if (data[i] == (byte)'D' && data[i - 1] == (byte)'N' && data[i - 2] == (byte)'E' && data[i - 3] == (byte)'I')
                return i + 1;
        }
        return -1;
    }

    /// <summary>解析 PNG 文件的完整数据（用于单卡查看器）</summary>
    public PngParseResult ParsePngData(string filePath)
    {
        if (!FileExists(filePath))
            throw new InvalidDataException("not a valid PNG file");

        var data = File.ReadAllBytes(filePath);
        var iendIndex = FindLastIend(data);
        if (iendIndex < 0)
            throw new InvalidDataException("IEND marker not found");

        var offset = Math.Min(iendIndex + 4, data.Length);
        var (names, modIds, structuralOk) = CharaCardParser.ParseDataRegion(data.AsSpan(offset));
        if (!structuralOk)
        {
            ErrorLog.Log($"ParsePngData structural parse failed, fallback to byte scan: {filePath}");
            var region = data[offset..];
            names = SearchBuffer(NameStart, NameEnd, region);
            modIds = SearchBuffer(ModStart, ModEnd, region);
        }

        return new PngParseResult
        {
            ModIDs = modIds,
            CharaNames = names,
            // 真正的追加数据长度（IEND 'D' 之后 + 4 字节 CRC），下界钳 0
            GameDataLen = Math.Max(0, data.Length - (iendIndex + 4)),
        };
    }

    /// <summary>批量提取 Mod GUID（默认并发 8）。单文件失败跳过（记日志）</summary>
    public async Task<List<PngModResult>> ReadPngModsBatchAsync(
        IReadOnlyList<string> filePaths, int concurrency = 8, Action<string>? onError = null, CancellationToken ct = default)
    {
        if (concurrency <= 0)
            concurrency = 8;
        var results = new ConcurrentBag<PngModResult>();
        await Parallel.ForEachAsync(filePaths, new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct }, (path, _) =>
        {
            try
            {
                results.Add(new PngModResult { Path = path, ModIDs = ReadPngMods(path) });
            }
            catch (Exception ex)
            {
                onError?.Invoke($"ReadPngMods failed: {path}: {ex.Message}");
            }
            return ValueTask.CompletedTask;
        });
        return results.ToList();
    }

    /// <summary>批量提取角色名称（默认并发 8）。单文件失败跳过（记日志）</summary>
    public async Task<List<PngNamesResult>> ReadPngNamesBatchAsync(
        IReadOnlyList<string> filePaths, int concurrency = 8, Action<string>? onError = null, CancellationToken ct = default)
    {
        if (concurrency <= 0)
            concurrency = 8;
        var results = new ConcurrentBag<PngNamesResult>();
        await Parallel.ForEachAsync(filePaths, new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct }, (path, _) =>
        {
            try
            {
                results.Add(new PngNamesResult { Path = path, Names = ReadPngNames(path) });
            }
            catch (Exception ex)
            {
                onError?.Invoke($"ReadPngNames failed: {path}: {ex.Message}");
            }
            return ValueTask.CompletedTask;
        });
        return results.ToList();
    }

    /// <summary>批量提取缩略图（默认并发 4）</summary>
    public async Task<List<PngImageResult>> ReadPngImagesBatchAsync(
        IReadOnlyList<string> filePaths, int concurrency = 4, Action<string>? onError = null, CancellationToken ct = default)
    {
        if (concurrency <= 0)
            concurrency = 4;
        var results = new ConcurrentBag<PngImageResult>();
        await Parallel.ForEachAsync(filePaths, new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct }, (path, _) =>
        {
            try
            {
                results.Add(new PngImageResult { Path = path, ImageData = ReadPngImage(path) });
            }
            catch (Exception ex)
            {
                onError?.Invoke($"ReadPngImage failed: {path}: {ex.Message}");
            }
            return ValueTask.CompletedTask;
        });
        return results.ToList();
    }

    /// <summary>
    /// 单文件页面数据（名称+缩略图）。
    /// C# 版一次读盘同时取两者（修掉原版同一文件读盘 2 次的问题）。
    /// 返回 null 表示读盘失败（对应原版 nameErr != nil → 跳过该文件）。
    /// </summary>
    public PngPageDataResult? ReadPngPageData(string filePath)
    {
        var names = new List<string>();
        var image = "";

        if (FileExists(filePath))
        {
            byte[] data;
            try
            {
                data = File.ReadAllBytes(filePath);
            }
            catch
            {
                return null;
            }
            names = ExtractFromGameData(data, wantNames: true);
            var endIndex = FindLastIend(data);
            if (endIndex >= 0)
            {
                // 缩略图含 IEND 的 4 字节 CRC（完整 PNG；越界钳制）
                var end = Math.Min(endIndex + 4, data.Length);
                image = Convert.ToBase64String(data, 0, end);
            }
        }

        return new PngPageDataResult { Path = filePath, Names = names, ImageData = image };
    }

    /// <summary>批量获取页面数据（默认并发 4）。读盘失败的文件跳过（记日志）</summary>
    public async Task<List<PngPageDataResult>> ReadPngPageDataBatchAsync(
        IReadOnlyList<string> filePaths, int concurrency = 4, Action<string>? onError = null, CancellationToken ct = default)
    {
        if (concurrency <= 0)
            concurrency = 4;
        var results = new ConcurrentBag<PngPageDataResult>();
        await Parallel.ForEachAsync(filePaths, new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct }, (path, _) =>
        {
            var result = ReadPngPageData(path);
            if (result != null)
                results.Add(result);
            else
                onError?.Invoke($"ReadPngPageData failed: {path}");
            return ValueTask.CompletedTask;
        });
        return results.ToList();
    }

    // ==================== zipmod 解析 ====================

    /// <summary>清理字符串，只保留 ASCII 可见字符（32~126），再 Trim。按 rune 处理避免半个代理对</summary>
    internal static string CleanString(string str)
    {
        var sb = new StringBuilder(str.Length);
        foreach (var r in str.EnumerateRunes())
        {
            if (r.Value is >= 32 and <= 126)
                sb.Append(r.ToString());
        }
        return sb.ToString().Trim();
    }

    /// <summary>解析 zipmod 文件，提取 manifest.xml 中的信息（guid/name 清洗、version 不清洗）</summary>
    public Dictionary<string, ModInfo> ReadZipMod(string filePath)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(filePath);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"failed to open zipmod: {ex.Message}", ex);
        }

        using (archive)
        {
            // manifest.xml 条目名大小写不敏感
            var manifestEntry = archive.Entries.FirstOrDefault(
                e => string.Equals(e.Name, "manifest.xml", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("manifest.xml not found in zipmod");

            string content;
            using (var reader = new StreamReader(manifestEntry.Open()))
                content = reader.ReadToEnd();

            XDocument doc;
            try
            {
                doc = XDocument.Parse(content);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"failed to parse manifest.xml: {ex.Message}", ex);
            }

            // Go encoding/xml：根元素必须是 manifest
            if (doc.Root is null || doc.Root.Name.LocalName != "manifest")
                throw new InvalidDataException("failed to parse manifest.xml: root element is not <manifest>");

            // Go encoding/xml 字段匹配：精确优先，大小写不敏感回退
            var guid = GetElementValue(doc.Root, "guid") ?? "";
            if (guid == "")
                throw new InvalidDataException("manifest.xml missing guid field");

            var cleanGuid = CleanString(guid);
            var cleanName = CleanString(GetElementValue(doc.Root, "name") ?? "");

            return new Dictionary<string, ModInfo>
            {
                [cleanGuid] = new ModInfo
                {
                    Name = cleanName,
                    Version = GetElementValue(doc.Root, "version") ?? "", // Version 不清洗
                    Path = filePath,
                },
            };
        }
    }

    private static string? GetElementValue(XElement root, string name)
    {
        var el = root.Element(name);
        if (el is null)
            el = root.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
        return el?.Value;
    }

    /// <summary>批量解析 zipmod 文件（默认并发 4）。单文件失败跳过（记日志）；重复 guid 覆盖</summary>
    public async Task<Dictionary<string, ModInfo>> ReadZipModBatchAsync(
        IReadOnlyList<string> filePaths, int concurrency = 4, Action<string>? onError = null, CancellationToken ct = default)
    {
        if (concurrency <= 0)
            concurrency = 4;
        var results = new ConcurrentDictionary<string, ModInfo>();
        await Parallel.ForEachAsync(filePaths, new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct }, (path, _) =>
        {
            try
            {
                foreach (var (guid, info) in ReadZipMod(path))
                    results[guid] = info;
            }
            catch (Exception ex)
            {
                onError?.Invoke($"ReadZipMod failed: {path}: {ex.Message}");
            }
            return ValueTask.CompletedTask;
        });
        return new Dictionary<string, ModInfo>(results);
    }

    // ==================== 文件操作 ====================

    /// <summary>移动文件（.NET File.Move 原生支持跨盘符；目标目录不存在则创建）</summary>
    public void MoveFile(string src, string dst)
    {
        var dir = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.Move(src, dst);
    }

    /// <summary>检查并创建目标目录</summary>
    public void CheckTargetDir(string target)
    {
        if (!Directory.Exists(target))
            Directory.CreateDirectory(target);
    }

    /// <summary>检查文件是否存在且是 PNG（不是目录、扩展名大小写不敏感）</summary>
    public static bool FileExists(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Directory) != 0)
                return false;
            return string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
