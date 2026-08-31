using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>一次整理移动：源 mod（Mod.Path 为现状路径）与目标目录</summary>
public sealed record ModMove(ModInfo Mod, string TargetDir);

/// <summary>
/// 整理 Mods 计划：同 GUID 跨 mods/unusedmods 两目录先去重（落选 → Duplicates），
/// 再对赢家按引用与站点索引分类（各类别在 helper 内直接算好目标目录）：
/// 两卡都没引用 → Unused（平铺进 unusedmods，不按站点目录；已在 unusedmods 的原地不动）；
/// 被引用且在站点索引 → SitePlaced（mods/&lt;站点目录&gt;/，含从 unusedmods 移回，站点目录优先于 scenemods）；
/// 被引用且不在索引、仅场景 → SceneOnly（mods/scenemods）；
/// 被引用且不在索引、人物卡引用 → 在 unusedmods 的移回 mods 根目录（进 SitePlaced），在 mods 的原地不动。
/// </summary>
public sealed class ModOrganizePlan
{
    /// <summary>去重落选文件（→ duplicatemods）</summary>
    public required List<ModMove> Duplicates { get; init; }

    /// <summary>人物卡/场景卡都未引用的赢家（→ unusedmods 平铺）</summary>
    public required List<ModMove> Unused { get; init; }

    /// <summary>仅场景卡引用且不在站点索引的赢家（→ mods/scenemods）</summary>
    public required List<ModMove> SceneOnly { get; init; }

    /// <summary>按站点目录归位 / 从 unusedmods 移回的赢家（→ mods/&lt;站点目录&gt;/ 或 mods 根目录）</summary>
    public required List<ModMove> SitePlaced { get; init; }

    /// <summary>去重后 guid → 最优（含全部赢家，无论是否移动）</summary>
    public required Dictionary<string, ModInfo> Winners { get; init; }

    /// <summary>存在重复的 GUID 组数（确认文案用）</summary>
    public int DupGroups { get; init; }
}

/// <summary>整理 Mods 的纯分类逻辑（无状态，便于单测）</summary>
public static class ModOrganizeHelper
{
    /// <summary>
    /// 由全部 zipmod 条目（mods + unusedmods 两处扫描结果，含重复 GUID）与两卡引用集合生成整理计划。
    /// 分组/裁决复刻 ModsWindowViewModel.DedupAsync：同 GUID 按
    /// <see cref="ScannerService.CompareModsForKeep"/>（版本高 → 体积大 → 日期新）取最优。
    /// charaUsage/sceneUsage 均为 OrdinalIgnoreCase 集合；siteIndex 为 null 表示未扫描过网站（不按站点目录归位）。
    /// </summary>
    public static ModOrganizePlan BuildPlan(
        IReadOnlyList<KeyValuePair<string, ModInfo>> entries,
        ISet<string> charaUsage,
        ISet<string> sceneUsage,
        IReadOnlyDictionary<string, string>? siteIndex,
        string gamePath,
        string modsDir)
    {
        var dupDir = Path.Combine(gamePath, "duplicatemods");
        var unusedDir = Path.Combine(gamePath, "unusedmods");
        var sceneModsDir = Path.Combine(modsDir, "scenemods");
        // 站点索引按 GUID 大小写不敏感查找（库文件反序列化出来的是 Ordinal 字典）
        var index = siteIndex is null ? null : new Dictionary<string, string>(siteIndex, StringComparer.OrdinalIgnoreCase);

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

        var unused = new List<ModMove>();
        var sceneOnly = new List<ModMove>();
        var sitePlaced = new List<ModMove>();
        foreach (var (guid, info) in winners)
        {
            var usedByChara = charaUsage.Contains(guid);
            var usedByScene = sceneUsage.Contains(guid);
            if (!usedByChara && !usedByScene)
            {
                // 未引用：已在 unusedmods 的原地不动；否则平铺进 unusedmods（不按站点目录）
                if (!IsUnderDir(info.Path, unusedDir))
                    unused.Add(new ModMove(info, unusedDir));
                continue;
            }

            var siteDir = index is null ? null : GetSiteTargetDir(index, guid, modsDir);
            if (siteDir is not null)
            {
                // 在站点索引（覆盖归位/移回/仅场景在索引；站点目录优先于 scenemods）：已在目标位置则不动
                if (!IsInDir(info.Path, siteDir))
                    sitePlaced.Add(new ModMove(info, siteDir));
            }
            else if (!usedByChara)
            {
                // 仅场景 + 不在索引 → mods/scenemods（含从 unusedmods 移回的）
                sceneOnly.Add(new ModMove(info, sceneModsDir));
            }
            else if (IsUnderDir(info.Path, unusedDir))
            {
                // 人物卡引用 + 不在索引：在 unusedmods 的移回 mods 根目录；在 mods 的原地不动
                sitePlaced.Add(new ModMove(info, modsDir));
            }
        }

        return new ModOrganizePlan
        {
            Duplicates = duplicates,
            Unused = unused,
            SceneOnly = sceneOnly,
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

    /// <summary>路径是否位于指定目录内（前缀匹配，大小写不敏感）</summary>
    private static bool IsUnderDir(string path, string dir) =>
        path.StartsWith(dir, StringComparison.OrdinalIgnoreCase);

    /// <summary>路径的直接父目录是否就是指定目录（大小写不敏感）</summary>
    private static bool IsInDir(string path, string dir) =>
        string.Equals(Path.GetDirectoryName(path), dir, StringComparison.OrdinalIgnoreCase);
}
