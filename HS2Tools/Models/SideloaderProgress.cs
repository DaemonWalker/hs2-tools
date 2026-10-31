namespace HS2Tools.Models;

/// <summary>爬虫进度：Current = 已发现数（对应 Go onProgress(current, total)，Total 恒为 0 保留）；
/// Processed = 已处理 Mod 数（含复用/失败/跳过）；Failed = 目录页 + Mod 抓取失败数</summary>
public readonly record struct SideloaderProgress(int Current, int Total, int Processed = 0, int Failed = 0);
