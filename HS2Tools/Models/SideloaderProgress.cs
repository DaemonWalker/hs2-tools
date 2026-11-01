namespace HS2Tools.Models;

/// <summary>爬虫进度：Current = 列表中发现的 Mod 文件数（不含目录，Total 恒为 0 保留）；
/// Processed = 其中已处理的 Mod 文件数（含复用/失败/跳过）；Failed = 目录页 + Mod 抓取失败数</summary>
public readonly record struct SideloaderProgress(int Current, int Total, int Processed = 0, int Failed = 0);
