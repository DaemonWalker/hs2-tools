using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>
/// 全局下载管理器（对应 app.go 的下载编排：activeDownloads + 事件）。
/// 单例；任务不持久化；关闭窗口不中断下载。
/// </summary>
public class DownloadManager
{
    /// <summary>下载 URL 拼接规则：{baseURL}{相对路径} → {dir}/{name}.zipmod</summary>
    public const string BaseUrl = "https://sideload.betterrepack.com/download/AISHS2/";

    private readonly ConcurrentDictionary<string, DownloadTask> _tasks = new();
    private readonly Queue<string> _queue = new();
    private readonly object _sync = new();
    private readonly Func<string?> _proxyProvider;
    private readonly string _baseUrl;
    private DownloaderService? _downloader;
    private string? _downloaderProxy;

    /// <param name="proxyProvider">代理串提供者（通常为 ConfigService.GetProxyString），每次启动下载时取值</param>
    /// <param name="baseUrl">下载基础 URL（测试可注入本地服务器；生产用默认）</param>
    public DownloadManager(Func<string?>? proxyProvider = null, string? baseUrl = null)
    {
        _proxyProvider = proxyProvider ?? (() => null);
        _baseUrl = baseUrl ?? BaseUrl;
    }

    /// <summary>新任务已加入</summary>
    public event EventHandler<DownloadTask>? TaskAdded;

    /// <summary>任务进度/字段变化（Info/Progress 消息）</summary>
    public event EventHandler<DownloadTask>? TaskProgress;

    /// <summary>任务到达终态（Completed/Failed/Cancelled）</summary>
    public event EventHandler<DownloadTask>? TaskFinished;

    public IReadOnlyCollection<DownloadTask> Tasks => _tasks.Values.ToList();

