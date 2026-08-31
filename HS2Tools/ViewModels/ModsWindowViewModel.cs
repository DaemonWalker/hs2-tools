using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.ViewModels;

/// <summary>本地模组列表行项（GUID / 名称 / 版本 / 使用次数 / 路径）</summary>
public partial class ModItemViewModel : ObservableObject
{
    public required string Guid { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Path { get; init; }

    /// <summary>使用次数（与 ModUsage 大小写不敏感匹配）</summary>
    public required int UsedCount { get; init; }
}

/// <summary>
/// 本地模组窗口 ViewModel（对应原版 Mods/LocalMods.tsx）：
/// 统计三卡（本地 Mods 总数 / 被引用 Mods 数 / 总引用次数，与首页数据概览同口径）、
/// 所有/未使用筛选、刷新重扫 mods 目录 zipmod。
/// 去重/整理不重扫磁盘，以首页「开始分析」持久化的缓存（ModEntries/CharaUsage/SceneUsage/UsedShaderNames）为数据源。
/// </summary>
public partial class ModsWindowViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly ScannerService _scanner;
    private readonly SideloadDatabaseService _sideloadDb;
    private List<ModItemViewModel> _all = new();

    public ModsWindowViewModel(ConfigService config, ScannerService scanner, SideloadDatabaseService sideloadDb)
    {
        _config = config;
        _scanner = scanner;
        _sideloadDb = sideloadDb;

        Reload();
        Mods.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        // 其他窗口完成分析/修改配置后列表自行刷新（单例服务与窗口同寿，无需退订）
        _config.Changed += (_, _) => UiDispatch.Run(Reload);
    }

    /// <summary>筛选后的展示行（DataGrid 负责点击列排序/虚拟化）</summary>
    public ObservableCollection<ModItemViewModel> Mods { get; } = new();

    [ObservableProperty] private int _modCount;
    [ObservableProperty] private int _usageCount;
    [ObservableProperty] private int _totalRefs;

