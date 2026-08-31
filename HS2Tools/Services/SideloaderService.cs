using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using HS2Tools.Models;
using HtmlAgilityPack;

namespace HS2Tools.Services;

/// <summary>
/// Sideload 爬虫（Go internal/sideloader 的 1:1 移植）。
/// 爬取 BetterRepack autoindex 目录列表，对每个远程 zipmod 只发 3~18 次 HTTP Range 小请求提取 GUID。
/// 爬虫从起始 URL 递归下钻全部子目录（ParseLinks 分流目录/mod，目录级并发 3），
/// 因此 KKEC 根目录（其下是多个 "Sideloader Modpack*" 子目录）与 AISHS2 用同一套逻辑即可覆盖，
/// 各游戏仅起点 baseUrl 不同（见 GameProfiles.*.SideloadBaseUrl，唯一事实来源）。
/// Run 可重入（每次运行新建内部状态）；Cancel 只置标志位（已发出的请求不中断）。
/// </summary>
public class SideloaderService : ISideloaderService
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36 Edg/111.0.1661.54";

    /// <summary>小文件阈值：≤64KB 直接整取</summary>
    private const long SmallFileThreshold = 65536;

    /// <summary>渐进抓取块大小 16KB / 窗口上限 256KB</summary>
    private const long ChunkSize = 16384;
    private const long MaxWindow = 262144;

    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly object _runLock = new();
    private RunState? _currentRun;

    /// <summary>单次运行的内部状态（Run 可重入：每次运行新建）</summary>
    private sealed class RunState
    {
        public bool Cancelled;
        public readonly Dictionary<string, string> Result = new();
        public readonly object ResultLock = new();
        public readonly SemaphoreSlim ModSem = new(10);
        public Action<string>? Log;
    }

    /// <param name="proxy">代理串（含认证），解析失败时忽略（与原版一致）</param>
    /// <param name="baseUrl">起始 URL（各游戏不同，见 GameProfiles.SideloadBaseUrl；空则回退 HS2）</param>
    public SideloaderService(string? proxy = null, string? baseUrl = null)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrEmpty(proxy))
            handler.Proxy = ProxyHelper.BuildProxyOrNull(proxy);
        else
            handler.UseProxy = false; // Go 自建 Transport 未配代理时直连；.NET 默认走系统代理，须显式关闭
        // Go 的 Transport 自动协商 HTTP/2：该站点（Apache，Upgrade: h2）支持 h2，
        // Go 因此单连接多路复用。.NET 默认 HTTP/1.1，而服务端对 h1 回 Connection: close
        // ——每请求新建 TCP+TLS 连接，慢且连接对象累积吃内存。改为优先 h2 与 Go 对齐。
        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        _baseUrl = baseUrl ?? GameProfiles.Hs2.SideloadBaseUrl;
    }

    public bool IsRunning
    {
        get { lock (_runLock) return _currentRun != null; }
    }

    /// <summary>取消爬取（只置标志位，已发出的请求不中断——与原版一致）</summary>
    public void Cancel()
    {
        lock (_runLock)
        {
            if (_currentRun != null)
                _currentRun.Cancelled = true;
        }
    }

    /// <summary>运行爬取。已在运行时抛 InvalidOperationException（对应原版 "sideloader is already running"）</summary>
    public async Task<Dictionary<string, string>> RunAsync(
        Action<string>? onLog = null, IProgress<SideloaderProgress>? onProgress = null)
    {
        RunState run;
        lock (_runLock)
        {
            if (_currentRun != null)
                throw new InvalidOperationException("sideloader is already running");
            run = new RunState();
            _currentRun = run;
        }

        try
        {
            run.Log = onLog;
            onLog?.Invoke($"Starting crawl from {_baseUrl}");
            await CrawlAsync(_baseUrl, run, onLog, onProgress);
            int count;
            lock (run.ResultLock)
                count = run.Result.Count;
            onLog?.Invoke($"Crawl completed, found {count} mods");

            lock (run.ResultLock)
                return new Dictionary<string, string>(run.Result);
        }
        finally
        {
            lock (_runLock)
                _currentRun = null;
        }
    }

    // ==================== 爬取 ====================

    private async Task CrawlAsync(string pageUrl, RunState run, Action<string>? onLog, IProgress<SideloaderProgress>? onProgress)
    {
        if (run.Cancelled)
            return;

        onLog?.Invoke($"Processing: {pageUrl}");

        HtmlDocument? doc;
        try
        {
            doc = await FetchDocAsync(pageUrl);
        }
        catch (Exception ex)
        {
            // 原版静默返回；迁移约定：至少记日志
            onLog?.Invoke($"Failed to fetch {pageUrl}: {ex.Message}");
            return;
        }

        var (dirs, mods) = ParseLinks(doc, pageUrl);

        // mod 级并发 10
        var modTasks = new List<Task>();
        foreach (var mod in mods)
        {
            if (run.Cancelled)
                break;
            await run.ModSem.WaitAsync();
            modTasks.Add(ProcessModAsync(mod));
        }
        await Task.WhenAll(modTasks);

        // 目录级并发 3
        var dirSem = new SemaphoreSlim(3);
        var dirTasks = new List<Task>();
        foreach (var dir in dirs)
        {
            if (run.Cancelled)
                break;
            await dirSem.WaitAsync();
            dirTasks.Add(CrawlDirAsync(dir));
        }
        await Task.WhenAll(dirTasks);

        return;

        async Task ProcessModAsync(string modUrl)
        {
            try
            {
                var guid = await ExtractGuidFromZipmodAsync(modUrl, run);
                if (guid != "")
                {
                    int count;
                    lock (run.ResultLock)
                    {
                        run.Result[guid] = modUrl.StartsWith(_baseUrl, StringComparison.Ordinal)
                            ? modUrl[_baseUrl.Length..]
                            : modUrl;
                        count = run.Result.Count;
                    }
                    onLog?.Invoke($"Found: {guid}");
                    onProgress?.Report(new SideloaderProgress(count, 0));
                }
                else
                {
                    int count;
                    lock (run.ResultLock)
                        count = run.Result.Count;
                    onLog?.Invoke($"Skipping {modUrl}: no GUID extracted");
                    onProgress?.Report(new SideloaderProgress(count, 0));
                }
            }
            finally
            {
                run.ModSem.Release();
            }
        }

        async Task CrawlDirAsync(string dirUrl)
        {
            try
            {
                await CrawlAsync(dirUrl, run, onLog, onProgress);
            }
            finally
            {
                dirSem.Release();
            }
        }
    }

    private async Task<HtmlDocument> FetchDocAsync(string pageUrl)
    {
        using var req = NewRequest(HttpMethod.Get, pageUrl);
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var resp = await _client.SendAsync(req);
        if (resp.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");
        var html = await resp.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }

    /// <summary>
    /// 解析 autoindex 页面链接（对应 Go parseLinks）。
    /// table#indexlist tr 跳过前 2 行；href 解码后拼 pageURL 去重；.zipmod 与目录分流。
    /// </summary>
    internal static (List<string> Dirs, List<string> Mods) ParseLinks(HtmlDocument doc, string pageUrl)
    {
        var hrefSet = new HashSet<string>();
        var rows = doc.DocumentNode.SelectNodes("//table[@id='indexlist']//tr");
        if (rows != null)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (i <= 1)
                    continue; // 跳过前 2 行（表头 + 父目录）
                var links = rows[i].SelectNodes(".//a");
                if (links == null)
                    continue;
                foreach (var a in links)
                {
                    var href = a.Attributes["href"]?.Value;
                    if (href == null)
                        continue;
                    string decoded;
                    try
                    {
                        decoded = Uri.UnescapeDataString(href);
                    }
                    catch
                    {
                        continue;
                    }
                    if (decoded == "" || decoded == "/")
                        continue;
                    // 目录/文件名可含 URI 结构字符（如 KKEC 的 #KK_MaterialEditor 目录，href 为 %23KK_MaterialEditor/）。
                    // 解码后直接拼接，# 会被 Uri 当作 fragment 截断：实际请求落到父目录，ParseLinks 又解析出
                    // 同一链接 → 无限递归。拼回 URL 前把 # / ? 转义还原（等价于服务器 href 的原始编码形态）。
                    hrefSet.Add(pageUrl + decoded.Replace("#", "%23").Replace("?", "%3F"));
                }
            }
        }

        var dirs = new List<string>();
        var mods = new List<string>();
        foreach (var href in hrefSet)
        {
            if (href.EndsWith(".zipmod", StringComparison.OrdinalIgnoreCase))
                mods.Add(href);
            else if (href.EndsWith("/", StringComparison.Ordinal))
                dirs.Add(href);
        }
        return (dirs, mods);
    }

    // ==================== HTTP 小请求 ====================

    /// <summary>
    /// 创建请求。Go 的 Transport 自动协商 HTTP/2（该站点 ALPN 支持 h2，单连接多路复用）；
    /// .NET 的 HttpClient.DefaultRequestVersion 不会生效（请求消息 Version 默认 1.1 覆盖客户端默认），
    /// 必须在每个请求上显式设置 Version/VersionPolicy。
    /// </summary>
    private static HttpRequestMessage NewRequest(HttpMethod method, string url)
    {
        return new HttpRequestMessage(method, url)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
    }

    private async Task<byte[]?> FetchRangeAsync(string modUrl, long start, long end, RunState run)
    {
        try
        {
            using var req = NewRequest(HttpMethod.Get, modUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            req.Headers.Range = new RangeHeaderValue(start, end);
            req.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

            if (resp.StatusCode == HttpStatusCode.PartialContent)
                return await resp.Content.ReadAsByteArrayAsync();

            if (resp.StatusCode == HttpStatusCode.OK && start == 0)
            {
                // 整文件请求（小文件分支）：200 即为全量内容（≤64KB，内存有界）
                return await resp.Content.ReadAsByteArrayAsync();
            }

            // Range 被忽略（200 且非整取）：视为失败。
            // 若按原版照单全收，渐进抓取会把全量文件反复前插累积（16×文件×10 并发 → 数 GB）。
            // 正常支持 Range 的站点（nginx/Cloudflare）恒返回 206，此分支仅在代理干预时触发。
            return null;
        }
        catch (Exception ex)
        {
            run.Log?.Invoke($"Failed to fetch range {modUrl}: {ex.Message}");
            return null; // 原版忽略 fetchRange 错误
        }
    }

    private async Task<long?> GetFileSizeAsync(string modUrl, RunState run)
    {
        try
        {
            using var req = NewRequest(HttpMethod.Head, modUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var resp = await _client.SendAsync(req);
            // 原版：ParseInt 头缺失/非法 → error → 返回 ""
            return resp.Content.Headers.ContentLength;
        }
        catch (Exception ex)
        {
            run.Log?.Invoke($"Failed to get file size {modUrl}: {ex.Message}");
            return null;
        }
    }

    // ==================== GUID 提取 ====================

    /// <summary>manifest 压缩数据上限：真实 manifest.xml 仅几 KB，超过即视为解析异常</summary>
    private const uint MaxManifestCompressedSize = 4 * 1024 * 1024;

    private async Task<string> ExtractGuidFromZipmodAsync(string modUrl, RunState run)
    {
        var size = await GetFileSizeAsync(modUrl, run);
        if (size is null)
            return "";
        var totalSize = size.Value;

        byte[]? data;
        byte[]? wholeFile = null;
        if (totalSize <= SmallFileThreshold)
        {
            data = await FetchRangeAsync(modUrl, 0, totalSize - 1, run);
            // 小文件已完整在内存：后续本地头/manifest 直接切片，不再发请求
            if (data != null && data.Length == totalSize)
                wholeFile = data;
        }
        else
        {
            data = null;
            // 从尾部按 16KB 递增抓取（最多 256KB 窗口），新 chunk 前插累积，逐步尝试解析中央目录
            for (long offset = 0; offset < MaxWindow && offset < totalSize; offset += ChunkSize)
            {
                var end = totalSize - offset - 1;
                var start = end - ChunkSize + 1;
                if (start < 0)
                    start = 0;
                var chunk = await FetchRangeAsync(modUrl, start, end, run);
                data = Prepend(chunk, data);
                var entries = ZipRemoteReader.ReadCentralDir(data, totalSize);
                if (entries != null)
                    return await ExtractFromEntriesAsync(entries, modUrl, run);
            }
        }

        if (data == null || data.Length == 0)
            return "";

        var finalEntries = ZipRemoteReader.ReadCentralDir(data, totalSize);
        if (finalEntries != null)
            return await ExtractFromEntriesAsync(finalEntries, modUrl, run, wholeFile);
        return "";
    }

    /// <summary>新 chunk 前插累积（对应 Go data = append(chunk, data...)）</summary>
    private static byte[]? Prepend(byte[]? chunk, byte[]? data)
    {
        if (chunk == null)
            return data;
        if (data == null || data.Length == 0)
            return chunk;
        var combined = new byte[chunk.Length + data.Length];
        Buffer.BlockCopy(chunk, 0, combined, 0, chunk.Length);
        Buffer.BlockCopy(data, 0, combined, chunk.Length, data.Length);
        return combined;
    }

    private async Task<string> ExtractFromEntriesAsync(
        Dictionary<string, ZipEntryInfo> entries, string modUrl, RunState run, byte[]? wholeFile = null)
    {
        // 大小写不敏感找 manifest.xml
        ZipEntryInfo? entry = null;
        foreach (var (name, e) in entries)
        {
            if (string.Equals(name, "manifest.xml", StringComparison.OrdinalIgnoreCase))
            {
                entry = e;
                break;
            }
        }
        if (entry == null)
            return "";

        // 对本地文件头偏移再发 Range（201 字节）；小文件已在内存则直接切片
        var headerStart = (int)entry.Offset;
        byte[]? headerData = wholeFile != null && headerStart < wholeFile.Length
            ? wholeFile[headerStart..Math.Min(headerStart + 201, wholeFile.Length)]
            : null;
        headerData ??= await FetchRangeAsync(modUrl, entry.Offset, entry.Offset + 200, run);
        long dataOffset;
        uint compressedSize;
        if (ZipRemoteReader.TryParseLocalHeader(headerData, out var parsedOffset, out var parsedSize))
        {
            dataOffset = parsedOffset;
            compressedSize = parsedSize;
        }
        else
        {
            // 解析失败回退 30 + nameLen（假设无 extra）
            dataOffset = 30 + entry.NameByteLength;
            compressedSize = entry.CompressedSize;
        }

        // 防御：压缩大小异常（CD 解析错位或畸形文件）时若直接抓取/解压会撑爆内存
        if (compressedSize > MaxManifestCompressedSize)
        {
            run.Log?.Invoke($"Skipping {modUrl}: manifest compressed size {compressedSize} exceeds limit");
            return "";
        }

        var actualOffset = entry.Offset + dataOffset;
        var dataStart = (int)actualOffset;
        byte[]? manifestData = wholeFile != null && dataStart < wholeFile.Length
            ? wholeFile[dataStart..Math.Min(dataStart + (int)compressedSize, wholeFile.Length)]
            : null;
        manifestData ??= await FetchRangeAsync(modUrl, actualOffset, actualOffset + compressedSize - 1, run);
        return ZipRemoteReader.ExtractManifestGuid(manifestData, entry.CompressionMethod) ?? "";
    }

    // ==================== 内置数据库 ====================

    /// <summary>
    /// 加载内嵌的 sideload 数据库（对应 app.go InitSideload + utils.ExtractZipJSON）。
    /// 从内嵌资源 sideload.zip 中按精确名（区分大小写）取 sideload.json。
    /// 内嵌库只有 HS2（AISHS2）一份；其他数据源（如 kkec）返回空字典，由用户爬虫更新落盘。
    /// </summary>
    public static Dictionary<string, string> LoadBundledDatabase(string sourceId = "hs2")
    {
        if (sourceId != GameProfiles.Hs2.SideloadSourceId)
            return new Dictionary<string, string>();

        var asm = typeof(SideloaderService).Assembly;
        var resourceName = Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith("sideload.zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("embedded resource sideload.zip not found");

        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("sideload.json")
            ?? throw new InvalidDataException("entry sideload.json not found in sideload.zip");

        using var reader = new StreamReader(entry.Open());
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>();
    }
}