    /// <summary>
    /// 把 GUID 转为安全的 Windows 文件名。真实 GUID 可能含 : /（如 "DL_..._https://pixiv.me/..."），
    /// 直接 Path.Combine 会下载失败、生成意外子目录，甚至被 ..\ 逃逸出 mods 目录。
    /// GUID 只作任务 Id 与显示名；清洗改变了名字时追加短哈希，避免不同 GUID 清洗后重名。
    /// </summary>
    internal static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 || c is '/' or '\\' ? '_' : c);

        var result = sb.ToString().Trim().TrimEnd('.');
        if (result.Length == 0 || result is "." or "..")
            result = "_";

        // Windows 保留名（含 COM1..COM9 / LPT1..LPT9）
        var baseName = result.Split('.')[0];
        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (baseName.Length == 4 && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && baseName[3] is >= '1' and <= '9'))
            result = "_" + result;

        if (result.Length > 100)
            result = result[..100];

        if (!string.Equals(result, name, StringComparison.Ordinal))
            result += "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8];
        return result;
    }

    /// <summary>
    /// 将下载加入全局串行队列。同名任务等待中或下载中时拒绝重复触发（返回 false）。
    /// baseUrl 为空时用构造默认值；多游戏场景由调用方传当前游戏档案的 SideloadBaseUrl。
    /// </summary>
    public bool StartDownload(string name, string relativeUrl, string dir, string? baseUrl = null)
    {
        DownloadTask task;
        lock (_sync)
        {
            if (_tasks.TryGetValue(name, out var existing) &&
                existing.Status is DownloadTaskStatus.Pending or DownloadTaskStatus.Downloading)
                return false;

            task = new DownloadTask
            {
                Id = name,
                Url = (baseUrl ?? _baseUrl) + relativeUrl,
                OutputPath = Path.Combine(dir, SanitizeFileName(name) + ".zipmod"),
                Status = DownloadTaskStatus.Pending,
                Cts = new CancellationTokenSource(),
            };
            _tasks[name] = task;
            _queue.Enqueue(name);
        }

        TaskAdded?.Invoke(this, task);
        StartNext();
        return true;
    }

    /// <summary>取消等待中或下载中的任务</summary>
    public bool Cancel(string name)
    {
        DownloadTask task;
        var cancelPending = false;
        lock (_sync)
        {
            if (!_tasks.TryGetValue(name, out task!) ||
                task.Status is not (DownloadTaskStatus.Pending or DownloadTaskStatus.Downloading))
                return false;

            if (task.Status == DownloadTaskStatus.Pending)
            {
                task.Status = DownloadTaskStatus.Cancelled;
                cancelPending = true;
            }
        }

        if (cancelPending)
            TaskFinished?.Invoke(this, task);
        else
            task.Cts?.Cancel();
        return true;
    }

    /// <summary>全部取消</summary>
    public void CancelAll()
    {
        List<DownloadTask> pending = new();
        List<CancellationTokenSource> downloading = new();
        lock (_sync)
        {
            foreach (var task in _tasks.Values)
            {
                if (task.Status == DownloadTaskStatus.Pending)
                {
                    task.Status = DownloadTaskStatus.Cancelled;
                    pending.Add(task);
                }
                else if (task.Status == DownloadTaskStatus.Downloading && task.Cts is not null)
                {
                    downloading.Add(task.Cts);
                }
            }
        }

        foreach (var task in pending)
            TaskFinished?.Invoke(this, task);
        foreach (var cts in downloading)
            cts.Cancel();
    }

    /// <summary>重试（非等待/下载中的任务，断点续传）</summary>
    public bool Retry(string name)
    {
        DownloadTask task;
        lock (_sync)
        {
            if (!_tasks.TryGetValue(name, out task!) ||
                task.Status is DownloadTaskStatus.Pending or DownloadTaskStatus.Downloading)
                return false;

            task.Status = DownloadTaskStatus.Pending;
            task.ErrorMessage = null;
            task.Downloaded = 0;
            task.Total = -1;
            task.Speed = 0;
            task.Percent = 0;
            task.Cts?.Dispose();
            task.Cts = new CancellationTokenSource();
            _queue.Enqueue(name);
        }

        TaskProgress?.Invoke(this, task);
        StartNext();
        return true;
    }

    /// <summary>清除所有非等待/下载中的任务</summary>
    public int ClearFinished()
    {
        var removed = 0;
        lock (_sync)
        {
            foreach (var (key, task) in _tasks)
            {
                if (task.Status is not (DownloadTaskStatus.Pending or DownloadTaskStatus.Downloading) &&
                    _tasks.TryRemove(key, out _))
                    removed++;
            }
        }
        return removed;
    }

    private void StartNext()
    {
        DownloadTask? next = null;
        lock (_sync)
        {
            if (_tasks.Values.Any(t => t.Status == DownloadTaskStatus.Downloading))
                return;

            while (_queue.Count > 0)
            {
                var id = _queue.Dequeue();
                if (_tasks.TryGetValue(id, out var task) && task.Status == DownloadTaskStatus.Pending)
                {
                    task.Status = DownloadTaskStatus.Downloading;
                    next = task;
                    break;
                }
            }
        }

        if (next is null)
            return;
        TaskProgress?.Invoke(this, next);
        _ = RunAsync(next);
    }

    private async Task RunAsync(DownloadTask task)
    {
        try
        {
            // 确保目录存在
            var dir = Path.GetDirectoryName(task.OutputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var downloader = GetOrCreateDownloader();
            var progress = new Progress<DownloadProgress>(p => OnProgress(task, p));
            await downloader.DownloadAsync(task.Url, task.OutputPath, resume: true, progress, task.Cts!.Token);
            ValidateZipmod(task.OutputPath); // 200 的 HTML 错误页/损坏文件不得标记完成

            // 完成消息已在 OnProgress 中处理
        }
        catch (DownloadCancelledException)
        {
            task.Status = DownloadTaskStatus.Cancelled;
            TaskFinished?.Invoke(this, task);
        }
        catch (OperationCanceledException)
        {
            task.Status = DownloadTaskStatus.Cancelled;
            TaskFinished?.Invoke(this, task);
        }
        catch (Exception ex)
        {
            task.Status = DownloadTaskStatus.Failed;
            task.ErrorMessage = ex.Message;
            ErrorLog.Log($"Download failed: {task.Id}: {ex.Message}"); // 行内文案不变，失败留痕
            TaskFinished?.Invoke(this, task);
        }
        finally
        {
            StartNext();
        }
    }

    /// <summary>复用下载器（HttpClient 连接/TLS 复用，不再每任务新建）；代理设置变化时重建</summary>
    private DownloaderService GetOrCreateDownloader()
    {
        var proxy = _proxyProvider();
        lock (_sync)
        {
            if (_downloader == null || _downloaderProxy != proxy)
            {
                _downloader = new DownloaderService(proxy);
                _downloaderProxy = proxy;
            }
            return _downloader;
        }
    }

    /// <summary>下载完成后的完整性校验：PK 头 + ZipArchive 可读 + 含 manifest.xml；
    /// 失败删除文件（不留在 mods 目录被当成本地已有）并抛出，由 RunAsync 标记任务失败</summary>
    private static void ValidateZipmod(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 4)
                throw new InvalidDataException("文件过小");
            Span<byte> sig = stackalloc byte[4];
            fs.ReadExactly(sig);
            if (!sig.SequenceEqual("PK\x03\x04"u8))
                throw new InvalidDataException("不是有效的 zip 文件（可能是服务器错误页）");
            fs.Position = 0;
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            if (archive.GetEntry("manifest.xml") is null)
                throw new InvalidDataException("zip 中缺少 manifest.xml");
        }
        catch (Exception ex)
        {
            try { File.Delete(path); } catch { /* 删除失败保留现场，错误仍会上报 */ }
            throw new InvalidDataException($"zipmod 校验失败：{ex.Message}", ex);
        }
    }

    private void OnProgress(DownloadTask task, DownloadProgress p)
    {
        switch (p.Type)
        {
            case DownloadMessageType.Progress:
                task.Downloaded = p.Downloaded;
                task.Total = p.Total;
                task.Speed = p.Speed;
                task.Percent = p.Percent;
                TaskProgress?.Invoke(this, task);
                break;
            case DownloadMessageType.Complete:
                task.Status = DownloadTaskStatus.Completed;
                task.Downloaded = p.Total; // 原版 complete 消息 Total = 当前已下载总量
                if (task.Total > 0)
                    task.Percent = 100;
                TaskFinished?.Invoke(this, task);
                break;
            case DownloadMessageType.Info:
                TaskProgress?.Invoke(this, task);
                break;
        }
    }
}
