using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>一次整理移动：源 mod（Mod.Path 为现状路径）与目标目录</summary>
public sealed record ModMove(ModInfo Mod, string TargetDir);

/// <summary>
/// 整理 Mods 计划：同 GUID 跨 mods/unusedmods 两目录先去重（落选 → Duplicates），
/// 再对赢家按站点索引分类：在索引中 → mods/&lt;站点目录&gt;/；不在索引中 → mods/MyMods。
/// </summary>
public sealed class ModOrganizePlan
{
    /// <summary>去重落选文件（→ duplicatemods）</summary>
    public required List<ModMove> Duplicates { get; init; }

    /// <summary>不在 Sideload 索引中的赢家（→ mods/MyMods）</summary>
    public required List<ModMove> Unlisted { get; init; }

    /// <summary>在 Sideload 索引中的赢家（→ mods/&lt;站点目录&gt;/）</summary>
    public required List<ModMove> SitePlaced { get; init; }

    /// <summary>去重后 guid → 最优（含全部赢家，无论是否移动）</summary>
    public required Dictionary<string, ModInfo> Winners { get; init; }

    /// <summary>存在重复的 GUID 组数（确认文案用）</summary>
    public int DupGroups { get; init; }
}

/// <summary>整理 Mods 的纯分类逻辑（无状态，便于单测）</summary>
public static class ModOrganizeHelper
{
    public const string CustomModsDirectoryName = "MyMods";

    /// <summary>
    /// 由全部 zipmod 条目（mods + unusedmods 两处扫描结果，含重复 GUID）与站点索引生成整理计划。
    /// 分组/裁决复刻 ModsWindowViewModel.DedupAsync：同 GUID 按
    /// <see cref="ScannerService.CompareModsForKeep"/>（版本高 → 体积大 → 日期新）取最优。
    /// 不再按卡片引用、shader 或场景用途分类。
    /// </summary>
    public static ModOrganizePlan BuildPlan(
        IReadOnlyList<KeyValuePair<string, ModInfo>> entries,
        IReadOnlyDictionary<string, string> siteIndex,
        string gamePath,
        string modsDir)
    {
        var dupDir = Path.Combine(gamePath, "duplicatemods");
        var customModsDir = Path.Combine(modsDir, CustomModsDirectoryName);
        var index = new Dictionary<string, string>(siteIndex.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (guid, url) in siteIndex)
        {
            if (!index.TryGetValue(guid, out var existing) || string.CompareOrdinal(url, existing) < 0)
                index[guid] = url;
        }

        var winners = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<ModMove>();
        var dupGroups = 0;
        foreach (var group in entries.GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.Select(kv => kv.Value)
                .OrderBy(m => m, Comparer<ModInfo>.Create(ScannerService.CompareModsForKeep))
                .ToList();
            winners[group.Key] = ordered[0];
            if (ordered.Count > 1)
            {
                dupGroups++;
                duplicates.AddRange(ordered.Skip(1).Select(m => new ModMove(m, dupDir)));
            }
        }

        var unlisted = new List<ModMove>();
        var sitePlaced = new List<ModMove>();
        foreach (var (guid, info) in winners)
        {
            var siteDir = GetSiteTargetDir(index, guid, modsDir);
            var targetDir = siteDir ?? customModsDir;
            if (IsInDir(info.Path, targetDir))
                continue;

            if (siteDir is null)
                unlisted.Add(new ModMove(info, targetDir));
            else
                sitePlaced.Add(new ModMove(info, targetDir));
        }

        return new ModOrganizePlan
        {
            Duplicates = duplicates,
            Unlisted = unlisted,
            SitePlaced = sitePlaced,
            Winners = winners,
            DupGroups = dupGroups,
        };
    }

    /// <summary>
    /// 站点相对路径（如 "Exclusive HS2/xxx.zipmod"）→ mods 下目标目录（无目录部分 → mods 根目录）。
    /// 含 .. 或 rooted 的路径视为不在索引（防路径穿越），ErrorLog 留痕。
    /// </summary>
    private static string? GetSiteTargetDir(
        IReadOnlyDictionary<string, string> index, string guid, string modsDir)
    {
        if (!index.TryGetValue(guid, out var rel))
            return null;
        if (Path.IsPathRooted(rel) || rel.Split('/', '\\').Any(seg => seg == ".."))
        {
            ErrorLog.Log($"sideload 站点路径不安全，按不在索引处理: {guid} -> {rel}");
            return null;
        }
        var dirPart = Path.GetDirectoryName(rel);
        return string.IsNullOrEmpty(dirPart) ? modsDir : Path.Combine(modsDir, dirPart);
    }

    /// <summary>路径的直接父目录是否就是指定目录（大小写不敏感）</summary>
    private static bool IsInDir(string path, string dir) =>
        string.Equals(Path.GetDirectoryName(path), dir, StringComparison.OrdinalIgnoreCase);
}
