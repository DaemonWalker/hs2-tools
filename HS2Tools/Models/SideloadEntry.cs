namespace HS2Tools.Models;

/// <summary>
/// Sideload 库条目（库格式 v2）：guid → 远程相对路径 + 增量爬取指纹 + last-seen。
/// SizeText/MtimeText 存 autoindex 列表页的原始文本。MtimeText 按 yyyy-MM-dd HH:mm 比较：
/// 本地记录不晚于本站当前时间即零请求沿用旧 GUID；日期无法解析时仅在原始指纹一致时复用，
/// 其余情况保守回退正常抓取，不会因此漏 Mod。
/// </summary>
public sealed class SideloadEntry
{
    /// <summary>远程相对路径（如 "Exclusive HS2/xxx.zipmod"）</summary>
    public required string Url { get; set; }

    /// <summary>autoindex 列表页的大小列原文（未知 = null）</summary>
    public string? SizeText { get; set; }

    /// <summary>autoindex 列表页的修改时间列原文（未知 = null）</summary>
    public string? MtimeText { get; set; }

    /// <summary>最近一次已应用扫描见到的时间（连续两次已应用扫描未见才淘汰）</summary>
    public DateTime LastSeen { get; set; }
}

/// <summary>旧库中按相对路径索引的条目（增量爬取输入）</summary>
public sealed record KnownSideloadMod(string Guid, string? SizeText, string? MtimeText);
