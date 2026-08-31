using HS2Tools.Models;

namespace HS2Tools.Services;

/// <summary>
/// 整理 Mods 计划：同 GUID 先去重（落选 → Duplicates），再对赢家按引用分类
/// （两卡都没引用 → Unused；仅场景卡引用 → SceneOnly；其余留原地）。
/// </summary>
public sealed class ModOrganizePlan
{
    /// <summary>去重落选文件（→ duplicatemods）</summary>
    public required List<ModInfo> Duplicates { get; init; }

    /// <summary>人物卡/场景卡都未引用的赢家（→ unusedmods）</summary>
    public required List<ModInfo> Unused { get; init; }

    /// <summary>仅场景卡引用的赢家（→ mods/scenemods）</summary>
    public required List<ModInfo> SceneOnly { get; init; }

    /// <summary>去重后 guid → 最优（含全部赢家，无论是否移动）</summary>
    public required Dictionary<string, ModInfo> Winners { get; init; }

    /// <summary>存在重复的 GUID 组数（确认文案用）</summary>
    public int DupGroups { get; init; }
}

/// <summary>整理 Mods 的纯分类逻辑（无状态，便于单测）</summary>
public static class ModOrganizeHelper
{
    /// <summary>
    /// 由全部 zipmod 条目（含重复 GUID）与两卡引用集合生成整理计划。
    /// 分组/裁决复刻 ModsWindowViewModel.DedupAsync：同 GUID 按
    /// <see cref="ScannerService.CompareModsForKeep"/>（版本高 → 体积大 → 日期新）取最优。
    /// charaUsage/sceneUsage 均为 OrdinalIgnoreCase 集合。
    /// </summary>
    public static ModOrganizePlan BuildPlan(
        IReadOnlyList<KeyValuePair<string, ModInfo>> entries,
        ISet<string> charaUsage,
        ISet<string> sceneUsage)
    {
        var winners = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<ModInfo>();
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
                duplicates.AddRange(ordered.Skip(1));
            }
        }

        var unused = new List<ModInfo>();
        var sceneOnly = new List<ModInfo>();
        foreach (var (guid, info) in winners)
        {
            if (!charaUsage.Contains(guid) && !sceneUsage.Contains(guid))
                unused.Add(info);
            else if (!charaUsage.Contains(guid))
                sceneOnly.Add(info);
        }

        return new ModOrganizePlan
        {
            Duplicates = duplicates,
            Unused = unused,
            SceneOnly = sceneOnly,
            Winners = winners,
            DupGroups = dupGroups,
        };
    }
}
