namespace HS2Tools.Models;

/// <summary>
/// 爬虫一次运行的结果与失败统计。
/// 空结果保留旧库；存在抓取失败时由调用方询问用户是否使用本轮结果。
/// </summary>
public sealed class SideloadRunResult
{
    /// <summary>guid → 库条目（相对路径 + 增量指纹）</summary>
    public required Dictionary<string, SideloadEntry> Mods { get; init; }

    /// <summary>列表中发现的 Mod 文件数（不含目录；可能大于去重后的 Mods.Count）</summary>
    public int DiscoveredMods { get; init; }

    /// <summary>增量爬取复用数（本站修改时间未变新，零请求沿用旧 GUID）</summary>
    public int ReusedMods { get; init; }

    /// <summary>目录页抓取失败数（该目录整棵子树缺失）</summary>
    public int FailedPages { get; init; }

    /// <summary>Mod 抓取失败数（网络/服务端错误；404 不算失败，见 <see cref="NotFoundMods"/>）</summary>
    public int FailedMods { get; init; }

    /// <summary>文件本身无法解析出 GUID（非 zip / 无 manifest 等，长期存在，不阻止更新）</summary>
    public int SkippedNoGuid { get; init; }

    /// <summary>manifest 压缩大小超防御上限（不阻止更新）</summary>
    public int SkippedTooLarge { get; init; }

    /// <summary>远端已不存在（404）：条目自然淘汰，不算失败</summary>
    public int NotFoundMods { get; init; }

    public bool HasFailures => FailedPages > 0 || FailedMods > 0;
}