    /// <summary>只显示未使用（使用次数为 0）的 Mods（对应原版 Radio 筛选 value="2"）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _showUnusedOnly;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(DedupCommand))]
    [NotifyCanExecuteChangedFor(nameof(OrganizeCommand))]
    [NotifyPropertyChangedFor(nameof(RefreshButtonText))]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _isRefreshing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(DedupCommand))]
    [NotifyCanExecuteChangedFor(nameof(OrganizeCommand))]
    [NotifyPropertyChangedFor(nameof(DedupButtonText))]
    private bool _isDeduping;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(DedupCommand))]
    [NotifyCanExecuteChangedFor(nameof(OrganizeCommand))]
    [NotifyPropertyChangedFor(nameof(OrganizeButtonText))]
    private bool _isOrganizing;

    public string RefreshButtonText => IsRefreshing ? "扫描中..." : "刷新模组列表";

    public string DedupButtonText => IsDeduping ? "去重中..." : "去重 Mods";

    public string OrganizeButtonText => IsOrganizing ? "整理中..." : "整理 Mods";

    /// <summary>请求用户确认去重（View 订阅弹确认框，确认后调 <see cref="ConfirmDedupAsync"/>）</summary>
    public event EventHandler<string>? DedupConfirmationRequested;

    /// <summary>去重普通提示（无重复 / 完成汇总等，View 订阅弹 MessageBox）</summary>
    public event EventHandler<string>? DedupMessageRequested;

    /// <summary>请求用户确认整理（View 订阅弹确认框，确认后调 <see cref="ConfirmOrganizeAsync"/>）</summary>
    public event EventHandler<string>? OrganizeConfirmationRequested;

    /// <summary>整理普通提示（无需整理 / 完成汇总等，View 订阅弹 MessageBox）</summary>
    public event EventHandler<string>? OrganizeMessageRequested;

    // 去重待执行计划（确认后由 ConfirmDedupAsync 消费）
    private Dictionary<string, ModInfo>? _pendingWinners;
    private List<ModInfo>? _pendingLosers;
    private string? _pendingDupDir;

    // 整理待执行计划（确认后由 ConfirmOrganizeAsync 消费；目标目录已在计划内）
    private ModOrganizePlan? _pendingPlan;

    // 整理分析阶段的完整条目（ConfirmOrganizeAsync 重建缓存时据此找回移动失败落选者的 guid）
    private List<KeyValuePair<string, ModInfo>>? _pendingEntries;

    public bool IsEmpty => Mods.Count == 0;

    public string EmptyText => IsRefreshing
        ? "正在扫描本地 Mods..."
        : ModCount == 0
            ? "暂无本地 Mods，请先设置游戏目录并点击「刷新模组列表」"
            : "没有未使用的 Mods";

    partial void OnShowUnusedOnlyChanged(bool value) => ApplyFilter();

    /// <summary>从 Config.Settings 重建主列表与统计（对应原版 useMemo(mods/useage)）</summary>
    private void Reload()
    {
        var settings = _config.Settings.Current;

        // 原版 createUsageMap：usage key 转小写建 Map，查找时 guid 也转小写 → 整体大小写不敏感；
        // 大小写重复时后者覆盖（与 JS Map.set 一致），故用 OrdinalIgnoreCase 字典手工填充
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (guid, count) in settings.ModUsage)
            usage[guid] = count;

        _all = settings.LocalMods
            .Select(kv => new ModItemViewModel
            {
                Guid = kv.Key,
                Name = kv.Value.Name,
                Version = kv.Value.Version,
                Path = kv.Value.Path,
                UsedCount = usage.TryGetValue(kv.Key, out var c) ? c : 0, // 原版 getUsageCount：未命中为 0
            })
            // 原版排序：guid → version → path（localeCompare）
            .OrderBy(m => m.Guid, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 统计口径与首页数据概览一致（Config.Settings 直取）
        ModCount = settings.LocalMods.Count;
        UsageCount = settings.ModUsage.Count;
        TotalRefs = settings.ModUsage.Values.Sum();

        ApplyFilter();
        OnPropertyChanged(nameof(EmptyText));
    }

    private void ApplyFilter()
    {
        Mods.Clear();
        foreach (var item in _all)
        {
            if (!ShowUnusedOnly || item.UsedCount == 0)
                Mods.Add(item);
        }
    }

    private bool CanRefresh() => !IsRefreshing && !IsDeduping && !IsOrganizing;

    private bool CanDedup() => !IsRefreshing && !IsDeduping && !IsOrganizing;

    private bool CanOrganize() => !IsRefreshing && !IsDeduping && !IsOrganizing;

    /// <summary>
    /// 刷新（对应原版 scanMods）：ScanDirectory(.zipmod) + ReadZipModBatchAsync，
    /// 结果写回 Config.Settings.Current.LocalMods（Changed 事件驱动 Reload）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            var mods = new Dictionary<string, ModInfo>();
            var dir = _config.GetModsDir();
            if (dir is not null)
            {
                var files = _scanner.ScanDirectory(dir, new ScanOptions { TargetExtension = { ".zipmod" } });
                mods = await _scanner.ReadZipModBatchAsync(files, onError: LogScanError);
            }
            _config.Update(s => s.Current.LocalMods = mods);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private static void LogScanError(string message) => App.LogException(new Exception(message));

    /// <summary>
    /// 去重（分析阶段）：以首页「开始分析」持久化的完整条目缓存为数据源（不重扫磁盘），
    /// 同 guid 按规则（版本高 → 体积大 → 日期新）裁决，发现重复则暂存计划并请求确认；
    /// 确认后由 <see cref="ConfirmDedupAsync"/> 执行移动。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDedup))]
    private Task DedupAsync()
    {
        IsDeduping = true;
        try
        {
            var modsDir = _config.GetModsDir();
            var gamePath = _config.Settings.Current.GamePath;
            if (modsDir is null || string.IsNullOrEmpty(gamePath))
            {
                DedupMessageRequested?.Invoke(this, "请先设置游戏目录");
                return Task.CompletedTask;
            }
            var settings = _config.Settings.Current;
            if (settings.LastAnalysisTime is not { } scanTime)
            {
                DedupMessageRequested?.Invoke(this, "请先在首页运行「开始分析」");
                return Task.CompletedTask;
            }

            // 缓存条目覆盖 mods/unusedmods 两目录；去重只针对 mods 目录（与原重扫口径一致）
            var entries = settings.ModEntries
                .Where(e => IsUnderDir(e.Info.Path, modsDir))
                .Select(e => KeyValuePair.Create(e.Guid, e.Info))
                .ToList();

            var winners = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
            var losers = new List<ModInfo>();
            var groups = 0;
            foreach (var group in entries.GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                var ordered = group.Select(kv => kv.Value)
                    .OrderBy(m => m, Comparer<ModInfo>.Create(ScannerService.CompareModsForKeep))
                    .ToList();
                winners[group.Key] = ordered[0];
                if (ordered.Count > 1)
                {
                    groups++;
                    losers.AddRange(ordered.Skip(1));
                }
            }

            if (losers.Count == 0)
            {
                DedupMessageRequested?.Invoke(this, "没有发现重复的 Mods");
                return Task.CompletedTask;
            }

            _pendingWinners = winners;
            _pendingLosers = losers;
            _pendingDupDir = Path.Combine(gamePath, "duplicatemods");
            DedupConfirmationRequested?.Invoke(this,
                $"发现 {groups} 组重复 Mods，将把 {losers.Count} 个落选文件移动到游戏目录下的 duplicatemods 文件夹" +
                $"（保留规则：版本高 → 体积大 → 日期新；基于 {scanTime:yyyy-MM-dd HH:mm} 的分析结果，如手动增删过 Mod 请先重新分析）。是否继续？");
        }
        finally
        {
            IsDeduping = false;
        }
        return Task.CompletedTask;
    }

    /// <summary>去重（执行阶段）：落选文件移入 duplicatemods，LocalMods 更新为各 guid 最优</summary>
    public async Task ConfirmDedupAsync()
    {
        if (_pendingWinners is not { } winners || _pendingLosers is not { } losers || _pendingDupDir is not { } dupDir)
            return;
        _pendingWinners = null;
        _pendingLosers = null;
        _pendingDupDir = null;

        IsDeduping = true;
        var downloadDir = _config.GetModDownloadDir();
        var (moved, failed, skipped) = (0, 0, 0);
        var movedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await Task.Run(() =>
            {
                foreach (var loser in losers)
                {
                    // 下载目录内可能有进行中的下载，跳过不动
                    if (downloadDir is not null &&
                        loser.Path.StartsWith(downloadDir, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        App.LogException(new Exception($"Dedup skip (in download dir): {loser.Path}"));
                        continue;
                    }
                    // 缓存时点之后文件可能已被手动移走/删除：存在性校验，消失则跳过
                    if (!File.Exists(loser.Path))
                    {
                        skipped++;
                        App.LogException(new Exception($"Dedup skip (file missing): {loser.Path}"));
                        continue;
                    }
                    try
                    {
                        _scanner.MoveFile(loser.Path, ScannerService.UniqueTargetPath(dupDir, loser.Path));
                        movedPaths.Add(loser.Path);
                        moved++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        App.LogException(new Exception($"Dedup move failed: {loser.Path}: {ex.Message}"));
                    }
                }
            });

            _config.Update(s =>
            {
                s.Current.LocalMods = winners;
                // 缓存同步：移走的落选者从完整条目中移除（留下的条目 Path 未变）
                if (movedPaths.Count > 0)
                    s.Current.ModEntries.RemoveAll(e => movedPaths.Contains(e.Info.Path));
            });

            var summary = $"去重完成：已移动 {moved} 个文件到 duplicatemods";
            if (skipped > 0)
                summary += $"，跳过 {skipped} 个（下载目录内或文件已不存在）";
            if (failed > 0)
                summary += $"（{failed} 个失败）";
            DedupMessageRequested?.Invoke(this, summary);
        }
        finally
        {
            IsDeduping = false;
        }
    }

    /// <summary>
    /// 整理（分析阶段）：以首页「开始分析」持久化的缓存为数据源（不重扫磁盘）——
    /// 完整 zipmod 条目（覆盖 mods/unusedmods，排除下载目录与 mods/scenemods）、
    /// 分卡（人物/场景）引用集、卡片命中的 shader 名（映射回提供它的 mod GUID 做豁免）；
    /// 先按去重规则裁决同 GUID 重复，再按引用与站点索引分类（有扫描记录才按站点目录归位）；
    /// 有活可干则暂存计划并请求确认。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOrganize))]
    private Task OrganizeAsync()
    {
        IsOrganizing = true;
        try
        {
            var modsDir = _config.GetModsDir();
            var gamePath = _config.Settings.Current.GamePath;
            if (modsDir is null || string.IsNullOrEmpty(gamePath))
            {
                OrganizeMessageRequested?.Invoke(this, "请先设置游戏目录");
                return Task.CompletedTask;
            }
            var settings = _config.Settings.Current;
            if (settings.LastAnalysisTime is not { } scanTime)
            {
                OrganizeMessageRequested?.Invoke(this, "请先在首页运行「开始分析」");
                return Task.CompletedTask;
            }

            // 下载目录与 mods/scenemods（整理目标目录）内的文件不参与整理（缓存条目按路径过滤）
            var downloadDir = _config.GetModDownloadDir();
            var sceneModsDir = Path.Combine(modsDir, "scenemods");
            var entries = settings.ModEntries
                .Where(e => !IsUnderDir(e.Info.Path, downloadDir) && !IsUnderDir(e.Info.Path, sceneModsDir))
                .Select(e => KeyValuePair.Create(e.Guid, e.Info))
                .ToList();

            // 分卡引用集（缓存为 guid->count 字典，整理只需归属集合）
            var charaUsage = new HashSet<string>(settings.CharaUsage.Keys, StringComparer.OrdinalIgnoreCase);
            var sceneUsage = new HashSet<string>(settings.SceneUsage.Keys, StringComparer.OrdinalIgnoreCase);

            // shader 豁免：分析时卡片 KKEx 命中的 shader 名 → 提供它的 mod GUID（按人物卡引用同口径豁免）
            var shaderNameToGuids = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var (guid, info) in entries)
            {
                foreach (var name in info.ShaderNames)
                {
                    if (!shaderNameToGuids.TryGetValue(name, out var guids))
                        shaderNameToGuids[name] = guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    guids.Add(guid);
                }
            }
            var shaderUsage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in settings.UsedShaderNames)
                if (shaderNameToGuids.TryGetValue(name, out var guids))
                    foreach (var guid in guids)
                        shaderUsage.Add(guid);

            // 有扫描记录（meta 存在）才按站点目录归位，否则站点索引不参与分类
            var siteIndex = _sideloadDb.GetMeta() is not null ? _sideloadDb.Database : null;
            var plan = ModOrganizeHelper.BuildPlan(entries, charaUsage, sceneUsage, shaderUsage, siteIndex, gamePath, modsDir);
            if (plan.Duplicates.Count == 0 && plan.Unused.Count == 0 &&
                plan.SceneOnly.Count == 0 && plan.SitePlaced.Count == 0)
            {
                OrganizeMessageRequested?.Invoke(this, "Mods 无需整理");
                return Task.CompletedTask;
            }

            _pendingPlan = plan;
            _pendingEntries = entries;
            var segments = new List<string>();
            if (plan.Duplicates.Count > 0)
                segments.Add($"{plan.Duplicates.Count} 个重复落选文件（{plan.DupGroups} 组，保留规则：版本高 → 体积大 → 日期新）移动到 duplicatemods");
            if (plan.Unused.Count > 0)
                segments.Add($"{plan.Unused.Count} 个未使用 Mods 移动到 unusedmods");
            if (plan.SceneOnly.Count > 0)
                segments.Add($"{plan.SceneOnly.Count} 个仅场景引用的 Mods 移动到 mods\\scenemods");
            if (plan.SitePlaced.Count > 0)
                segments.Add($"{plan.SitePlaced.Count} 个 Mods 按站点目录归位/移回 mods");
            OrganizeConfirmationRequested?.Invoke(this,
                $"整理 Mods 将把：{string.Join("，", segments)}" +
                $"（基于 {scanTime:yyyy-MM-dd HH:mm} 的分析结果，如手动增删过 Mod 请先重新分析）。是否继续？");
        }
        finally
        {
            IsOrganizing = false;
        }
        return Task.CompletedTask;
    }

    /// <summary>整理（执行阶段）：各类文件分别移入计划目标目录，LocalMods 与完整条目缓存同步写回</summary>
    public async Task ConfirmOrganizeAsync()
    {
        if (_pendingPlan is not { } plan)
            return;
        _pendingPlan = null;
        var oldEntries = _pendingEntries;
        _pendingEntries = null;

        IsOrganizing = true;
        var (dupMoved, unusedMoved, sceneMoved, siteMoved, failed) = (0, 0, 0, 0, 0);
        var failedMods = new List<ModInfo>();
        try
        {
            await Task.Run(() =>
            {
                // 移动成功统一改写 Path（与 plan.Winners 同一引用），供 LocalMods 按最终落点写回
                foreach (var move in plan.Duplicates)
                {
                    if (TryOrganizeMove(move.Mod, move.TargetDir) is { } newPath)
                    {
                        move.Mod.Path = newPath;
                        dupMoved++;
                    }
                    else
                    {
                        failed++;
                        failedMods.Add(move.Mod);
                    }
                }
                foreach (var move in plan.Unused)
                {
                    if (TryOrganizeMove(move.Mod, move.TargetDir) is { } newPath)
                    {
                        move.Mod.Path = newPath;
                        unusedMoved++;
                    }
                    else
                    {
                        failed++;
                        failedMods.Add(move.Mod);
                    }
                }
                foreach (var move in plan.SceneOnly)
                {
                    if (TryOrganizeMove(move.Mod, move.TargetDir) is { } newPath)
                    {
                        move.Mod.Path = newPath;
                        sceneMoved++;
                    }
                    else
                    {
                        failed++;
                        failedMods.Add(move.Mod);
                    }
                }
                foreach (var move in plan.SitePlaced)
                {
                    if (TryOrganizeMove(move.Mod, move.TargetDir) is { } newPath)
                    {
                        move.Mod.Path = newPath;
                        siteMoved++;
                    }
                    else
                    {
                        failed++;
                        failedMods.Add(move.Mod);
                    }
                }
            });

            // LocalMods 写回以最终落点为准：仍在 mods 目录下的赢家才是本地 mod
            // （SceneOnly/SitePlaced 的 Path 已更新；移进或留在 unusedmods 的不收录；移动失败的保留原样）
            var modsDirNow = _config.GetModsDir();
            _config.Update(s =>
            {
                var mods = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var (guid, info) in plan.Winners)
                {
                    if (modsDirNow is null || IsUnderDir(info.Path, modsDirNow))
                        mods[guid] = info;
                }
                s.Current.LocalMods = mods;

                // 缓存同步：以赢家最终落点重建完整条目；移动失败的落选者仍在磁盘上，保留条目供下次去重/整理
                var rebuilt = new List<ModScanEntry>();
                foreach (var (guid, info) in plan.Winners)
                    rebuilt.Add(new ModScanEntry { Guid = guid, Info = info });
                if (failedMods.Count > 0 && oldEntries is not null)
                {
                    var failedSet = new HashSet<ModInfo>(failedMods);
                    foreach (var (guid, info) in oldEntries)
                        if (failedSet.Contains(info))
                            rebuilt.Add(new ModScanEntry { Guid = guid, Info = info });
                }
                s.Current.ModEntries = rebuilt;
            });

            var parts = new List<string>();
            if (dupMoved > 0)
                parts.Add($"去重 {dupMoved}");
            if (unusedMoved > 0)
                parts.Add($"未使用 {unusedMoved}");
            if (sceneMoved > 0)
                parts.Add($"场景专用 {sceneMoved}");
            if (siteMoved > 0)
                parts.Add($"站点归位 {siteMoved}");
            var summary = $"整理完成：{string.Join("、", parts)}";
            if (failed > 0)
                summary += $"（{failed} 个失败）";
            OrganizeMessageRequested?.Invoke(this, summary);
        }
        finally
        {
            IsOrganizing = false;
        }
    }

    /// <summary>逐文件移动（防重名）；缓存时点后文件消失或移动失败记日志返回 null 不中断</summary>
    private string? TryOrganizeMove(ModInfo mod, string targetDir)
    {
        // 存在性校验：分析缓存时点之后文件可能已被手动移走/删除
        if (!File.Exists(mod.Path))
        {
            App.LogException(new Exception($"Organize move skipped (file missing): {mod.Path}"));
            return null;
        }
        try
        {
            var target = ScannerService.UniqueTargetPath(targetDir, mod.Path);
            _scanner.MoveFile(mod.Path, target);
            return target;
        }
        catch (Exception ex)
        {
            App.LogException(new Exception($"Organize move failed: {mod.Path}: {ex.Message}"));
            return null;
        }
    }

    /// <summary>路径是否位于指定目录内（前缀匹配，大小写不敏感）</summary>
    private static bool IsUnderDir(string path, string? dir) =>
        dir is not null && path.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
}
