using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>下载被取消（对应 Go 的 "download cancelled" 错误）</summary>
public class DownloadCancelledException : Exception
{
    public DownloadCancelledException() : base("download cancelled") { }
}

/// <summary>
/// 断点续传下载器。小文件使用单流；超过分段阈值的大文件通过 HTTP Range 并发分段下载。
/// 无总时长超时，靠 CancellationToken 取消；读停滞 60s 中断；进度节流 200ms 或 64KB。
/// </summary>
public class DownloaderService
{
    private const int BufferSize = 32 * 1024;
    private const int MaxRetryCount = 3;
    private const long DefaultSegmentSize = 50L * 1024 * 1024;
    private const int DefaultMaxConcurrency = 4;
    private const long MetadataCheckpointSize = 1024 * 1024;

    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _client;
    private readonly long _segmentSize;
    private readonly int _maxConcurrency;

    /// <summary>proxyUrl 非法时抛异常（对应 Go NewDownloader 返回错误）</summary>
    public DownloaderService(string? proxyUrl = null)
        : this(proxyUrl, DefaultSegmentSize, DefaultMaxConcurrency) { }

    internal DownloaderService(string? proxyUrl, long segmentSize, int maxConcurrency)
    {
        if (segmentSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(segmentSize));
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        _segmentSize = segmentSize;
        _maxConcurrency = maxConcurrency;
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
            handler.UseProxy = false;
        }
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
    public Task DownloadAsync(
        string fileUrl, string outputPath, bool resume,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default) =>
        DownloadCoreAsync(fileUrl, outputPath, resume, null, progress, ct);

    internal Task DownloadWithValidationAsync(
        string fileUrl, string outputPath, bool resume, Action<string> validateBeforeCommit,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(validateBeforeCommit);
        return DownloadCoreAsync(fileUrl, outputPath, resume, validateBeforeCommit, progress, ct);
    }

    private async Task DownloadCoreAsync(
        string fileUrl, string outputPath, bool resume, Action<string>? validateBeforeCommit,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        progress?.Report(new DownloadProgress { Type = DownloadMessageType.Info, Message = $"Starting download: {fileUrl}" });

        try
        {
            if (!resume)
                DeleteSegmentedArtifacts(outputPath);

            var legacyPartial = resume && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
            if (!legacyPartial)
            {
                var state = resume ? LoadSegmentedState(outputPath) : null;
                RemoteFileInfo? remote = null;
                try
                {
                    remote = await ProbeRemoteAsync(fileUrl, state is not null, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ErrorLog.Log($"Download probe failed, using single stream: {fileUrl}: {ex.Message}");
                }

                if (remote is not null && remote.TotalSize > _segmentSize)
                {
                    if (state is not null && !Matches(state, fileUrl, remote))
                    {
                        DeleteSegmentedArtifacts(outputPath);
                        state = null;
                    }
                    if (state is null)
                        DeleteSegmentedArtifacts(outputPath);

                    try
                    {
                        var current = await DownloadSegmentedAsync(
                            fileUrl, outputPath, remote, state, progress, ct).ConfigureAwait(false);
                        var partPath = GetPartPath(outputPath);
                        try
                        {
                            validateBeforeCommit?.Invoke(partPath);
                        }
                        catch
                        {
                            TryDeleteSegmentedArtifacts(outputPath);
                            throw;
                        }

                        File.Move(partPath, outputPath, overwrite: true);
                        DeleteSegmentedArtifacts(outputPath);
                        ReportComplete(outputPath, current, progress);
                        return;
                    }
                    catch (RangeNotSupportedException)
                    {
                        DeleteSegmentedArtifacts(outputPath);
                        progress?.Report(new DownloadProgress
                        {
                            Type = DownloadMessageType.Info,
                            Message = "Server ignored a segment range, restarting with a single stream",
                        });
                    }
                }
                else if (state is not null && remote is not null)
                {
                    DeleteSegmentedArtifacts(outputPath);
                }
            }

            var downloaded = await DownloadSingleAsync(fileUrl, outputPath, resume, progress, ct).ConfigureAwait(false);
            validateBeforeCommit?.Invoke(outputPath);
            DeleteSegmentedArtifacts(outputPath);
            ReportComplete(outputPath, downloaded, progress);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new DownloadCancelledException();
        }
        catch (OperationCanceledException ex)
        {
            throw new HttpRequestException("分段下载因其他分段失败而中止", ex);
        }
    }

    private async Task<RemoteFileInfo?> ProbeRemoteAsync(
        string fileUrl, bool hasSegmentedState, CancellationToken ct)
    {
        string? etag = null;
        DateTimeOffset? lastModified = null;
        long headLength = -1;
        using (var head = NewRequest(HttpMethod.Head, fileUrl))
        using (var response = await _client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            if (response.StatusCode != HttpStatusCode.OK)
                return null;
            headLength = response.Content.Headers.ContentLength ?? -1;
            if (!hasSegmentedState && headLength <= _segmentSize)
                return null;
            etag = response.Headers.ETag?.ToString();
            lastModified = response.Content.Headers.LastModified;
        }

        using var probe = NewRequest(HttpMethod.Get, fileUrl);
        probe.Headers.Range = new RangeHeaderValue(0, 0);
        using var probeResponse = await _client.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (probeResponse.StatusCode != HttpStatusCode.PartialContent)
            return null;

        var range = probeResponse.Content.Headers.ContentRange;
        if (range is null || !range.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase)
            || range.From != 0 || range.To != 0 || range.Length is null or <= 0)
            return null;
        if (headLength > 0 && range.Length.Value != headLength)
            return null;

        return new RemoteFileInfo(
            range.Length.Value,
            probeResponse.Headers.ETag?.ToString() ?? etag,
            probeResponse.Content.Headers.LastModified ?? lastModified);
    }

