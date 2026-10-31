namespace HS2Tools.Models;

/// <summary>
/// Sideload 库条目（库格式 v2）：guid → 远程相对路径 + 增量爬取指纹 + last-seen。
/// SizeText/MtimeText 存 autoindex 列表页的原始文本（不参与解析，仅做相等比对）：
/// 路径 + 大小 + 时间一致即视为文件未变，增量爬取零请求沿用旧 GUID；
/// 任一字段缺失则保守回退为正常抓取，不会因此漏 Mod。
/// </summary>
public sealed class SideloadEntry
{
    /// <summary>远程相对路径（如 "Exclusive HS2/xxx.zipmod"）</summary>
    public required string Url { get; set; }

    /// <summary>autoindex 列表页的大小列原文（未知 = null）</summary>
    public string? SizeText { get; set; }

    /// <summary>autoindex 列表页的修改时间列原文（未知 = null）</summary>
    public string? MtimeText { get; set; }

    /// <summary>最近一次完整扫描见到的时间（连续两次完整扫描未见才淘汰）</summary>
    public DateTime LastSeen { get; set; }
}

/// <summary>旧库中按相对路径索引的条目（增量爬取输入）</summary>
public sealed record KnownSideloadMod(string Guid, string? SizeText, string? MtimeText);
