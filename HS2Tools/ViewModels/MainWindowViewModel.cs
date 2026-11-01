using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Models;
using HS2Tools.Services;
using Microsoft.Win32;

namespace HS2Tools.ViewModels;

/// <summary>全局游戏下拉框的一项</summary>
public partial class GameSwitchItemViewModel : ObservableObject
{
    public required GameProfile Profile { get; init; }

    public string Id => Profile.Id;
    public string DisplayName => Profile.DisplayName;

    /// <summary>当前生效游戏</summary>
    [ObservableProperty]
    private bool _isCurrent;
}

/// <summary>Sideloader 更新区块的 UI 状态（对应原版 sideloadUpdateStatus）</summary>
public enum SideloaderUiState
{
    Idle,
    Running,
    Success,
    Stopped,
    Error,

    /// <summary>部分失败（空结果或有抓取失败）：已保留旧库不覆盖</summary>
    Partial,
}

/// <summary>
/// 主壳与工作台共用 ViewModel：全局游戏切换、路径设置、数据分析、快速启动、
/// sideloader 更新（运行/停止）、缺失 Mod 批量补全、数据概览。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>分批大小（对应原版 scanLogic BATCH_SIZE）</summary>
    private const int BatchSize = 500;

    private readonly ConfigService _config;
    private readonly ScannerService _scanner;
    private readonly DownloadManager _downloads;
    private readonly GameLauncherService _launcher;
    private readonly SideloadDatabaseService _sideloadDb;
    private readonly Func<ISideloaderService> _sideloaderFactory;
    private readonly AnalysisCacheService? _analysisCache;

    private ISideloaderService? _currentSideloader;
    private SideloadRunResult? _pendingSideloadResult;
    private bool _stopRequested;

    /// <summary>已按之加载的游戏 ID（Changed 时与 Settings.CurrentGame 比较，识别"游戏被切换"）</summary>
    private string _loadedGameId;

    public MainWindowViewModel(
        ConfigService config,
        ScannerService scanner,
        DownloadManager downloads,
        GameLauncherService launcher,
        SideloadDatabaseService sideloadDb,
        Func<ISideloaderService> sideloaderFactory,
        AnalysisCacheService? analysisCache = null)
    {
        _config = config;
        _scanner = scanner;
        _downloads = downloads;
        _launcher = launcher;
        _sideloadDb = sideloadDb;
        _sideloaderFactory = sideloaderFactory;
        _analysisCache = analysisCache;

        _loadedGameId = config.Settings.CurrentGame;
        GameSwitchItems = new(GameProfiles.All.Select(p =>
            new GameSwitchItemViewModel { Profile = p, IsCurrent = p.Id == _loadedGameId }));

        GamePath = config.Settings.Current.GamePath; // setter 链触发校验
        RefreshStats();

        // 其他窗口改配置（代理/收藏/切游戏）或爬虫更新数据库后，统计与缺失数自行刷新
        _config.Changed += (_, _) => UiDispatch.Run(OnConfigChanged);
        _sideloadDb.Changed += (_, _) => UiDispatch.Run(RefreshStats);
    }

    /// <summary>请求用户确认停止爬虫（View 订阅弹确认框，确认后调 <see cref="ConfirmStopSideloader"/>）</summary>
    public event EventHandler? StopConfirmationRequested;

    /// <summary>部分抓取失败时请求用户确认是否使用本轮结果</summary>
    public event EventHandler<string>? PartialResultConfirmationRequested;

    /// <summary>测试用：非 null 时覆盖下载 base URL（默认取当前游戏档案的 SideloadBaseUrl）</summary>
    internal string? DownloadBaseUrlOverride;

    // ==================== 游戏切换（侧栏底部全局下拉框；数据来自 GameProfiles.All） ====================

    /// <summary>全局游戏选项（当前游戏 IsCurrent=true）</summary>
    public ObservableCollection<GameSwitchItemViewModel> GameSwitchItems { get; }

    /// <summary>全局下拉框选中项；运行中拒绝切换时立即回退到当前游戏。</summary>
    public GameSwitchItemViewModel SelectedGameSwitchItem
    {
        get => GameSwitchItems.First(item => item.IsCurrent);
        set
        {
            if (value is null || value.Id == _loadedGameId)
                return;
            if (CanSwitchGame())
                SwitchGame(value.Id);
            else
                OnPropertyChanged(nameof(SelectedGameSwitchItem));
        }
    }

    /// <summary>当前游戏档案（标题/路径卡片等文案绑定其 DisplayName/GameExeName）</summary>
    public GameProfile CurrentProfile => _config.CurrentProfile;

    /// <summary>扫描/爬虫进行中或等待应用部分结果时禁止切游戏，避免结果落错数据源</summary>
    private bool CanSwitchGame() => !IsScanning && SideloaderState != SideloaderUiState.Running
                                    && _pendingSideloadResult is null;

    /// <summary>切换当前游戏（未知 ID 由 GameProfiles.Get 回退 HS2，与配置加载口径一致）</summary>
    [RelayCommand(CanExecute = nameof(CanSwitchGame))]
    private void SwitchGame(string? gameId)
    {
        if (string.IsNullOrEmpty(gameId) || gameId == _loadedGameId)
            return;
        _loadedGameId = gameId; // 先于 Update：Changed 回调据此判定为自己发起，不重复应用（无回环）
        _config.Update(s => s.CurrentGame = gameId);
        ApplyGameSwitch(gameId);
    }

    /// <summary>配置变化：游戏被别处切换时同步切换器与路径；其余变化（收藏/代理）只刷统计</summary>
    private void OnConfigChanged()
    {
        if (_config.Settings.CurrentGame != _loadedGameId)
            ApplyGameSwitch(_config.Settings.CurrentGame);
        RefreshStats();
    }

    /// <summary>应用游戏切换：切换器高亮、重载该游戏 GamePath 并重校验、重置扫描展示、刷新统计</summary>
    private void ApplyGameSwitch(string gameId)
    {
        _loadedGameId = gameId;
        foreach (var item in GameSwitchItems)
            item.IsCurrent = item.Id == gameId;
        OnPropertyChanged(nameof(CurrentProfile));
        OnPropertyChanged(nameof(SelectedGameSwitchItem));

        // 扫描进度/结果属于旧游戏，重置展示（统计数据为各游戏独立存储，由 RefreshStats 重读）
        ScanCompleted = false;
        ScanStep = 0;
        ModScanDone = SceneScanDone = CharaScanDone = false;
        ModScanProgress = SceneScanProgress = CharaScanProgress = "";
        ScanError = "";
        LaunchStatusText = "";
        UnusedModCount = 0;
        _pendingMoveBack = null;
        _pendingGuidRepairs = null;

        var path = _config.Settings.Current.GamePath;
        GamePath = path;           // setter 有 != 守卫：与已存值相同不会回写配置
        ApplyPathValidation(path); // 路径串相同（setter 未触发）也要按新游戏 exe 重校验
        RefreshStats();
    }

    // ==================== 游戏路径 ====================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchStudioCommand))]
    [NotifyCanExecuteChangedFor(nameof(ComplementMissingModsCommand))]
    private bool _isGamePathValid;

    [ObservableProperty]
    private string _gamePath = "";

    [ObservableProperty]
    private string _pathStatusText = "";

    partial void OnGamePathChanged(string value)
    {
        ApplyPathValidation(value);

        // 与已加载值相同则不重复写盘（避免每次启动重写配置文件）
        if (value != _config.Settings.Current.GamePath)
            _config.Update(s => s.Current.GamePath = value);
    }

    /// <summary>按当前游戏 exe 校验路径并更新状态文案（切游戏后路径串不变时也需重跑）</summary>
    private void ApplyPathValidation(string value)
    {
        IsGamePathValid = ValidateGamePath(value);
        PathStatusText = IsGamePathValid
            ? $"已验证：找到 {_config.CurrentProfile.GameExeName}"
            : $"未找到 {_config.CurrentProfile.GameExeName}，请选择游戏目录";
    }

    /// <summary>选择游戏 exe（对应原版 SelectPath：选 exe 取目录）</summary>
    [RelayCommand]
    private void Browse()
    {
        var exeName = _config.CurrentProfile.GameExeName;
        var dialog = new OpenFileDialog
        {
            Title = $"选择 {exeName}",
            Filter = $"{exeName}|{exeName}|可执行文件 (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
            GamePath = Path.GetDirectoryName(dialog.FileName) ?? "";
    }

    /// <summary>游戏路径校验：目录下存在当前游戏的 exe</summary>
    public bool ValidateGamePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, _config.CurrentProfile.GameExeName));

    // ==================== 数据分析（对应原版 Scan：顺序 scanMods → scanScene → scanFemale） ====================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwitchGameCommand))]
    [NotifyPropertyChangedFor(nameof(ScanButtonText))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanButtonText))]
    private bool _scanCompleted;

    /// <summary>当前阶段：0=分析 Mods 1=分析场景 2=分析角色 3=完成</summary>
    [ObservableProperty]
    private int _scanStep;

    [ObservableProperty] private bool _modScanDone;
    [ObservableProperty] private bool _sceneScanDone;
    [ObservableProperty] private bool _charaScanDone;

    [ObservableProperty] private string _modScanProgress = "";
    [ObservableProperty] private string _sceneScanProgress = "";
    [ObservableProperty] private string _charaScanProgress = "";

    [ObservableProperty] private string _scanError = "";

    /// <summary>unusedmods 中的 mod 数（数据概览"本地 Mods"后括号显示；随每次分析刷新，切游戏清零）</summary>
    [ObservableProperty] private int _unusedModCount;

    // unusedmods 待移回清单（分析完成后请求确认，确认后由 ConfirmMoveBackAsync 消费）
    private List<(string Guid, ModInfo Info)>? _pendingMoveBack;
    private List<ZipModGuidRepairPlan>? _pendingGuidRepairs;

    /// <summary>请求用户确认移回 unusedmods 中被引用的 mod（View 订阅弹确认框，确认后调 <see cref="ConfirmMoveBackAsync"/>）</summary>
    public event EventHandler<string>? MoveBackConfirmationRequested;

    /// <summary>仅在原始 GUID 无精确匹配时，请求确认修复 Trim 后相同的 zipmod GUID。</summary>
    public event EventHandler<string>? GuidRepairConfirmationRequested;

    public string ScanButtonText => IsScanning ? "分析中..." : ScanCompleted ? "重新分析" : "开始分析";

    private bool CanScan() => IsGamePathValid && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        ScanCompleted = false;
        ScanError = "";
        ScanStep = 0;
        ModScanDone = SceneScanDone = CharaScanDone = false;
        ModScanProgress = SceneScanProgress = CharaScanProgress = "";
        UnusedModCount = 0;
        _pendingMoveBack = null;
        _pendingGuidRepairs = null;

        try
        {
            // unusedmods 一并扫描（方案：分析顺带统计未使用数，被引用的分析完成后确认移回）
            var unusedDir = _config.Settings.Current.GamePath is { } gp
                ? Path.Combine(gp, "unusedmods")
                : null;
            var (mods, unusedMods, entries) = await ScanModsAsync(_config.GetModsDir()!, unusedDir);
            UnusedModCount = unusedMods.Count;
            ScanStep = 1;
            ModScanDone = true;

            var sceneUsage = await ScanPngUsageAsync(_config.GetSceneDir()!, "scene", p => SceneScanProgress = p);
            ScanStep = 2;
            SceneScanDone = true;

            var charaUsage = await ScanPngUsageAsync(_config.GetCharaDir()!, "chara", p => CharaScanProgress = p);
            ScanStep = 3;
            CharaScanDone = true;

            // 原版合并语义：{ ...scene, ...female } —— 同 guid 时角色统计覆盖场景统计
            var mergedUsage = new Dictionary<string, int>(sceneUsage);
            foreach (var (guid, count) in charaUsage)
                mergedUsage[guid] = count;

            // 移回候选：unusedmods 里被卡片引用、或 manifest 声明了 shader 的 mod（shader 包不按 GUID
            // 被卡片引用，只要含 shader 信息就视为在用），且 mods 里没有同 GUID（同 GUID 已在 mods 的属重复，留给整理去重）
            _pendingMoveBack = unusedMods
                .Where(kv => (mergedUsage.ContainsKey(kv.Key) || kv.Value.ShaderNames.Count > 0) && !mods.ContainsKey(kv.Key))
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

            _pendingGuidRepairs = ZipModGuidRepairService.BuildPlans(mods, mergedUsage.Keys);

            _config.Update(s =>
            {
                s.Current.LocalMods = mods;
                s.Current.ModUsage = mergedUsage;
                // 去重/整理/清理的数据源缓存：完整条目（含重复）+ 分卡引用 + 时点
                s.Current.ModEntries = entries;
                s.Current.CharaUsage = charaUsage;
                s.Current.SceneUsage = sceneUsage;
                s.Current.LastAnalysisTime = DateTime.Now;
            });
            ScanCompleted = true;

            if (_pendingMoveBack.Count > 0)
                MoveBackConfirmationRequested?.Invoke(this,
                    $"发现 {_pendingMoveBack.Count} 个被角色/场景卡引用或包含 shader 的 Mod 在 unusedmods 中。\n\n是否移回 mods 目录？");
            if (_pendingGuidRepairs.Count > 0)
                GuidRepairConfirmationRequested?.Invoke(this,
                    $"发现 {_pendingGuidRepairs.Count} 个 Mod 与卡片引用仅在 GUID 首尾空格上不同，且原始 GUID 均未精确匹配。\n" +
                    $"将直接修改 {_pendingGuidRepairs.Count(p => p.DirectGuid is not null)} 个源文件，" +
                    $"复制 {_pendingGuidRepairs.Sum(p => p.CopyGuids.Count)} 个 zipmod 以保留不同 GUID 的引用。\n\n是否修复？");
        }
        catch (Exception ex)
        {
            ScanError = $"分析失败：{ex.Message}";
            App.LogException(ex);
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// 阶段 1：扫描 mods 目录全部 zipmod 并解析 manifest；unusedDir（unusedmods）存在时一并扫描。
    /// 折叠后的 mods/unusedMods 供统计与移回候选（unusedMods 不进 LocalMods）；
    /// 完整条目列表（含重复 guid，与两字典共享 ModInfo 引用）持久化为去重/整理/清理的缓存数据源。
    /// </summary>
    private async Task<(Dictionary<string, ModInfo> Mods, Dictionary<string, ModInfo> UnusedMods, List<ModScanEntry> Entries)> ScanModsAsync(
        string modsDir, string? unusedDir)
    {
        var files = _scanner.ScanDirectory(modsDir, new ScanOptions { TargetExtension = { ".zipmod" } });
        var unusedFiles = unusedDir is not null && Directory.Exists(unusedDir)
            ? _scanner.ScanDirectory(unusedDir, new ScanOptions { TargetExtension = { ".zipmod" } })
            : new List<string>();
        var total = files.Count + unusedFiles.Count;
        ModScanProgress = $"0/{total}";

        var result = new Dictionary<string, ModInfo>(files.Count, StringComparer.OrdinalIgnoreCase);
        var unusedResult = new Dictionary<string, ModInfo>(unusedFiles.Count, StringComparer.OrdinalIgnoreCase);
        var entries = new List<ModScanEntry>(total);
        var gameId = _config.Settings.CurrentGame;
        var cachedMods = _analysisCache?.GetZipMods(gameId, "mods") ?? new(StringComparer.OrdinalIgnoreCase);
        var cachedUnused = _analysisCache?.GetZipMods(gameId, "unused") ?? new(StringComparer.OrdinalIgnoreCase);
        var validatedMods = new System.Collections.Concurrent.ConcurrentDictionary<string, ZipModAnalysisEntry>(StringComparer.OrdinalIgnoreCase);
        var validatedUnused = new System.Collections.Concurrent.ConcurrentDictionary<string, ZipModAnalysisEntry>(StringComparer.OrdinalIgnoreCase);
        var newCacheMods = new List<ZipModAnalysisEntry>(files.Count);
        var newCacheUnused = new List<ZipModAnalysisEntry>(unusedFiles.Count);
        var scanned = 0;
        foreach (var (batch, isUnused) in Batches(files, false).Concat(Batches(unusedFiles, true)))
        {
            var scope = isUnused ? "unused" : "mods";
            var cached = isUnused ? cachedUnused : cachedMods;
            var validated = isUnused ? validatedUnused : validatedMods;
            var batchEntries = await _scanner.ReadZipModBatchListAsync(batch, onError: LogScanError,
                tryGetCached: path =>
                {
                    if (cached.TryGetValue(path, out var entry) &&
                        AnalysisCacheService.IsCurrent(path, entry.FileLength, entry.LastWriteTimeUtcTicks))
                    {
                        validated[path] = entry;
                        return entry;
                    }
                    return null;
                });
            var target = isUnused ? unusedResult : result;
            var cacheTarget = isUnused ? newCacheUnused : newCacheMods;
            // 跨批合并同 guid：按去重规则裁决取最优（与 ReadZipModBatchAsync 批内口径一致）；
            // 完整列表逐条留存（重复 guid 不折叠），供去重/整理/清理复用
            foreach (var (guid, info) in batchEntries)
            {
                entries.Add(new ModScanEntry { Guid = guid, Info = info });
                if (!target.TryGetValue(guid, out var existing) || ScannerService.CompareModsForKeep(info, existing) < 0)
                    target[guid] = info;
                if (_analysisCache is not null)
                {
                    try
                    {
                        cacheTarget.Add(validated.TryGetValue(info.Path, out var hit)
                            ? hit
                            : ZipModAnalysisEntry.Create(scope, guid, info));
                    }
                    catch (Exception ex)
                    {
                        ErrorLog.Log($"Mod 缓存指纹读取失败: {info.Path}: {ex.Message}");
                    }
                }
            }
            scanned += batch.Count;
            ModScanProgress = $"{scanned}/{total}";
        }
        if (_analysisCache is not null)
        {
            await Task.Run(() =>
            {
                _analysisCache.ReplaceZipMods(gameId, "mods", newCacheMods);
                _analysisCache.ReplaceZipMods(gameId, "unused", newCacheUnused);
            });
        }
        return (result, unusedResult, entries);

        static IEnumerable<(List<string> Batch, bool IsUnused)> Batches(List<string> list, bool isUnused)
        {
            for (var i = 0; i < list.Count; i += BatchSize)
                yield return (list.GetRange(i, Math.Min(BatchSize, list.Count - i)), isUnused);
        }
    }

    /// <summary>
    /// 移回（执行阶段）：unusedmods 中被引用的 mod 平移回 mods 根目录（同名追加 _2/_3 后缀），
    /// 成功者补录进 LocalMods（Changed 事件驱动统计/缺失数刷新）。单文件失败记日志不中断。
    /// </summary>
    public async Task<(int Moved, int Failed)> ConfirmMoveBackAsync()
    {
        if (_pendingMoveBack is not { } pending)
            return (0, 0);
        _pendingMoveBack = null;

        var modsDir = _config.GetModsDir();
        if (modsDir is null)
            return (0, pending.Count);

        var movedPairs = new List<(string Guid, ModInfo Info)>();
        var failed = 0;
        await Task.Run(() =>
        {
            foreach (var (guid, info) in pending)
            {
                try
                {
                    var target = ScannerService.UniqueTargetPath(modsDir, info.Path);
                    _scanner.MoveFile(info.Path, target);
                    info.Path = target;
                    movedPairs.Add((guid, info));
                }
                catch (Exception ex)
                {
                    failed++;
                    ErrorLog.Log($"unusedmods 移回失败: {info.Path}: {ex.Message}");
                }
            }
        });

        if (movedPairs.Count > 0)
            _config.Update(s =>
            {
                foreach (var (guid, info) in movedPairs)
                    s.Current.LocalMods[guid] = info;
            });
        UnusedModCount -= movedPairs.Count;
        return (movedPairs.Count, failed);
    }

    public void DismissGuidRepair() => _pendingGuidRepairs = null;

    /// <summary>执行用户确认的 GUID 修复：未被引用的源直接改首个目标，其余目标复制。</summary>
    public async Task<(int Modified, int Copied, int Failed)> ConfirmGuidRepairAsync()
    {
        if (_pendingGuidRepairs is not { } pending)
            return (0, 0, 0);
        _pendingGuidRepairs = null;
        var operations = pending.Select(plan =>
        {
            var info = _config.Settings.Current.LocalMods[plan.CurrentGuid];
            return (Plan: plan, Info: info);
        }).ToList();
        var completed = new List<(ZipModGuidRepairPlan Plan, ModInfo Info, ZipModGuidRepairResult Result)>();
        var failed = 0;
        await Task.Run(() =>
        {
            foreach (var operation in operations)
            {
                if (operation.Plan.DirectGuid is { } directGuid)
                    Execute(operation.Plan, operation.Info, directGuid, false);
                foreach (var copyGuid in operation.Plan.CopyGuids)
                    Execute(operation.Plan, operation.Info, copyGuid, true);
            }

            void Execute(ZipModGuidRepairPlan plan, ModInfo info, string targetGuid, bool copy)
            {
                try
                {
                    var result = ZipModGuidRepairService.RewriteGuid(plan.SourcePath, targetGuid, copy);
                    completed.Add((plan, info, result));
                }
                catch (Exception ex)
                {
                    failed++;
                    ErrorLog.Log($"GUID 修复失败: {plan.SourcePath} -> {targetGuid}: {ex.Message}");
                }
            }
        });

        if (completed.Count > 0)
        {
            _config.Update(s =>
            {
                foreach (var (plan, info, result) in completed)
                {
                    info.FileLength = new FileInfo(result.OutputPath).Length;
                    info.LastWriteTimeUtcTicks = File.GetLastWriteTimeUtc(result.OutputPath).Ticks;
                    if (result.IsCopy)
                    {
                        var copyInfo = new ModInfo
                        {
                            Name = info.Name,
                            Version = info.Version,
                            Path = result.OutputPath,
                            FileLength = info.FileLength,
                            LastWriteTimeUtcTicks = info.LastWriteTimeUtcTicks,
                            ShaderNames = new List<string>(info.ShaderNames),
                        };
                        s.Current.LocalMods[result.NewGuid] = copyInfo;
                        s.Current.ModEntries.Add(new ModScanEntry { Guid = result.NewGuid, Info = copyInfo });
                    }
                    else
                    {
                        s.Current.LocalMods.Remove(plan.CurrentGuid);
                        s.Current.LocalMods[result.NewGuid] = info;
                        foreach (var entry in s.Current.ModEntries.Where(e =>
                                     e.Info.Path.Equals(result.SourcePath, StringComparison.OrdinalIgnoreCase)))
                            entry.Guid = result.NewGuid;
                    }
                }
            });
        }
        return (completed.Count(r => !r.Result.IsCopy), completed.Count(r => r.Result.IsCopy), failed);
    }

    /// <summary>阶段 2/3：扫描 PNG 目录并统计 Mod 引用次数，未变化卡片直接复用缓存。</summary>
    private async Task<Dictionary<string, int>> ScanPngUsageAsync(string dir, string scope, Action<string> report)
    {
        var files = _scanner.ScanDirectory(dir, new ScanOptions { TargetExtension = { ".png" } })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        report($"0/{files.Count}");

        var gameId = _config.Settings.CurrentGame;
        var cached = _analysisCache?.GetCards(gameId, scope) ?? new(StringComparer.OrdinalIgnoreCase);
        var validated = new System.Collections.Concurrent.ConcurrentDictionary<string, CardAnalysisEntry>(StringComparer.OrdinalIgnoreCase);
        var cacheEntries = new List<CardAnalysisEntry>(files.Count);
        var usage = new Dictionary<string, int>();
        for (var i = 0; i < files.Count; i += BatchSize)
        {
            var batch = files.GetRange(i, Math.Min(BatchSize, files.Count - i));
            var batchResults = await _scanner.ReadPngDataBatchAsync(batch, onError: LogScanError,
                tryGetCached: path =>
                {
                    if (cached.TryGetValue(path, out var entry) &&
                        AnalysisCacheService.IsCurrent(path, entry.FileLength, entry.LastWriteTimeUtcTicks))
                    {
                        validated[path] = entry;
                        return entry.ToResult();
                    }
                    return null;
                });
            foreach (var item in batchResults)
            {
                foreach (var modId in item.ModIDs)
                    usage[modId] = usage.TryGetValue(modId, out var c) ? c + 1 : 1;
                if (_analysisCache is not null)
                    cacheEntries.Add(validated.TryGetValue(item.Path, out var hit)
                        ? hit
                        : CardAnalysisEntry.FromResult(scope, item));
            }
            report($"{Math.Min(i + BatchSize, files.Count)}/{files.Count}");
        }
        if (_analysisCache is not null)
            await Task.Run(() => _analysisCache.ReplaceCards(gameId, scope, cacheEntries));
        return usage;
    }

    private static void LogScanError(string message) => App.LogException(new Exception(message));

    // ==================== 快速启动（A1：原版按钮无事件，C# 版接通 GameLauncherService） ====================

    [ObservableProperty]
    private string _launchStatusText = "";

    [RelayCommand(CanExecute = nameof(IsGamePathValid))]
    private void LaunchGame() => Launch(_launcher.LaunchGame);

    [RelayCommand(CanExecute = nameof(IsGamePathValid))]
    private void LaunchStudio() => Launch(_launcher.LaunchStudio);

    private void Launch(Action launch)
    {
        try
        {
            launch();
            LaunchStatusText = "";
        }
        catch (Exception ex)
        {
            LaunchStatusText = $"启动失败：{ex.Message}";
            App.LogException(ex);
        }
    }

    // ==================== Sideloader 更新（运行/停止；完成后结果落盘——修复原版更新不生效） ====================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchGameCommand))]
    [NotifyPropertyChangedFor(nameof(SideloaderButtonText))]
    [NotifyPropertyChangedFor(nameof(IsSideloaderRunning))]
    [NotifyPropertyChangedFor(nameof(SideloaderSucceeded))]
    [NotifyPropertyChangedFor(nameof(SideloaderStopped))]
    [NotifyPropertyChangedFor(nameof(SideloaderFailed))]
    private SideloaderUiState _sideloaderState = SideloaderUiState.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SideloaderButtonText))]
    private string _sideloaderMessage = "";

    [ObservableProperty]
    private int _sideloaderFoundCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SideloaderProgressDetail))]
    private int _sideloaderProcessedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SideloaderProgressDetail))]
    private int _sideloaderFailedCount;

    /// <summary>运行中的进度明细（已处理 / 失败）：长时间爬取时让用户判断是否卡死</summary>
    public string SideloaderProgressDetail => $"已处理 {SideloaderProcessedCount} 个，失败 {SideloaderFailedCount} 个";

    [ObservableProperty]
    private string _sideloaderError = "";

    public bool IsSideloaderRunning => SideloaderState == SideloaderUiState.Running;
    public bool SideloaderSucceeded => SideloaderState == SideloaderUiState.Success;
    public bool SideloaderStopped => SideloaderState == SideloaderUiState.Stopped;
    public bool SideloaderFailed => SideloaderState is SideloaderUiState.Error or SideloaderUiState.Partial;

    public string SideloaderButtonText => SideloaderState switch
    {
        SideloaderUiState.Running => _stopRequested ? "正在停止..." : "点击停止更新",
        SideloaderUiState.Success => "更新成功",
        SideloaderUiState.Error or SideloaderUiState.Partial => "重试更新",
        _ => "更新 Sideload 数据",
    };

    /// <summary>切换运行/停止。运行中点击 → 弹确认（原版 Modal.confirm 语义）</summary>
    [RelayCommand]
    private void ToggleSideloader()
    {
        if (SideloaderState == SideloaderUiState.Running)
        {
            // 已在停止流程中则忽略重复点击（原版行为）
            if (!_stopRequested)
                StopConfirmationRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        _ = RunSideloaderAsync();
    }

    /// <summary>View 确认停止后调用：置标志 + 取消（Cancel 经 CancellationToken 中断在飞请求）</summary>
    public void ConfirmStopSideloader()
    {
        if (SideloaderState != SideloaderUiState.Running)
            return;
        _stopRequested = true;
        SideloaderMessage = "正在停止...";
        OnPropertyChanged(nameof(SideloaderButtonText));
        _currentSideloader?.Cancel();
    }

    private async Task RunSideloaderAsync()
    {
        var loader = _sideloaderFactory();
        _currentSideloader = loader;
        _stopRequested = false;
        SideloaderState = SideloaderUiState.Running;
        SideloaderMessage = "正在启动...";
        SideloaderFoundCount = 0;
        SideloaderProcessedCount = 0;
        SideloaderFailedCount = 0;
        SideloaderError = "";

        try
        {
            var result = await loader.RunAsync(
                onLog: msg =>
                {
                    // 原版只显示 Processing 消息（正在分析的目录）
                    const string prefix = "Processing: ";
                    if (msg.Contains("Processing:", StringComparison.Ordinal))
                    {
                        var i = msg.IndexOf(prefix, StringComparison.Ordinal);
                        UiDispatch.Run(() => SideloaderMessage = msg[(i + prefix.Length)..]);
                    }
                    else if (!msg.StartsWith("Found: ", StringComparison.Ordinal))
                    {
                        // 单页失败、manifest 超限等留痕；"Found: guid" 每条 Mod 一条，不进 error.log（洪水）
                        ErrorLog.Log(msg);
                    }
                },
                onProgress: new Progress<SideloaderProgress>(p =>
                {
                    SideloaderFoundCount = p.Current;
                    SideloaderProcessedCount = p.Processed;
                    SideloaderFailedCount = p.Failed;
                }),
                known: _sideloadDb.GetKnownMods());
            SideloaderFoundCount = result.DiscoveredMods;

            if (_stopRequested)
            {
                // 取消后 Run 正常返回部分结果：不更新数据库，回到已停止状态
                SideloaderState = SideloaderUiState.Stopped;
                SideloaderMessage = "已停止";
                SaveSideloadScanMeta(new SideloadScanMeta
                {
                    LastScanTime = DateTime.Now,
                    Status = SideloadScanStatus.Stopped,
                    FoundCount = result.DiscoveredMods,
                });
            }
            else if (result.HasFailures)
            {
                _pendingSideloadResult = result;
                SideloaderState = SideloaderUiState.Partial;
                SideloaderError = $"部分失败：{result.FailedPages} 个目录、{result.FailedMods} 个 Mod 获取失败，等待确认";
                SwitchGameCommand.NotifyCanExecuteChanged();
                var message = $"本次更新有 {result.FailedPages} 个目录、{result.FailedMods} 个 Mod 获取失败，" +
                              $"已发现 {result.DiscoveredMods} 个 Mod。\n\n" +
                              "使用本次结果可能缺少抓取失败的内容，是否仍要更新本地 Sideload 数据？";
                if (PartialResultConfirmationRequested is { } handler)
                    handler.Invoke(this, message);
                else
                    ConfirmPartialSideloaderResult(false);
            }
            else if (result.Mods.Count == 0)
            {
                SideloaderState = SideloaderUiState.Partial;
                SideloaderError = "未发现任何 Mod，为避免清空数据库已保留旧数据";
                SaveSideloadScanMeta(new SideloadScanMeta
                {
                    LastScanTime = DateTime.Now,
                    Status = SideloadScanStatus.PartialFailure,
                    FoundCount = result.DiscoveredMods,
                });
            }
            else
            {
                _sideloadDb.Update(result.Mods);
                SideloaderState = SideloaderUiState.Success;
                SideloaderMessage = $"已发现 {result.DiscoveredMods} 个 Mods";
                SaveSideloadScanMeta(new SideloadScanMeta
                {
                    LastScanTime = DateTime.Now,
                    Status = SideloadScanStatus.Success,
                    FoundCount = result.DiscoveredMods,
                });
            }
        }
        catch (Exception ex)
        {
            SideloaderState = SideloaderUiState.Error;
            SideloaderError = ex.Message;
            App.LogException(ex);
            SaveSideloadScanMeta(new SideloadScanMeta
            {
                LastScanTime = DateTime.Now,
                Status = SideloadScanStatus.Error,
                Error = ex.Message,
            });
        }
        finally
        {
            _currentSideloader = null;
        }
    }

    /// <summary>应用或丢弃等待确认的部分抓取结果；确认前禁止切换游戏数据源</summary>
    public void ConfirmPartialSideloaderResult(bool useResult)
    {
        if (_pendingSideloadResult is not { } result)
            return;
        _pendingSideloadResult = null;
        var failedCount = result.FailedPages + result.FailedMods;
        try
        {
            if (useResult)
            {
                _sideloadDb.Update(result.Mods);
                SideloaderState = SideloaderUiState.Success;
                SideloaderError = "";
                SideloaderMessage = $"已使用本次结果：发现 {result.DiscoveredMods} 个 Mods，{failedCount} 处获取失败";
            }
            else
            {
                SideloaderState = SideloaderUiState.Partial;
                SideloaderError = $"部分失败：{result.FailedPages} 个目录、{result.FailedMods} 个 Mod 获取失败，已保留旧数据";
            }
            SaveSideloadScanMeta(new SideloadScanMeta
            {
                LastScanTime = DateTime.Now,
                Status = SideloadScanStatus.PartialFailure,
                FoundCount = result.DiscoveredMods,
                FailedCount = failedCount,
                ResultApplied = useResult,
            });
        }
        catch (Exception ex)
        {
            SideloaderState = SideloaderUiState.Error;
            SideloaderError = $"应用本次结果失败：{ex.Message}";
            App.LogException(ex);
            SaveSideloadScanMeta(new SideloadScanMeta
            {
                LastScanTime = DateTime.Now,
                Status = SideloadScanStatus.Error,
                FoundCount = result.DiscoveredMods,
                Error = ex.Message,
            });
        }
        finally
        {
            SwitchGameCommand.NotifyCanExecuteChanged();
        }
    }

    private void SaveSideloadScanMeta(SideloadScanMeta meta)
    {
        try
        {
            _sideloadDb.SaveMeta(meta);
        }
        catch (Exception metaEx)
        {
            App.LogException(new Exception($"Sideload meta save failed: {metaEx.Message}"));
        }
    }

    // ==================== 缺失 Mod 批量补全（串行下载 + 进度；对应原版 SideloadInit + ProgressModal） ====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloadableMods))]
    [NotifyPropertyChangedFor(nameof(ShowModsReady))]
    [NotifyPropertyChangedFor(nameof(ComplementButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ComplementMissingModsCommand))]
    private int _downloadableModCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingMods))]
    [NotifyPropertyChangedFor(nameof(ShowModsReady))]
    private int _missingModCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ComplementMissingModsCommand))]
    private bool _isComplementing;

    [ObservableProperty]
    private string _complementProgress = "";

    [ObservableProperty]
    private string _complementCurrentName = "";

    public bool HasDownloadableMods => DownloadableModCount > 0;

    public bool HasMissingMods => MissingModCount > 0;

    public string ComplementButtonText => IsComplementing ? "补全中..." : $"补全可下载 {DownloadableModCount} 个 Mods";

    /// <summary>可下载清单：被引用但本地不存在、且 sideload 库中有的 guid（原版 SideloadInit 的 downloadList）</summary>
    private List<(string Guid, string Url)> ComputeDownloadableMods()
    {
        var usage = _config.Settings.Current.ModUsage;
        var localMods = _config.Settings.Current.LocalMods;
        var db = _sideloadDb.Database;
        return usage.Keys
            .Where(guid => !localMods.ContainsKey(guid) && db.ContainsKey(guid))
            .Select(guid => (guid, db[guid]))
            .ToList();
    }

    private bool CanComplement() => IsGamePathValid && HasDownloadableMods && !IsComplementing;

    [RelayCommand(CanExecute = nameof(CanComplement))]
    private async Task ComplementMissingModsAsync()
    {
        var dir = _config.GetModDownloadDir();
        if (dir is null)
            return;

        var list = ComputeDownloadableMods();
        if (list.Count == 0)
            return;

        IsComplementing = true;
        ComplementProgress = $"0/{list.Count}";
        var remaining = list.Select(i => i.Guid).ToHashSet();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ok = 0;

        void OnProgress(object? s, DownloadTask t)
        {
            lock (remaining)
            {
                if (t.Status == DownloadTaskStatus.Downloading && remaining.Contains(t.Id))
                    UiDispatch.Run(() => ComplementCurrentName = t.Id);
            }
        }

        void OnFinished(object? s, DownloadTask t)
        {
            bool allFinished;
            int finishedCount;
            lock (remaining)
            {
                if (!remaining.Remove(t.Id))
                    return;
                if (t.Status == DownloadTaskStatus.Completed)
                    ok++;
                finishedCount = list.Count - remaining.Count;
                allFinished = remaining.Count == 0;
            }

            UiDispatch.Run(() => ComplementProgress = $"{finishedCount}/{list.Count}");
            if (allFinished)
                done.TrySetResult();
        }

        _downloads.TaskProgress += OnProgress;
        _downloads.TaskFinished += OnFinished;
        try
        {
            // 一次性提交全部任务，让下载窗口立即显示当前任务与所有等待中任务。
            foreach (var (guid, url) in list)
            {
                // 同名任务已在队列时 StartDownload 返回 false——等待在途任务完成即可。
                _downloads.StartDownload(guid, url, dir,
                    DownloadBaseUrlOverride ?? _config.CurrentProfile.SideloadBaseUrl);
            }
            await done.Task;

            // 结束汇总：失败/取消要体现在主页（原来失败只在下载窗口可见）
            ComplementProgress = ok == list.Count
                ? $"{list.Count}/{list.Count} 补全完成"
                : $"完成：成功 {ok}，失败 {list.Count - ok}";
        }
        finally
        {
            _downloads.TaskProgress -= OnProgress;
            _downloads.TaskFinished -= OnFinished;
            IsComplementing = false;
            ComplementCurrentName = "";
        }
    }

    // ==================== 数据概览（对应原版 QuickStats） ====================

    [ObservableProperty] private int _modCount;
    [ObservableProperty] private int _usageCount;
    [ObservableProperty] private int _totalRefs;

    /// <summary>已有扫描数据（mods 库或被引用记录非空）——未扫描时不显示"所有 Mods 已就绪"</summary>
    public bool HasScanData => ModCount > 0 || UsageCount > 0;

    /// <summary>"所有 Mods 已就绪"文案可见性：有扫描数据且可下载与缺失均为 0（避免无数据时误显示）</summary>
    public bool ShowModsReady => HasScanData && !HasDownloadableMods && !HasMissingMods;

    internal void RefreshStats()
    {
        var settings = _config.Settings.Current;
        ModCount = settings.LocalMods.Count;
        UsageCount = settings.ModUsage.Count;
        TotalRefs = settings.ModUsage.Values.Sum();
        var db = _sideloadDb.Database;
        DownloadableModCount = settings.ModUsage.Keys
            .Count(guid => !settings.LocalMods.ContainsKey(guid) && db.ContainsKey(guid));
        MissingModCount = settings.ModUsage.Keys
            .Count(guid => !settings.LocalMods.ContainsKey(guid) && !db.ContainsKey(guid));
        OnPropertyChanged(nameof(HasScanData));
        OnPropertyChanged(nameof(ShowModsReady));
    }
}
