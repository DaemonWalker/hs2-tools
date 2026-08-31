using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>
/// Sideload 爬虫抽象（ViewModel 依赖它便于测试替身；生产实现为 <see cref="SideloaderService"/>）。
/// </summary>
public interface ISideloaderService
{
    bool IsRunning { get; }

    /// <summary>取消爬取（只置标志位，已发出的请求不中断——与原版一致）</summary>
    void Cancel();

    /// <summary>运行爬取，返回 guid → 相对路径 的结果表</summary>
    Task<Dictionary<string, string>> RunAsync(
        Action<string>? onLog = null, IProgress<SideloaderProgress>? onProgress = null);
}
