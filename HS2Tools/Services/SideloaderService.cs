using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;
using HS2Tools.Models;
using HtmlAgilityPack;

namespace HS2Tools.Services;

/// <summary>
/// Sideload 爬虫（Go internal/sideloader 移植，含更新可靠性/性能修复）。
/// 爬取 BetterRepack autoindex 目录列表，对每个远程 zipmod 只发 1~7 次 HTTP Range 小请求提取 GUID
/// （suffix range 首请求省 HEAD、尾部窗口几何扩窗、本地头+manifest 合并请求）。
/// 爬虫从起始 URL 遍历全部子目录（ParseLinks 分流目录/mod），
/// 单目录生产者 + Channel + 10 个异步 Mod 消费者，避免递归并发调度的父子任务死锁。
/// 因此 KKEC 根目录（其下是多个 "Sideloader Modpack*" 子目录）与 AISHS2 用同一套逻辑即可覆盖，
/// 各游戏仅起点 baseUrl 不同（见 GameProfiles.*.SideloadBaseUrl，唯一事实来源）。
/// Run 可重入（每次运行新建内部状态）；Cancel 置标志位 + CancellationToken（在飞请求即时中断）。
/// 服务层 await 一律 ConfigureAwait(false)：ZIP/HTML 解析等续体不回 WPF UI 线程。
/// </summary>
public class SideloaderService : ISideloaderService
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36 Edg/111.0.1661.54";

    /// <summary>小文件阈值：≤64KB 直接整取</summary>
    private const long SmallFileThreshold = 65536;

    /// <summary>渐进抓取首块 16KB（窗口几何翻倍）/ 窗口上限 256KB</summary>
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
        public readonly CancellationTokenSource Cts = new();
        public readonly Dictionary<string, SideloadEntry> Result = new();
        public readonly object ResultLock = new();

        /// <summary>旧库条目（按相对路径索引）：Last modified 未变新的 Mod 零请求复用</summary>
        public IReadOnlyDictionary<string, KnownSideloadMod>? Known;

        public Action<string>? Log;

        // 失败/跳过计数（多线程 Interlocked）：供调用方提示并确认是否应用部分结果
        public int FailedPages;
        public int FailedMods;
        public int SkippedNoGuid;
        public int SkippedTooLarge;
        public int NotFoundMods;
        public int ReusedMods;
        public int Discovered;
        public int Processed;
    }

    /// <param name="proxy">代理串（含认证），解析失败时忽略（与原版一致）</param>
    /// <param name="baseUrl">起始 URL（各游戏不同，见 GameProfiles.SideloadBaseUrl；空则回退 HS2）</param>
    public SideloaderService(string? proxy = null, string? baseUrl = null)
    {
        // SocketsHttpHandler：EnableMultipleHttp2Connections 避免单条 h2 连接断开时
        // 十几个并发请求一起失败（HttpClientHandler 不暴露该属性）
        var handler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true };
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

    /// <summary>取消爬取：置标志位 + 取消 CancellationToken（在飞请求与 Channel 等待即时中断）</summary>
    public void Cancel()
    {
        RunState? run;
        lock (_runLock)
        {
            run = _currentRun;
            if (run != null)
                run.Cancelled = true;
        }
        run?.Cts.Cancel();
    }

    /// <summary>运行爬取。已在运行时抛 InvalidOperationException（对应原版 "sideloader is already running"）。
    /// known 为旧库条目（按相对路径索引）：Last modified 未变新的 Mod 零请求复用（增量爬取）。</summary>
    public async Task<SideloadRunResult> RunAsync(
        Action<string>? onLog = null, IProgress<SideloaderProgress>? onProgress = null,
        IReadOnlyDictionary<string, KnownSideloadMod>? known = null)
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
            run.Known = known;
            onLog?.Invoke($"Starting crawl from {_baseUrl}");
            try
            {
                await CrawlAsync(_baseUrl, run, onLog, onProgress).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (run.Cancelled)
            {
                // 用户停止：Channel 等待/请求被 CT 中断，走正常返回（部分结果由调用方决定丢弃）
            }
            int count;
            lock (run.ResultLock)
                count = run.Result.Count;
            onLog?.Invoke($"Crawl completed, found {run.Discovered} mods ({count} unique GUIDs)");

            Dictionary<string, SideloadEntry> mods;
            lock (run.ResultLock)
                mods = new Dictionary<string, SideloadEntry>(run.Result);
            return new SideloadRunResult
            {
                Mods = mods,
                DiscoveredMods = run.Discovered,
                ReusedMods = run.ReusedMods,
                FailedPages = run.FailedPages,
                FailedMods = run.FailedMods,
                SkippedNoGuid = run.SkippedNoGuid,
                SkippedTooLarge = run.SkippedTooLarge,
                NotFoundMods = run.NotFoundMods,
            };
        }
        finally
        {
            lock (_runLock)
                _currentRun = null;
            run.Cts.Dispose();
        }
    }

    // ==================== 爬取 ====================

    private async Task CrawlAsync(string pageUrl, RunState run, Action<string>? onLog, IProgress<SideloaderProgress>? onProgress)
    {
        const int modWorkerCount = 10;
        var channel = Channel.CreateUnbounded<AutoindexMod>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false,
            AllowSynchronousContinuations = false,
        });
        var modWorkers = Enumerable.Range(0, modWorkerCount)
            .Select(_ => ModWorkerAsync())
            .ToArray();

        try
        {
            var pendingDirectories = new Queue<string>();
            var visitedDirectories = new HashSet<string>(StringComparer.Ordinal);
            pendingDirectories.Enqueue(pageUrl);
            visitedDirectories.Add(pageUrl);

            // 单目录生产者串行遍历；子目录只入队，不递归等待，避免父目录与子目录相互等待。
            while (pendingDirectories.Count > 0)
            {
                run.Cts.Token.ThrowIfCancellationRequested();
                var currentUrl = pendingDirectories.Dequeue();
                onLog?.Invoke($"Processing: {currentUrl}");

                HtmlDocument? doc;
                try
                {
                    doc = await FetchDocAsync(currentUrl, run).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 目录失败 = 整棵子树缺失，计数供用户确认是否使用部分结果；继续处理其他目录。
                    Interlocked.Increment(ref run.FailedPages);
                    onLog?.Invoke($"Failed to fetch {currentUrl}: {ex.Message}");
                    continue;
                }

                var (dirs, mods) = ParseLinks(doc, currentUrl);
                foreach (var dir in dirs)
                {
                    if (visitedDirectories.Add(dir))
                        pendingDirectories.Enqueue(dir);
                }
                foreach (var mod in mods)
                {
                    Interlocked.Increment(ref run.Discovered);
                    await channel.Writer.WriteAsync(mod, run.Cts.Token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            // 异常或取消也必须关闭 Channel，并等待消费者退出，不能遗留后台任务。
            channel.Writer.TryComplete();
            await Task.WhenAll(modWorkers).ConfigureAwait(false);
        }

        async Task ModWorkerAsync()
        {
            try
            {
                await foreach (var mod in channel.Reader.ReadAllAsync(run.Cts.Token).ConfigureAwait(false))
                    await ProcessModAsync(mod).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (run.Cancelled)
            {
                // 用户停止：目录生产者关闭 Channel 后，所有消费者统一在此退出。
            }
        }

        async Task ProcessModAsync(AutoindexMod mod)
        {
            var relative = mod.Url.StartsWith(_baseUrl, StringComparison.Ordinal)
                ? mod.Url[_baseUrl.Length..]
                : mod.Url;
            try
            {
                // 增量爬取：本地记录的 Last modified 不晚于本站当前时间 → 零请求沿用旧 GUID。
                // 日期缺失或无法解析时仅在原始指纹完全一致时复用，其余情况保守重抓。
                if (run.Known != null && run.Known.TryGetValue(relative, out var known)
                    && CanReuseWithoutFetch(known, mod))
                {
                    Interlocked.Increment(ref run.ReusedMods);
                    AddResult(known.Guid);
                    return;
                }

                var (outcome, guid) = await ExtractGuidFromZipmodAsync(mod.Url, run).ConfigureAwait(false);
                switch (outcome)
                {
                    case ModOutcome.Failed:
                        Interlocked.Increment(ref run.FailedMods);
                        break;
                    case ModOutcome.NotFound:
                        Interlocked.Increment(ref run.NotFoundMods);
                        break;
                    case ModOutcome.NoGuid:
                        Interlocked.Increment(ref run.SkippedNoGuid);
                        break;
                    case ModOutcome.TooLarge:
                        Interlocked.Increment(ref run.SkippedTooLarge);
                        break;
                }

                if (guid != "")
                    AddResult(guid);
                else
                    onLog?.Invoke($"Skipping {mod.Url}: {outcome}");
            }
            finally
            {
                var processed = Interlocked.Increment(ref run.Processed);
                var discovered = Volatile.Read(ref run.Discovered);
                onProgress?.Report(new SideloaderProgress(discovered, 0, processed, run.FailedPages + run.FailedMods));
            }

            void AddResult(string guid)
            {
                lock (run.ResultLock)
                {
                    // 重复 GUID 确定性裁决：相对路径字典序较小者胜。
                    // 原"后写覆盖"由并发完成顺序决定，同一批 Mod 不同次爬取会被整理归位到不同目录
                    if (!run.Result.TryGetValue(guid, out var existing)
                        || string.CompareOrdinal(relative, existing.Url) < 0)
                        run.Result[guid] = new SideloadEntry
                        {
                            Url = relative,
                            SizeText = mod.SizeText,
                            MtimeText = mod.MtimeText,
                        };
                }
                onLog?.Invoke($"Found: {guid}");
            }
        }
    }

    /// <summary>单个 Mod 的抓取结局（404 视为远端已删除、自然淘汰，不算失败）</summary>
    private enum ModOutcome { Ok, NotFound, Failed, NoGuid, TooLarge }

    /// <summary>
    /// 瞬时失败（超时/连接重置/429/5xx）最多 3 次、500ms 起指数退避重试；
    /// 404 等确定性状态不重试。请求消息每次重建（已发送的 HttpRequestMessage 不可复用）。
    /// 重试耗尽返回 null（已记日志）；非重试类响应原样返回给调用方判断。
    /// </summary>
    private async Task<HttpResponseMessage?> SendWithRetryAsync(
        RunState run, Func<HttpRequestMessage> newRequest, string logContext,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? resp = null;
            try
            {
                using var req = newRequest();
                resp = await _client.SendAsync(req, completion, run.Cts.Token).ConfigureAwait(false);
                if ((int)resp.StatusCode is 429 or >= 500 && attempt < maxAttempts && !run.Cancelled)
                {
                    resp.Dispose();
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * (1 << (attempt - 1))), run.Cts.Token).ConfigureAwait(false);
                    continue;
                }
                return resp;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or OperationCanceledException)
            {
                resp?.Dispose();
                if (attempt >= maxAttempts || run.Cancelled)
                {
                    if (!run.Cancelled) // 用户停止不记失败日志（非错误）
                        run.Log?.Invoke($"Failed {logContext}: {ex.Message}");
                    return null;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (1 << (attempt - 1))), run.Cts.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task<HtmlDocument> FetchDocAsync(string pageUrl, RunState run)
    {
        using var resp = await SendWithRetryAsync(run, () =>
        {
            var req = NewRequest(HttpMethod.Get, pageUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            return req;
        }, pageUrl).ConfigureAwait(false) ?? throw new HttpRequestException("request failed after retries");
        if (resp.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");
        var html = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }

    private static bool CanReuseWithoutFetch(KnownSideloadMod known, AutoindexMod mod)
    {
        if (TryParseMtime(known.MtimeText, out var previous) && TryParseMtime(mod.MtimeText, out var current))
            return previous <= current;
        return known.SizeText != null && known.MtimeText != null
               && known.SizeText == mod.SizeText && known.MtimeText == mod.MtimeText;
    }

    private static bool TryParseMtime(string? text, out DateTime value) =>
        DateTime.TryParseExact(text, ["yyyy-MM-dd HH:mm", "yyyy-M-d H:mm"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    /// <summary>autoindex 列表行中的 zipmod：URL + 大小/修改时间列原文（增量指纹）</summary>
    internal sealed record AutoindexMod(string Url, string? SizeText, string? MtimeText);

    /// <summary>
    /// 解析 autoindex 页面链接（对应 Go parseLinks）。
    /// table#indexlist tr 跳过前 2 行；href 解码后拼 pageURL 去重；.zipmod 与目录分流。
    /// 同时取每行的大小/修改时间列（class 含 size/date，或 h5ai 经典 Name/Date/Size 列序），
    /// 取不到则为 null（增量比对会保守回退为正常抓取）。
    /// </summary>
    internal static (List<string> Dirs, List<AutoindexMod> Mods) ParseLinks(HtmlDocument doc, string pageUrl)
    {
        var hrefSet = new HashSet<string>();
        var modList = new List<AutoindexMod>();
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

                string? sizeText = null, mtimeText = null;
                var cells = rows[i].SelectNodes("./td");
                if (cells != null)
                {
                    foreach (var td in cells)
                    {
                        var cls = td.Attributes["class"]?.Value ?? "";
                        if (cls.Contains("date", StringComparison.OrdinalIgnoreCase))
                            mtimeText = td.InnerText.Trim();
                        else if (cls.Contains("size", StringComparison.OrdinalIgnoreCase))
                            sizeText = td.InnerText.Trim();
                    }
                    // 无 class 标记时按 h5ai 经典 Name/Date/Size 列序回退（末两列）
                    if (mtimeText == null && sizeText == null && cells.Count >= 3)
                    {
                        mtimeText = cells[^2].InnerText.Trim();
                        sizeText = cells[^1].InnerText.Trim();
                    }
                    if (sizeText == "") sizeText = null;
                    if (mtimeText == "") mtimeText = null;
                }

                foreach (var a in links)
                {
                    var href = a.Attributes["href"]?.Value;
                    if (href == null)
                        continue;
                    string decoded;
                    try
                    {
                        // HAP 的 Attribute.Value 保留 HTML 实体（如 Nonna &amp; Clara/），
                        // 不解实体直接拼 URL 会 404，整棵子树丢失。先实体解码再 URI 解码。
                        decoded = Uri.UnescapeDataString(WebUtility.HtmlDecode(href));
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
                    var url = pageUrl + decoded.Replace("#", "%23").Replace("?", "%3F");
                    if (hrefSet.Add(url) && url.EndsWith(".zipmod", StringComparison.OrdinalIgnoreCase)
                        && sizeText != "0")
                        modList.Add(new AutoindexMod(url, sizeText, mtimeText));
                }
            }
        }

        var dirs = new List<string>();
        foreach (var href in hrefSet)
        {
            if (href.EndsWith("/", StringComparison.Ordinal))
                dirs.Add(href);
        }
        return (dirs, modList);
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
        using var resp = await SendWithRetryAsync(run, () =>
        {
            var req = NewRequest(HttpMethod.Get, modUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            req.Headers.Range = new RangeHeaderValue(start, end);
            req.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            return req;
        }, $"range {modUrl}", HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if (resp is null)
            return null;

        if (resp.StatusCode == HttpStatusCode.PartialContent)
            return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.OK && start == 0)
        {
            // 整文件请求（小文件分支）：200 即为全量内容（≤64KB，内存有界）
            return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        // Range 被忽略（200 且非整取）：视为失败。
        // 若按原版照单全收，渐进抓取会把全量文件反复前插累积（16×文件×10 并发 → 数 GB）。
        // 正常支持 Range 的站点（nginx/Cloudflare）恒返回 206，此分支仅在代理干预时触发。
        return null;
    }

    /// <summary>
    /// 首请求：suffix range（bytes=-N）一次拿到尾部窗口与总大小，省掉 HEAD。
    /// 206 → 总大小取自 Content-Range；200（小文件或服务器忽略 Range）→ 仅当 Content-Length
    /// ≤64KB 才整收（防代理忽略 Range 时把大文件整个读进内存）；404/410 → NotFound（自然淘汰）。
    /// </summary>
    private async Task<(ModOutcome Outcome, byte[]? Data, long TotalSize)> FetchTailAsync(
        string modUrl, long suffixLength, RunState run)
    {
        using var resp = await SendWithRetryAsync(run, () =>
        {
            var req = NewRequest(HttpMethod.Get, modUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            req.Headers.Range = new RangeHeaderValue(null, suffixLength);
            req.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            return req;
        }, $"tail {modUrl}", HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if (resp is null)
            return (ModOutcome.Failed, null, 0);
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            return (ModOutcome.NotFound, null, 0);

        try
        {
            if (resp.StatusCode == HttpStatusCode.PartialContent)
            {
                var total = resp.Content.Headers.ContentRange?.Length;
                if (total is null or <= 0)
                {
                    run.Log?.Invoke($"Failed to parse Content-Range {modUrl}");
                    return (ModOutcome.Failed, null, 0);
                }
                var data = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                return (ModOutcome.Ok, data, total.Value);
            }

            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var length = resp.Content.Headers.ContentLength;
                if (length is > 0 and <= SmallFileThreshold)
                {
                    // 小文件：200 即为全量内容（内存有界）
                    var data = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return (ModOutcome.Ok, data, length.Value);
                }
                // 大文件 200 全量（Range 被忽略）：拒绝整收，防内存累积
                run.Log?.Invoke($"Skipping {modUrl}: server ignored suffix range (HTTP 200, length={length})");
                return (ModOutcome.Failed, null, 0);
            }

            run.Log?.Invoke($"Failed to fetch tail {modUrl}: HTTP {(int)resp.StatusCode}");
            return (ModOutcome.Failed, null, 0);
        }
        catch (Exception ex)
        {
            run.Log?.Invoke($"Failed to fetch tail {modUrl}: {ex.Message}");
            return (ModOutcome.Failed, null, 0);
        }
    }

    // ==================== GUID 提取 ====================

    /// <summary>manifest 压缩数据上限：真实 manifest.xml 仅几 KB，超过即视为解析异常</summary>
    private const uint MaxManifestCompressedSize = 4 * 1024 * 1024;

    private async Task<(ModOutcome Outcome, string Guid)> ExtractGuidFromZipmodAsync(string modUrl, RunState run)
    {
        // 首请求 suffix range：一次拿到尾部 16KB 窗口与总大小（Content-Range），省掉 HEAD
        var (outcome, tail, totalSize) = await FetchTailAsync(modUrl, ChunkSize, run).ConfigureAwait(false);
        if (outcome != ModOutcome.Ok)
            return (outcome, "");

        byte[]? data = tail;
        // 尾部窗口已覆盖全文件（≤16KB，或服务器 200 整取的小文件）：后续全部内存切片
        byte[]? wholeFile = data != null && data.Length == totalSize ? data : null;

        if (wholeFile == null && totalSize <= SmallFileThreshold)
        {
            // 小文件（16KB < size ≤ 64KB）：整取一次，后续本地头/manifest 直接切片，不再发请求
            data = await FetchRangeAsync(modUrl, 0, totalSize - 1, run).ConfigureAwait(false);
            if (data != null && data.Length == totalSize)
                wholeFile = data;
        }

        var entries = ZipRemoteReader.ReadCentralDir(data, totalSize);
        if (entries != null)
            return await ExtractFromEntriesAsync(entries, modUrl, run, wholeFile).ConfigureAwait(false);

        // 大文件：尾部窗口按 16→32→64→128→256KB 几何增长（每次新抓 chunk = 当前窗口大小，
        // 到 256KB 上限仅需 5 次请求，原 16KB 线性递增需 16 次）；新 chunk 前插累积
        if (wholeFile == null && data != null)
        {
            long window = data.Length;
            while (window < MaxWindow && window < totalSize)
            {
                var chunkSize = Math.Min(window, MaxWindow - window);
                var end = totalSize - window - 1;
                var start = Math.Max(end - chunkSize + 1, 0);
                var chunk = await FetchRangeAsync(modUrl, start, end, run).ConfigureAwait(false);
                if (chunk == null)
                    return (ModOutcome.Failed, "");
                data = Prepend(chunk, data)!; // chunk 非 null 时 Prepend 必非 null
                window = data.Length;
                entries = ZipRemoteReader.ReadCentralDir(data, totalSize);
                if (entries != null)
                    return await ExtractFromEntriesAsync(entries, modUrl, run).ConfigureAwait(false);
            }
        }

        if (data == null || data.Length == 0)
            return (ModOutcome.Failed, "");
        return (ModOutcome.NoGuid, "");
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

    private async Task<(ModOutcome Outcome, string Guid)> ExtractFromEntriesAsync(
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
            return (ModOutcome.NoGuid, "");

        // 大小统一以中央目录为准（本地头 size 可能是 0=data descriptor 或 ZIP64 哨兵）；
        // 本地头仅用于计算 dataOffset
        var compressedSize = entry.CompressedSize;

        // 防御：压缩大小异常（CD 解析错位或畸形文件）时若直接抓取/解压会撑爆内存
        if (compressedSize > MaxManifestCompressedSize)
        {
            run.Log?.Invoke($"Skipping {modUrl}: manifest compressed size {compressedSize} exceeds limit");
            return (ModOutcome.TooLarge, "");
        }
        if (compressedSize <= 0)
            return (ModOutcome.NoGuid, "");

        // 一次合并 Range 取「本地头 + 数据区」（本地 extra 预留 512B；小文件直接内存切片），
        // 比原来「201B 本地头 + manifest 数据」两次请求减半；数据不全时再对数据区补抓一次
        byte[]? block;
        if (wholeFile != null && entry.Offset < wholeFile.Length)
            block = wholeFile[(int)entry.Offset..];
        else
            block = await FetchRangeAsync(modUrl, entry.Offset,
                entry.Offset + 30 + entry.NameByteLength + 512 + compressedSize - 1, run).ConfigureAwait(false);
        if (block == null)
            return (ModOutcome.Failed, "");

        long dataOffset = 30 + entry.NameByteLength; // 解析失败回退（假设无 extra）
        if (ZipRemoteReader.TryParseLocalHeader(block, out var parsedOffset, out _))
            dataOffset = parsedOffset;

        byte[]? manifestData = dataOffset + compressedSize <= block.Length
            ? block[(int)dataOffset..(int)(dataOffset + compressedSize)]
            : await FetchRangeAsync(modUrl, entry.Offset + dataOffset,
                entry.Offset + dataOffset + compressedSize - 1, run).ConfigureAwait(false);
        var guid = ZipRemoteReader.ExtractManifestGuid(manifestData, entry.CompressionMethod);
        return guid != null ? (ModOutcome.Ok, guid) : (ModOutcome.NoGuid, "");
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