    private async Task<long> DownloadSegmentedAsync(
        string fileUrl, string outputPath, RemoteFileInfo remote, MultiDownloadState? existing,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var state = existing ?? new MultiDownloadState
        {
            Url = fileUrl,
            TotalSize = remote.TotalSize,
            SegmentSize = _segmentSize,
            ETag = remote.ETag,
            LastModified = remote.LastModified,
            Downloaded = new long[GetSegmentCount(remote.TotalSize, _segmentSize)],
        };
        var partPath = GetPartPath(outputPath);
        await using var file = new FileStream(partPath, new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.RandomAccess,
        });
        file.SetLength(state.TotalSize);

        var initial = state.Downloaded.Sum();
        var tracker = new ProgressTracker(initial, state.TotalSize, progress);
        var session = new SegmentedDownloadSession(state, GetMetadataPath(outputPath), tracker);
        session.SaveMetadata();

        var pending = new List<int>();
        for (var i = 0; i < state.Downloaded.Length; i++)
        {
            if (state.Downloaded[i] < GetSegmentLength(state, i))
                pending.Add(i);
        }

        await Parallel.ForEachAsync(pending, new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxConcurrency,
            CancellationToken = ct,
        }, (index, token) => new ValueTask(DownloadSegmentAsync(fileUrl, file, session, index, token))).ConfigureAwait(false);

        await file.FlushAsync(ct).ConfigureAwait(false);
        file.Flush(flushToDisk: true);
        return state.TotalSize;
    }

    private async Task DownloadSegmentAsync(
        string fileUrl, FileStream file, SegmentedDownloadSession session, int index, CancellationToken ct)
    {
        try
        {
            var state = session.State;
            var segmentLength = GetSegmentLength(state, index);
            var attempt = 0;
            while (session.GetDownloaded(index) < segmentLength)
            {
                try
                {
                    await DownloadSegmentPieceAsync(fileUrl, file, session, index, ct).ConfigureAwait(false);
                }
                catch (RangeNotSupportedException)
                {
                    throw;
                }
                catch (Exception ex) when (IsTransient(ex) && attempt < MaxRetryCount - 1)
                {
                    attempt++;
                    await Task.Delay(TimeSpan.FromSeconds(1 << (attempt - 1)), ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            try
            {
                session.SaveMetadata();
            }
            catch (Exception ex) when (ct.IsCancellationRequested)
            {
                ErrorLog.Log($"Failed to checkpoint cancelled download: {fileUrl}: {ex.Message}");
            }
        }
    }

    private async Task DownloadSegmentPieceAsync(
        string fileUrl, FileStream file, SegmentedDownloadSession session, int index, CancellationToken ct)
    {
        var state = session.State;
        var confirmed = session.GetDownloaded(index);
        var segmentStart = (long)index * state.SegmentSize;
        var start = segmentStart + confirmed;
        var end = Math.Min(segmentStart + state.SegmentSize, state.TotalSize) - 1;
        var expected = end - start + 1;

        using var request = NewRequest(HttpMethod.Get, fileUrl);
        request.Headers.Range = new RangeHeaderValue(start, end);
        if (state.ETag is not null && EntityTagHeaderValue.TryParse(state.ETag, out var tag) && !tag.IsWeak)
            request.Headers.IfRange = new RangeConditionHeaderValue(tag);
        else if (state.LastModified is not null)
            request.Headers.IfRange = new RangeConditionHeaderValue(state.LastModified.Value);

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.OK)
            throw new RangeNotSupportedException();
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new HttpRequestException(
                $"server returned status {(int)response.StatusCode}: {response.ReasonPhrase}", null, response.StatusCode);

        var contentRange = response.Content.Headers.ContentRange;
        if (contentRange is null || !contentRange.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase)
            || contentRange.From != start || contentRange.To != end || contentRange.Length != state.TotalSize)
            throw new InvalidDataException("分段响应的 Content-Range 与请求不一致");
        if (response.Content.Headers.ContentLength is long contentLength && contentLength != expected)
            throw new InvalidDataException("分段响应的 Content-Length 与请求不一致");

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var offset = start;
        var remaining = expected;
        try
        {
            while (remaining > 0)
            {
                stallCts.CancelAfter(StallTimeout);
                int count;
                try
                {
                    count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), stallCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException("下载停滞：60 秒未收到数据");
                }
                if (count == 0)
                    throw new EndOfStreamException("分段响应提前结束");

                await RandomAccess.WriteAsync(file.SafeFileHandle, buffer.AsMemory(0, count), offset, ct).ConfigureAwait(false);
                session.Advance(index, count);
                offset += count;
                remaining -= count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<long> DownloadSingleAsync(
        string fileUrl, string outputPath, bool resume,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        long startPos = 0;
        if (resume && File.Exists(outputPath))
        {
            startPos = new FileInfo(outputPath).Length;
            progress?.Report(new DownloadProgress { Type = DownloadMessageType.Info, Message = $"Resuming download from byte {startPos}" });
        }

        using var req = NewRequest(HttpMethod.Get, fileUrl);
        if (startPos > 0)
            req.Headers.Range = new RangeHeaderValue(startPos, null);

        HttpResponseMessage resp;
        try
        {
            resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new DownloadCancelledException();
        }

        if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && startPos > 0)
        {
            resp.Dispose();
            progress?.Report(new DownloadProgress { Type = DownloadMessageType.Info, Message = "Server rejected resume range, restarting download" });
            return await DownloadSingleAsync(fileUrl, outputPath, resume: false, progress, ct).ConfigureAwait(false);
        }

        using (resp)
        {
            if (resp.StatusCode != HttpStatusCode.OK && resp.StatusCode != HttpStatusCode.PartialContent)
                throw new HttpRequestException($"server returned status {(int)resp.StatusCode}: {resp.ReasonPhrase}");

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

            await using var file = new FileStream(outputPath, new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
            if (startPos > 0)
                file.Position = file.Length;
            else
                file.SetLength(0);

            if (startPos > 0 && resp.StatusCode == HttpStatusCode.OK)
            {
                progress?.Report(new DownloadProgress { Type = DownloadMessageType.Info, Message = "Server does not support resume, restarting download" });
                startPos = 0;
                file.SetLength(0);
                file.Position = 0;
            }

            var tracker = new ProgressTracker(startPos, totalSize, progress);
            using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var buf = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    while (true)
                    {
                        stallCts.CancelAfter(StallTimeout);
                        int n;
                        try
                        {
                            n = await src.ReadAsync(buf.AsMemory(0, BufferSize), stallCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            throw new TimeoutException("下载停滞：60 秒未收到数据");
                        }
                        if (n == 0)
                            break;
                        await file.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                        tracker.Update(n);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }
            }
            catch (OperationCanceledException)
            {
                throw new DownloadCancelledException();
            }

            return tracker.Current;
        }
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string fileUrl)
    {
        var request = new HttpRequestMessage(method, fileUrl)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        request.Headers.TryAddWithoutValidation("User-Agent", "hs2-tools-downloader/1.0");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        return request;
    }

    private static bool IsTransient(Exception ex) =>
        ex is IOException or TimeoutException
        || ex is HttpRequestException http && (http.StatusCode is null
            or HttpStatusCode.TooManyRequests
            or >= HttpStatusCode.InternalServerError);

    private MultiDownloadState? LoadSegmentedState(string outputPath)
    {
        var partPath = GetPartPath(outputPath);
        var metadataPath = GetMetadataPath(outputPath);
        if (!File.Exists(partPath) || !File.Exists(metadataPath))
            return null;

        try
        {
            var state = JsonSerializer.Deserialize<MultiDownloadState>(File.ReadAllText(metadataPath));
            if (state is null || state.TotalSize <= _segmentSize || state.SegmentSize != _segmentSize
                || state.Downloaded is null || state.Downloaded.Length != GetSegmentCount(state.TotalSize, _segmentSize)
                || new FileInfo(partPath).Length != state.TotalSize)
                throw new InvalidDataException("invalid segmented download metadata");

            for (var i = 0; i < state.Downloaded.Length; i++)
            {
                if (state.Downloaded[i] < 0 || state.Downloaded[i] > GetSegmentLength(state, i))
                    throw new InvalidDataException("invalid segmented download progress");
            }
            return state;
        }
        catch (Exception ex)
        {
            ErrorLog.Log($"Failed to load segmented download state: {metadataPath}: {ex.Message}");
            return null;
        }
    }

    private static bool Matches(MultiDownloadState state, string fileUrl, RemoteFileInfo remote) =>
        state.Url == fileUrl && state.TotalSize == remote.TotalSize
        && state.ETag == remote.ETag && state.LastModified == remote.LastModified;

    private static int GetSegmentCount(long totalSize, long segmentSize) =>
        checked((int)((totalSize - 1) / segmentSize + 1));

    private static long GetSegmentLength(MultiDownloadState state, int index) =>
        Math.Min(state.SegmentSize, state.TotalSize - (long)index * state.SegmentSize);

    private static string GetPartPath(string outputPath) => outputPath + ".part";
    private static string GetMetadataPath(string outputPath) => GetPartPath(outputPath) + ".json";

    private static void DeleteSegmentedArtifacts(string outputPath)
    {
        File.Delete(GetPartPath(outputPath));
        File.Delete(GetMetadataPath(outputPath));
        File.Delete(GetMetadataPath(outputPath) + ".tmp");
    }

    private static void TryDeleteSegmentedArtifacts(string outputPath)
    {
        try
        {
            DeleteSegmentedArtifacts(outputPath);
        }
        catch (Exception ex)
        {
            ErrorLog.Log($"Failed to delete segmented download artifacts: {outputPath}: {ex.Message}");
        }
    }

    private static void ReportComplete(string outputPath, long current, IProgress<DownloadProgress>? progress) =>
        progress?.Report(new DownloadProgress
        {
            Type = DownloadMessageType.Complete,
            Path = outputPath,
            Total = current,
            Message = "Download completed successfully",
        });

    /// <summary>解析 Content-Range 头获取总大小（对应 Go fmt.Sscanf(contentRange, "bytes %*d-%*d/%d", &total)）</summary>
    internal static long ParseContentRangeTotal(string contentRange)
    {
        var m = Regex.Match(contentRange, @"^bytes\s*\d+-\d+/(\d+)");
        return m.Success && long.TryParse(m.Groups[1].Value, out var total) ? total : -1;
    }

    private sealed record RemoteFileInfo(long TotalSize, string? ETag, DateTimeOffset? LastModified);

    private sealed class MultiDownloadState
    {
        public string Url { get; set; } = "";
        public long TotalSize { get; set; }
        public long SegmentSize { get; set; }
        public string? ETag { get; set; }
        public DateTimeOffset? LastModified { get; set; }
        public long[] Downloaded { get; set; } = [];
    }

    private sealed class SegmentedDownloadSession
    {
        private readonly string _metadataPath;
        private readonly ProgressTracker _tracker;
        private readonly long[] _lastSaved;
        private readonly object _sync = new();

        public SegmentedDownloadSession(MultiDownloadState state, string metadataPath, ProgressTracker tracker)
        {
            State = state;
            _metadataPath = metadataPath;
            _tracker = tracker;
            _lastSaved = state.Downloaded.ToArray();
        }

        public MultiDownloadState State { get; }

        public long GetDownloaded(int index)
        {
            lock (_sync)
                return State.Downloaded[index];
        }

        public void Advance(int index, int count)
        {
            lock (_sync)
            {
                State.Downloaded[index] += count;
                if (State.Downloaded[index] - _lastSaved[index] >= MetadataCheckpointSize)
                    SaveMetadataLocked();
            }
            _tracker.Update(count);
        }

        public void SaveMetadata()
        {
            lock (_sync)
                SaveMetadataLocked();
        }

        private void SaveMetadataLocked()
        {
            var tempPath = _metadataPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(State));
            File.Move(tempPath, _metadataPath, overwrite: true);
            Array.Copy(State.Downloaded, _lastSaved, State.Downloaded.Length);
        }
    }

    private sealed class RangeNotSupportedException : Exception;

    private sealed class ProgressTracker
    {
        private readonly long _startPos;
        private readonly long _total;
        private readonly IProgress<DownloadProgress>? _progress;
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        private readonly object _sync = new();
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

        public long Current
        {
            get
            {
                lock (_sync)
                    return _startPos + _downloaded;
            }
        }

        public void Update(int n)
        {
            lock (_sync)
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
        }

        private void SendProgress()
        {
            var current = _startPos + _downloaded;
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
