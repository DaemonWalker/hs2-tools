namespace HS2Tools.Models;

/// <summary>
/// 爬虫一次运行的结果与失败统计。
/// 用于更新保护（A 策略）：空结果或存在抓取失败时保留旧库不覆盖，
/// 避免部分失败的爬取把旧库条目抹掉（kkec 无内嵌库兜底，清空即真丢失）。
/// </summary>
public sealed class SideloadRunResult
{
    /// <summary>guid → 库条目（相对路径 + 增量指纹）</summary>
    public required Dictionary<string, SideloadEntry> Mods { get; init; }

    /// <summary>增量爬取复用数（路径+大小+时间未变，零请求沿用旧 GUID）</summary>
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
