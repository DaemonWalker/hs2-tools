namespace HS2Tools.Models;

/// <summary>下载消息类型（对应 Go ProgressMessage.Type: progress/complete/error/info）</summary>
public enum DownloadMessageType
{
    Progress,
    Complete,
    Error,
    Info,
}

/// <summary>下载进度消息（对应 Go downloader.ProgressMessage）</summary>
public class DownloadProgress
{
    public DownloadMessageType Type { get; set; }

    /// <summary>已下载字节数（含续传起点）</summary>
    public long Downloaded { get; set; }

    /// <summary>总字节数（-1 表示未知）</summary>
    public long Total { get; set; } = -1;

    /// <summary>下载速度 bytes/s（只计本次会话下载量）</summary>
    public double Speed { get; set; }

    public double Percent { get; set; }

    /// <summary>完成时的文件路径</summary>
    public string Path { get; set; } = "";

    /// <summary>错误或信息消息</summary>
    public string Message { get; set; } = "";
}
