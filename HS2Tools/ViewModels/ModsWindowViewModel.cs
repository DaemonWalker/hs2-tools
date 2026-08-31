using System.Collections.ObjectModel;
using System.Text;
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
    /// 去重（分析阶段）：重扫 mods 目录，同 guid 按规则（版本高 → 体积大 → 日期新）裁决，
    /// 发现重复则暂存计划并请求确认；确认后由 <see cref="ConfirmDedupAsync"/> 执行移动。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDedup))]
    private async Task DedupAsync()
    {
        IsDeduping = true;
        try
        {
            var modsDir = _config.GetModsDir();
            var gamePath = _config.Settings.Current.GamePath;
            if (modsDir is null || string.IsNullOrEmpty(gamePath))
            {
                DedupMessageRequested?.Invoke(this, "请先设置游戏目录");
                return;
            }

            var files = _scanner.ScanDirectory(modsDir, new ScanOptions { TargetExtension = { ".zipmod" } });
            var entries = await _scanner.ReadZipModBatchListAsync(files, onError: LogScanError);

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
                return;
            }

            _pendingWinners = winners;
            _pendingLosers = losers;
            _pendingDupDir = Path.Combine(gamePath, "duplicatemods");
            DedupConfirmationRequested?.Invoke(this,
                $"发现 {groups} 组重复 Mods，将把 {losers.Count} 个落选文件移动到游戏目录下的 duplicatemods 文件夹" +
                "（保留规则：版本高 → 体积大 → 日期新）。是否继续？");
        }
        finally
        {
            IsDeduping = false;
        }
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
                    try
                    {
                        _scanner.MoveFile(loser.Path, ScannerService.UniqueTargetPath(dupDir, loser.Path));
                        moved++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        App.LogException(new Exception($"Dedup move failed: {loser.Path}: {ex.Message}"));
                    }
                }
            });

            _config.Update(s => s.Current.LocalMods = winners);

            var summary = $"去重完成：已移动 {moved} 个文件到 duplicatemods";
            if (skipped > 0)
                summary += $"，跳过下载目录 {skipped} 个";
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
    /// 整理（分析阶段）：重扫 mods 目录（排除下载目录与 mods/scenemods）外加 unusedmods，
    /// 实时分别扫描人物卡/场景卡 PNG 引用（现有 ModUsage 缓存是两卡合并口径，区分不了"仅场景"），
    /// 先按去重规则裁决同 GUID 重复，再按引用与站点索引分类（有扫描记录才按站点目录归位）；
    /// 有活可干则暂存计划并请求确认。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOrganize))]
    private async Task OrganizeAsync()
    {
        IsOrganizing = true;
        try
        {
            var modsDir = _config.GetModsDir();
            var gamePath = _config.Settings.Current.GamePath;
            if (modsDir is null || string.IsNullOrEmpty(gamePath))
            {
                OrganizeMessageRequested?.Invoke(this, "请先设置游戏目录");
                return;
            }

            // 下载目录与 mods/scenemods（整理目标目录）内的文件不参与整理
            var downloadDir = _config.GetModDownloadDir();
            var sceneModsDir = Path.Combine(modsDir, "scenemods");
            var files = _scanner.ScanDirectory(modsDir, new ScanOptions { TargetExtension = { ".zipmod" } })
                .Where(f => !IsUnderDir(f, downloadDir) && !IsUnderDir(f, sceneModsDir))
                .ToList();
            // unusedmods 一并扫描：被引用的移回 mods，未引用的原地不动
            var unusedDir = Path.Combine(gamePath, "unusedmods");
            if (Directory.Exists(unusedDir))
                files.AddRange(_scanner.ScanDirectory(unusedDir, new ScanOptions { TargetExtension = { ".zipmod" } }));
            var entries = await _scanner.ReadZipModBatchListAsync(files, onError: LogScanError);

            // shader 使用检测：卡片不按 GUID 引用 shader 包，但会把 shader 名明文留在 KKEx
            // （Material Editor 数据）里；以 manifest 声明的 shader 名为候选做内容级匹配
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
            var shaderNames = shaderNameToGuids.Keys
                .Select(n => new KeyValuePair<string, byte[]>(n, Encoding.UTF8.GetBytes(n)))
                .ToList();
            var usedShaderNames = new HashSet<string>(StringComparer.Ordinal);

            var charaUsage = await ScanPngUsageSetAsync(_config.GetCharaDir(), shaderNames, usedShaderNames);
            var sceneUsage = await ScanPngUsageSetAsync(_config.GetSceneDir(), shaderNames, usedShaderNames);
            // 命中 shader 名 → 提供它的 mod GUID（整理时按人物卡引用同口径豁免）
            var shaderUsage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in usedShaderNames)
                foreach (var guid in shaderNameToGuids[name])
                    shaderUsage.Add(guid);

            // 有扫描记录（meta 存在）才按站点目录归位，否则站点索引不参与分类
            var siteIndex = _sideloadDb.GetMeta() is not null ? _sideloadDb.Database : null;
            var plan = ModOrganizeHelper.BuildPlan(entries, charaUsage, sceneUsage, shaderUsage, siteIndex, gamePath, modsDir);
            if (plan.Duplicates.Count == 0 && plan.Unused.Count == 0 &&
                plan.SceneOnly.Count == 0 && plan.SitePlaced.Count == 0)
            {
                OrganizeMessageRequested?.Invoke(this, "Mods 无需整理");
                return;
            }

            _pendingPlan = plan;
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
                $"整理 Mods 将把：{string.Join("，", segments)}。是否继续？");
        }
        finally
        {
            IsOrganizing = false;
        }
    }

    /// <summary>整理（执行阶段）：各类文件分别移入计划目标目录，LocalMods 同步写回</summary>
    public async Task ConfirmOrganizeAsync()
    {
        if (_pendingPlan is not { } plan)
            return;
        _pendingPlan = null;

        IsOrganizing = true;
        var (dupMoved, unusedMoved, sceneMoved, siteMoved, failed) = (0, 0, 0, 0, 0);
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

    /// <summary>逐文件移动（防重名），失败记日志返回 null 不中断</summary>
    private string? TryOrganizeMove(ModInfo mod, string targetDir)
    {
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

    /// <summary>
    /// 扫描 PNG 目录汇成引用 GUID 集合（分批同 MainWindowViewModel，目录不存在视为空）。
    /// shaderNames 非空时同步做 shader 使用检测，命中的 shader 名累积进 usedShaderNames。
    /// </summary>
    private async Task<HashSet<string>> ScanPngUsageSetAsync(
        string? dir, IReadOnlyList<KeyValuePair<string, byte[]>>? shaderNames = null,
        HashSet<string>? usedShaderNames = null)
    {
        var usage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (dir is null || !Directory.Exists(dir))
            return usage;

        const int batchSize = 500;
        var files = _scanner.ScanDirectory(dir, new ScanOptions { TargetExtension = { ".png" } });
        for (var i = 0; i < files.Count; i += batchSize)
        {
            var batch = files.GetRange(i, Math.Min(batchSize, files.Count - i));
            if (shaderNames is { Count: > 0 })
            {
                var results = await _scanner.ReadPngModsAndShadersBatchAsync(batch, shaderNames, onError: LogScanError);
                foreach (var item in results)
                {
                    foreach (var modId in item.ModIDs)
                        usage.Add(modId);
                    if (usedShaderNames is not null)
                        foreach (var name in item.ShaderNames)
                            usedShaderNames.Add(name);
                }
                continue;
            }
            var modsOnly = await _scanner.ReadPngModsBatchAsync(batch, onError: LogScanError);
            foreach (var item in modsOnly)
                foreach (var modId in item.ModIDs)
                    usage.Add(modId);
        }
        return usage;
    }

    /// <summary>路径是否位于指定目录内（前缀匹配，大小写不敏感）</summary>
    private static bool IsUnderDir(string path, string? dir) =>
        dir is not null && path.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
}
