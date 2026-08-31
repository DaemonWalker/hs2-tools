using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>下载被取消（对应 Go 的 "download cancelled" 错误）</summary>
public class DownloadCancelledException : Exception
{
    public DownloadCancelledException() : base("download cancelled") { }
}

/// <summary>
/// 断点续传下载器（Go internal/downloader 的 1:1 移植）。
/// 无超时，靠 CancellationToken 取消；32KB 缓冲；进度节流 200ms 或 64KB。
/// </summary>
public class DownloaderService
{
    private const int BufferSize = 32 * 1024;

    private readonly HttpClient _client;

    /// <summary>proxyUrl 非法时抛异常（对应 Go NewDownloader 返回错误）</summary>
    public DownloaderService(string? proxyUrl = null)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrEmpty(proxyUrl))
        {
            try
            {
                handler.Proxy = ProxyHelper.BuildProxy(proxyUrl);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"invalid proxy URL: {ex.Message}", ex);
            }
        }
        else
        {
            handler.UseProxy = false; // 与 Go 一致：未配置代理时直连（不用系统代理）
        }
        // Go: Timeout 0（无超时）。优先 HTTP/2（与 Go Transport 自动协商一致），回退 HTTP/1.1
        _client = new HttpClient(handler)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
    }

    /// <summary>
    /// 执行下载，通过 progress 推送进度。
    /// 取消时抛 <see cref="DownloadCancelledException"/>。
    /// </summary>
    public async Task DownloadAsync(
        string fileUrl, string outputPath, bool resume,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        // 确保输出目录存在
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        progress?.Report(new DownloadProgress { Type = DownloadMessageType.Info, Message = $"Starting download: {fileUrl}" });

        // 检查是否已存在部分文件
        long startPos = 0;
        if (resume && File.Exists(outputPath))
        {
            startPos = new FileInfo(outputPath).Length;
            progress?.Report(new DownloadProgress { Type = DownloadMessageType.Info, Message = $"Resuming download from byte {startPos}" });
        }

        // 创建 HTTP 请求（优先 HTTP/2，与 Go Transport 自动协商一致）
        using var req = new HttpRequestMessage(HttpMethod.Get, fileUrl)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        if (startPos > 0)
            req.Headers.Range = new RangeHeaderValue(startPos, null);
        req.Headers.TryAddWithoutValidation("User-Agent", "hs2-tools-downloader/1.0");

        // 发送请求
        HttpResponseMessage resp;
        try
        {
            resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException)
        {
            throw new DownloadCancelledException();
        }

        using (resp)
        {
            // 检查响应状态
            if (resp.StatusCode != HttpStatusCode.OK && resp.StatusCode != HttpStatusCode.PartialContent)
                throw new HttpRequestException($"server returned status {(int)resp.StatusCode}: {resp.ReasonPhrase}");

            // 获取文件总大小
            long totalSize = -1;
            if (resp.StatusCode == HttpStatusCode.PartialContent)
            {
                if (resp.Content.Headers.TryGetValues("Content-Range", out var values))
                    totalSize = ParseContentRangeTotal(values.FirstOrDefault() ?? "");
            }
            else
            {
                startPos = 0;
                totalSize = resp.Content.Headers.ContentLength ?? -1;
            }

            // 打开文件（可定位流手动模拟 Go 的 O_APPEND / O_TRUNC，避免 Append 模式不可 Seek 的问题）
            await using var file = new FileStream(outputPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            if (startPos > 0)
                file.Position = file.Length;
            else
                file.SetLength(0);

            // 如果服务器不支持断点续传但文件已存在，重新开始下载。
            // 注意：该分支在原版 Go 代码中即不可达（200 响应时 startPos 已在上方被重置为 0），
            // 保留以复刻原版结构；实际的重开行为由上方的 SetLength(0) 完成。
            if (startPos > 0 && resp.StatusCode == HttpStatusCode.OK)
            {
                progress?.Report(new DownloadProgress { Type = DownloadMessageType.Info, Message = "Server does not support resume, restarting download" });
                startPos = 0;
                file.SetLength(0);
                file.Position = 0;
            }

            var tracker = new ProgressTracker(startPos, totalSize, progress);
            try
            {
                await using var src = await resp.Content.ReadAsStreamAsync(ct);

                var buf = new byte[BufferSize];
                while (true)
                {
                    var n = await src.ReadAsync(buf, ct);
                    if (n == 0)
                        break;
                    await file.WriteAsync(buf.AsMemory(0, n), ct);
                    tracker.Update(n);
                }
            }
            catch (OperationCanceledException)
            {
                throw new DownloadCancelledException();
            }

            // 发送完成消息（Total 字段按原版填 tracker.current()）
            progress?.Report(new DownloadProgress
            {
                Type = DownloadMessageType.Complete,
                Path = outputPath,
                Total = tracker.Current,
                Message = "Download completed successfully",
            });
        }
    }

    /// <summary>解析 Content-Range 头获取总大小（对应 Go fmt.Sscanf(contentRange, "bytes %*d-%*d/%d", &total)）</summary>
    internal static long ParseContentRangeTotal(string contentRange)
    {
        var m = Regex.Match(contentRange, @"^bytes\s*\d+-\d+/(\d+)");
        return m.Success && long.TryParse(m.Groups[1].Value, out var total) ? total : -1;
    }

    /// <summary>进度追踪器：每 200ms 或每下载 64KB 上报一次；速度只计本次会话下载量</summary>
    private sealed class ProgressTracker
    {
        private readonly long _startPos;
        private readonly long _total;
        private readonly IProgress<DownloadProgress>? _progress;
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        private TimeSpan _lastUpdate;
        private long _downloaded;
        private long _lastDownloaded;

        public ProgressTracker(long startPos, long total, IProgress<DownloadProgress>? progress)
        {
            _startPos = startPos;
            _total = total;
            _progress = progress;
            _lastUpdate = _sw.Elapsed;
        }

        public long Current => _startPos + _downloaded;

        public void Update(int n)
        {
            _downloaded += n;
            var now = _sw.Elapsed;
            if (now - _lastUpdate > TimeSpan.FromMilliseconds(200) || _downloaded - _lastDownloaded > 64 * 1024)
            {
                SendProgress();
                _lastUpdate = now;
                _lastDownloaded = _downloaded;
            }
        }

        private void SendProgress()
        {
            var current = Current;
            var elapsed = _sw.Elapsed.TotalSeconds;
            var speed = elapsed > 0 ? _downloaded / elapsed : 0;
            var percent = _total > 0 ? (double)current / _total * 100 : 0;

            _progress?.Report(new DownloadProgress
            {
                Type = DownloadMessageType.Progress,
                Downloaded = current,
                Total = _total,
                Speed = speed,
                Percent = percent,
            });
        }
    }
}
