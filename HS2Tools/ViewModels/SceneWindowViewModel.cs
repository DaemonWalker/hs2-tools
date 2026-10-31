using CommunityToolkit.Mvvm.ComponentModel;
using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.ViewModels;

/// <summary>
/// 场景库窗口 ViewModel：场景网格（搜索/排序由 CardGridControl 处理）+ 智能整理。
/// 详情逻辑复用 <see cref="CardDetailViewModel"/>。
/// </summary>
public partial class SceneWindowViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly ScannerService _scanner;
    private List<string> _scannedPaths = new();

    private string _loadedGameId;
    private string _loadedGamePath;

    public SceneWindowViewModel(
        ConfigService config,
        ScannerService scanner,
        DownloadManager downloads,
        SideloadDatabaseService sideloadDb,
        GameLauncherService launcher,
        AnalysisCacheService? analysisCache = null)
    {
        _config = config;
        _scanner = scanner;
        _loadedGameId = config.Settings.CurrentGame;
        _loadedGamePath = config.Settings.Current.GamePath;
        Detail = new CardDetailViewModel(config, scanner, downloads, sideloadDb, launcher, analysisCache, "scene");
        Organize = new SceneOrganizeViewModel(config, scanner, analysisCache);
        // 整理完成 → 重扫场景目录（对应原版 onOrganizeComplete → cardGrid.reload）
        Organize.OrganizeCompleted += (_, _) => LoadCardPaths();
        _config.Changed += (_, _) => UiDispatch.Run(ReloadIfGameContextChanged);
    }

    /// <summary>详情面板（选择变化时自动加载）</summary>
    public CardDetailViewModel Detail { get; }

    /// <summary>智能整理</summary>
    public SceneOrganizeViewModel Organize { get; }

    [ObservableProperty]
    private List<CardDirectoryNode> _directories = new();

    [ObservableProperty]
    private CardDirectoryNode? _selectedDirectory;

    /// <summary>当前目录直接包含的场景路径</summary>
    [ObservableProperty]
    private List<string> _allPaths = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private string? _selectedPath;

    public bool HasSelection => SelectedPath is not null;

    public string EmptyText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_config.Settings.Current.GamePath))
                return "未设置游戏路径，请先在工作台选择游戏目录";
            if (!_config.IsCurrentGamePathValid)
                return $"游戏路径无效：未找到 {_config.CurrentProfile.GameExeName}，请先在工作台修正目录";
            return "暂无数据";
        }
    }

    partial void OnSelectedPathChanged(string? value) => _ = Detail.LoadAsync(value, () => SelectedPath);

    partial void OnSelectedDirectoryChanged(CardDirectoryNode? value)
    {
        AllPaths = value is null
            ? new List<string>()
            : CardDirectoryTree.FilterDirectPaths(_scannedPaths, value.FullPath);
        if (SelectedPath is not null && !AllPaths.Contains(SelectedPath, StringComparer.OrdinalIgnoreCase))
            SelectedPath = null;
    }

    private void ReloadIfGameContextChanged()
    {
        if (_config.Settings.CurrentGame == _loadedGameId
            && _config.Settings.Current.GamePath == _loadedGamePath)
            return;
        SelectedPath = null;
        LoadCardPaths();
    }

    /// <summary>扫描场景目录，默认选中根目录并仅展示其直接包含的场景。</summary>
    public void LoadCardPaths()
    {
        _loadedGameId = _config.Settings.CurrentGame;
        _loadedGamePath = _config.Settings.Current.GamePath;
        var dir = _config.GetSceneDir();
        var valid = dir is not null && _config.IsCurrentGamePathValid;
        _scannedPaths = !valid
            ? new List<string>()
            : _scanner.ScanDirectory(dir!, new ScanOptions { TargetExtension = { ".png" } });

        SelectedDirectory = null;
        Directories = new List<CardDirectoryNode>();
        if (valid)
        {
            var root = CardDirectoryTree.Build(dir!, _scanner.ScanDirectories(dir!));
            SubscribeDirectoryEvents(root);
            root.IsExpanded = true;
            Directories = new List<CardDirectoryNode> { root };
            root.IsSelected = true;
        }
        OnPropertyChanged(nameof(EmptyText));
    }

    private void SubscribeDirectoryEvents(CardDirectoryNode node)
    {
        node.Selected += (_, _) => SelectedDirectory = node;
        foreach (var child in node.Children)
            SubscribeDirectoryEvents(child);
    }
}
