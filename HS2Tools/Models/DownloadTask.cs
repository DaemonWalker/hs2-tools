namespace HS2Tools.Models;

public enum DownloadTaskStatus
{
    Downloading,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>下载任务（不持久化，关闭窗口不中断）</summary>
public class DownloadTask
{
    /// <summary>任务标识（Mod 名，与原版 activeDownloads 的 key 一致）</summary>
    public required string Id { get; init; }

    /// <summary>完整下载 URL</summary>
    public required string Url { get; init; }

    /// <summary>输出文件路径（{dir}/{name}.zipmod）</summary>
    public required string OutputPath { get; init; }

    public DownloadTaskStatus Status { get; set; } = DownloadTaskStatus.Downloading;

    public long Downloaded { get; set; }
    public long Total { get; set; } = -1;
    public double Speed { get; set; }
    public double Percent { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    internal CancellationTokenSource? Cts { get; set; }
}
