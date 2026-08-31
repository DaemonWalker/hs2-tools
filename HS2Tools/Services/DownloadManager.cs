using System.Collections.Concurrent;
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
    private readonly Func<string?> _proxyProvider;
    private readonly string _baseUrl;

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
    /// 触发下载。同名任务正在下载时拒绝重复触发（返回 false），
    /// 避免原版覆盖 activeDownloads 条目导致旧任务失控的问题。
    /// </summary>
    public bool StartDownload(string name, string relativeUrl, string dir)
    {
        if (_tasks.TryGetValue(name, out var existing) && existing.Status == DownloadTaskStatus.Downloading)
            return false;

        var task = new DownloadTask
        {
            Id = name,
            Url = _baseUrl + relativeUrl,
            OutputPath = Path.Combine(dir, name + ".zipmod"),
            Status = DownloadTaskStatus.Downloading,
            Cts = new CancellationTokenSource(),
        };
        _tasks[name] = task;
        TaskAdded?.Invoke(this, task);

        _ = RunAsync(task);
        return true;
    }

    /// <summary>取消下载（对应 CancelDownload）</summary>
    public bool Cancel(string name)
    {
        if (_tasks.TryGetValue(name, out var task) && task.Status == DownloadTaskStatus.Downloading)
        {
            task.Cts?.Cancel();
            return true;
        }
        return false;
    }

    /// <summary>全部取消</summary>
    public void CancelAll()
    {
        foreach (var task in _tasks.Values)
        {
            if (task.Status == DownloadTaskStatus.Downloading)
                task.Cts?.Cancel();
        }
    }

    /// <summary>重试（非下载中的任务，断点续传）</summary>
    public bool Retry(string name)
    {
        if (!_tasks.TryGetValue(name, out var task) || task.Status == DownloadTaskStatus.Downloading)
            return false;

        task.Status = DownloadTaskStatus.Downloading;
        task.ErrorMessage = null;
        task.Downloaded = 0;
        task.Total = -1;
        task.Speed = 0;
        task.Percent = 0;
        task.Cts?.Dispose();
        task.Cts = new CancellationTokenSource();
        TaskProgress?.Invoke(this, task);

        _ = RunAsync(task);
        return true;
    }

    /// <summary>清除所有非下载中的任务</summary>
    public int ClearFinished()
    {
        var removed = 0;
        foreach (var (key, task) in _tasks)
        {
            if (task.Status != DownloadTaskStatus.Downloading && _tasks.TryRemove(key, out _))
                removed++;
        }
        return removed;
    }

    private async Task RunAsync(DownloadTask task)
    {
        try
        {
            // 确保目录存在
            var dir = Path.GetDirectoryName(task.OutputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var downloader = new DownloaderService(_proxyProvider());
            var progress = new Progress<DownloadProgress>(p => OnProgress(task, p));
            await downloader.DownloadAsync(task.Url, task.OutputPath, resume: true, progress, task.Cts!.Token);

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
