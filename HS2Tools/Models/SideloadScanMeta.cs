namespace HS2Tools.Models;

/// <summary>Sideloader 爬虫扫描结果状态</summary>
public enum SideloadScanStatus
{
    /// <summary>成功结束</summary>
    Success,

    /// <summary>用户停止（部分结果）</summary>
    Stopped,

    /// <summary>异常终止</summary>
    Error,
}

/// <summary>
/// Sideload 数据源最近一次爬虫扫描的元数据，
/// 独立文件 sideload-{sourceId}.meta.json 持久化（与库文件同目录同命名风格）。
/// </summary>
public class SideloadScanMeta
{
    /// <summary>扫描结束时间（本地时间）</summary>
    public DateTime LastScanTime { get; set; }

    public SideloadScanStatus Status { get; set; }

    /// <summary>发现的 Mod 数（停止时为已发现的部分结果数；异常时为 0）</summary>
    public int FoundCount { get; set; }

    /// <summary>异常原因（仅 Status = Error 时有值）</summary>
    public string? Error { get; set; }
}
