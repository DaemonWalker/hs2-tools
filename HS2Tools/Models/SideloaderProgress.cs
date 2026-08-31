namespace HS2Tools.Models;

/// <summary>爬虫进度（对应 Go onProgress(current, total)，total 恒为 0）</summary>
public readonly record struct SideloaderProgress(int Current, int Total);
