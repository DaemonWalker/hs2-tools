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

    public SceneWindowViewModel(
        ConfigService config,
        ScannerService scanner,
        DownloadManager downloads,
        SideloadDatabaseService sideloadDb,
        GameLauncherService launcher)
    {
        _config = config;
        _scanner = scanner;
        Detail = new CardDetailViewModel(config, scanner, downloads, sideloadDb, launcher);
        Organize = new SceneOrganizeViewModel(config, scanner);
        // 整理完成 → 重扫场景目录（对应原版 onOrganizeComplete → cardGrid.reload）
        Organize.OrganizeCompleted += (_, _) => LoadCardPaths();
    }

    /// <summary>详情面板（选择变化时自动加载）</summary>
    public CardDetailViewModel Detail { get; }

    /// <summary>智能整理</summary>
    public SceneOrganizeViewModel Organize { get; }

    /// <summary>目录扫描到的全部场景路径</summary>
    [ObservableProperty]
    private List<string> _allPaths = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private string? _selectedPath;

    public bool HasSelection => SelectedPath is not null;

    /// <summary>网格空态文案：未设游戏路径时给引导</summary>
    public string EmptyText => _config.GetSceneDir() is null
        ? "未设置游戏路径，请先在首页「游戏路径」中选择游戏目录"
        : "暂无数据";

    partial void OnSelectedPathChanged(string? value) => _ = Detail.LoadAsync(value, () => SelectedPath);

    /// <summary>扫描场景目录（窗口加载/点刷新）。对应原版 getAllFiles(scenePath, .png)</summary>
    public void LoadCardPaths()
    {
        var dir = _config.GetSceneDir();
        AllPaths = dir is null
            ? new List<string>()
            : _scanner.ScanDirectory(dir, new ScanOptions { TargetExtension = { ".png" } });
        OnPropertyChanged(nameof(EmptyText));
    }
}
