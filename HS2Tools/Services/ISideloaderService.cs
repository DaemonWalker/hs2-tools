using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>
/// Sideload 爬虫抽象（ViewModel 依赖它便于测试替身；生产实现为 <see cref="SideloaderService"/>）。
/// </summary>
public interface ISideloaderService
{
    bool IsRunning { get; }

    /// <summary>取消爬取（置标志位 + CancellationToken，在飞请求即时中断）</summary>
    void Cancel();

    /// <summary>运行爬取，返回结果表与失败统计（有失败时调用方应保留旧库不覆盖）。
    /// known 为旧库条目（按相对路径索引）：路径+大小+时间未变的 Mod 零请求复用（增量爬取）。</summary>
    Task<SideloadRunResult> RunAsync(
        Action<string>? onLog = null, IProgress<SideloaderProgress>? onProgress = null,
        IReadOnlyDictionary<string, KnownSideloadMod>? known = null);
}
