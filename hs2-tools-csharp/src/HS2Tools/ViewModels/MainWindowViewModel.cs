using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Models;
using HS2Tools.Services;
using Microsoft.Win32;

namespace HS2Tools.ViewModels;

/// <summary>Sideloader 更新区块的 UI 状态（对应原版 sideloadUpdateStatus）</summary>
public enum SideloaderUiState
{
    Idle,
    Running,
    Success,
    Stopped,
    Error,
}

/// <summary>
/// 主窗口（首页）ViewModel：路径设置、数据分析（三阶段扫描）、快速启动（A1 修复）、
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

    private ISideloaderService? _currentSideloader;
    private bool _stopRequested;

    public MainWindowViewModel(
        ConfigService config,
        ScannerService scanner,
        DownloadManager downloads,
        GameLauncherService launcher,
        SideloadDatabaseService sideloadDb,
        Func<ISideloaderService> sideloaderFactory)
    {
        _config = config;
        _scanner = scanner;
        _downloads = downloads;
        _launcher = launcher;
        _sideloadDb = sideloadDb;
        _sideloaderFactory = sideloaderFactory;

        GamePath = config.Settings.GamePath; // setter 链触发校验
        RefreshStats();

        // 其他窗口改配置（代理/收藏）或爬虫更新数据库后，统计与缺失数自行刷新
        _config.Changed += (_, _) => UiDispatch.Run(RefreshStats);
        _sideloadDb.Changed += (_, _) => UiDispatch.Run(RefreshStats);
    }

    /// <summary>请求用户确认停止爬虫（View 订阅弹确认框，确认后调 <see cref="ConfirmStopSideloader"/>）</summary>
    public event EventHandler? StopConfirmationRequested;

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
        IsGamePathValid = ValidateGamePath(value);
        PathStatusText = IsGamePathValid
            ? $"已验证：找到 {ConfigService.GameExeName}"
            : $"未找到 {ConfigService.GameExeName}，请选择游戏目录";

        // 与已加载值相同则不重复写盘（避免每次启动重写配置文件）
        if (value != _config.Settings.GamePath)
            _config.Update(s => s.GamePath = value);
    }

    /// <summary>选择游戏 exe（对应原版 SelectPath：选 exe 取目录）</summary>
    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = $"选择 {ConfigService.GameExeName}",
            Filter = $"{ConfigService.GameExeName}|{ConfigService.GameExeName}|可执行文件 (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
            GamePath = Path.GetDirectoryName(dialog.FileName) ?? "";
    }

    /// <summary>游戏路径校验：目录下存在 HoneySelect2.exe</summary>
    public static bool ValidateGamePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, ConfigService.GameExeName));

    // ==================== 数据分析（对应原版 Scan：顺序 scanMods → scanScene → scanFemale） ====================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
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

        try
        {
            var mods = await ScanModsAsync(_config.GetModsDir()!);
            ScanStep = 1;
            ModScanDone = true;

            var sceneUsage = await ScanPngUsageAsync(_config.GetSceneDir()!, p => SceneScanProgress = p);
            ScanStep = 2;
            SceneScanDone = true;

            var charaUsage = await ScanPngUsageAsync(_config.GetCharaDir()!, p => CharaScanProgress = p);
            ScanStep = 3;
            CharaScanDone = true;

            // 原版合并语义：{ ...scene, ...female } —— 同 guid 时角色统计覆盖场景统计
            var mergedUsage = new Dictionary<string, int>(sceneUsage);
            foreach (var (guid, count) in charaUsage)
                mergedUsage[guid] = count;

            _config.Update(s =>
            {
                s.LocalMods = mods;
                s.ModUsage = mergedUsage;
            });
            ScanCompleted = true;
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

    /// <summary>阶段 1：扫描 mods 目录全部 zipmod 并解析 manifest</summary>
    private async Task<Dictionary<string, ModInfo>> ScanModsAsync(string modsDir)
    {
        var files = _scanner.ScanDirectory(modsDir, new ScanOptions { TargetExtension = { ".zipmod" } });
        ModScanProgress = $"0/{files.Count}";

        var result = new Dictionary<string, ModInfo>();
        for (var i = 0; i < files.Count; i += BatchSize)
        {
            var batch = files.GetRange(i, Math.Min(BatchSize, files.Count - i));
            var batchResult = await _scanner.ReadZipModBatchAsync(batch, onError: LogScanError);
            foreach (var (guid, info) in batchResult)
                result[guid] = info;
            ModScanProgress = $"{Math.Min(i + BatchSize, files.Count)}/{files.Count}";
        }
        return result;
    }

    /// <summary>阶段 2/3：扫描 PNG 目录并统计 Mod 引用次数</summary>
    private async Task<Dictionary<string, int>> ScanPngUsageAsync(string dir, Action<string> report)
    {
        var files = _scanner.ScanDirectory(dir, new ScanOptions { TargetExtension = { ".png" } });
        report($"0/{files.Count}");

        var usage = new Dictionary<string, int>();
        for (var i = 0; i < files.Count; i += BatchSize)
        {
            var batch = files.GetRange(i, Math.Min(BatchSize, files.Count - i));
            var batchResults = await _scanner.ReadPngModsBatchAsync(batch, onError: LogScanError);
            foreach (var item in batchResults)
            {
                foreach (var modId in item.ModIDs)
                    usage[modId] = usage.TryGetValue(modId, out var c) ? c + 1 : 1;
            }
            report($"{Math.Min(i + BatchSize, files.Count)}/{files.Count}");
        }
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
    private string _sideloaderError = "";

    public bool IsSideloaderRunning => SideloaderState == SideloaderUiState.Running;
    public bool SideloaderSucceeded => SideloaderState == SideloaderUiState.Success;
    public bool SideloaderStopped => SideloaderState == SideloaderUiState.Stopped;
    public bool SideloaderFailed => SideloaderState == SideloaderUiState.Error;

    public string SideloaderButtonText => SideloaderState switch
    {
        SideloaderUiState.Running => _stopRequested ? "正在停止..." : "点击停止更新",
        SideloaderUiState.Success => "更新成功",
        SideloaderUiState.Error => "重试更新",
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

    /// <summary>View 确认停止后调用：置标志 + 取消（Cancel 只置标志位，请求不中断）</summary>
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
                    else
                    {
                        ErrorLog.Log(msg); // 单页失败、manifest 超限等非 Processing 消息留痕
                    }
                },
                onProgress: new Progress<SideloaderProgress>(p => SideloaderFoundCount = p.Current));

            if (_stopRequested)
            {
                // 取消后 Run 正常返回部分结果：不更新数据库，回到已停止状态
                SideloaderState = SideloaderUiState.Stopped;
                SideloaderMessage = "已停止";
            }
            else
            {
                _sideloadDb.Update(result);
                SideloaderState = SideloaderUiState.Success;
                SideloaderMessage = $"已发现 {result.Count} 个 Mods";
            }
        }
        catch (Exception ex)
        {
            SideloaderState = SideloaderUiState.Error;
            SideloaderError = ex.Message;
            App.LogException(ex);
        }
        finally
        {
            _currentSideloader = null;
        }
    }

    // ==================== 缺失 Mod 批量补全（串行下载 + 进度；对应原版 SideloadInit + ProgressModal） ====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingMods))]
    [NotifyPropertyChangedFor(nameof(ShowModsReady))]
    [NotifyPropertyChangedFor(nameof(ComplementButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ComplementMissingModsCommand))]
    private int _missingModCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ComplementMissingModsCommand))]
    private bool _isComplementing;

    [ObservableProperty]
    private string _complementProgress = "";

    [ObservableProperty]
    private string _complementCurrentName = "";

    public bool HasMissingMods => MissingModCount > 0;

    public string ComplementButtonText => IsComplementing ? "补全中..." : $"补全缺失 {MissingModCount} 个 Mods";

    /// <summary>缺失清单：被引用但本地不存在、且 sideload 库中有的 guid（原版 SideloadInit 的 downloadList）</summary>
    private List<(string Guid, string Url)> ComputeMissingMods()
    {
        var usage = _config.Settings.ModUsage;
        var localMods = _config.Settings.LocalMods;
        var db = _sideloadDb.Database;
        return usage.Keys
            .Where(guid => !localMods.ContainsKey(guid) && db.ContainsKey(guid))
            .Select(guid => (guid, db[guid]))
            .ToList();
    }

    private bool CanComplement() => IsGamePathValid && HasMissingMods && !IsComplementing;

    [RelayCommand(CanExecute = nameof(CanComplement))]
    private async Task ComplementMissingModsAsync()
    {
        var dir = _config.GetModDownloadDir();
        if (dir is null)
            return;

        var list = ComputeMissingMods();
        if (list.Count == 0)
            return;

        IsComplementing = true;
        ComplementProgress = $"0/{list.Count}";
        var ok = 0;
        try
        {
            for (var i = 0; i < list.Count; i++)
            {
                var (guid, url) = list[i];
                ComplementCurrentName = guid;

                // 等待该任务到达终态（完成/失败/取消都算，失败不阻断后续——原版 catch 后继续）
                var done = new TaskCompletionSource();
                void OnFinished(object? s, DownloadTask t)
                {
                    if (t.Id == guid)
                    {
                        if (t.Status == DownloadTaskStatus.Completed)
                            ok++;
                        done.TrySetResult();
                    }
                }
                _downloads.TaskFinished += OnFinished;
                try
                {
                    // 同名任务正在下载时 StartDownload 返回 false——等待在途任务完成即可
                    _downloads.StartDownload(guid, url, dir);
                    await done.Task;
                }
                finally
                {
                    _downloads.TaskFinished -= OnFinished;
                }
                ComplementProgress = $"{i + 1}/{list.Count}";
            }

            // 结束汇总：失败/取消要体现在主页（原来失败只在下载窗口可见）
            ComplementProgress = ok == list.Count
                ? $"{list.Count}/{list.Count} 补全完成"
                : $"完成：成功 {ok}，失败 {list.Count - ok}";
        }
        finally
        {
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

    /// <summary>"所有 Mods 已就绪"文案可见性：有扫描数据且无缺失（避免无数据时误显示）</summary>
    public bool ShowModsReady => HasScanData && !HasMissingMods;

    internal void RefreshStats()
    {
        ModCount = _config.Settings.LocalMods.Count;
        UsageCount = _config.Settings.ModUsage.Count;
        TotalRefs = _config.Settings.ModUsage.Values.Sum();
        MissingModCount = ComputeMissingMods().Count;
        OnPropertyChanged(nameof(HasScanData));
        OnPropertyChanged(nameof(ShowModsReady));
    }
}
