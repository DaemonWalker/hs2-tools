using CommunityToolkit.Mvvm.ComponentModel;
using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.ViewModels;

/// <summary>
/// 角色卡浏览窗口 ViewModel：网格数据（搜索/排序由 CardGridControl 处理）+ 卡片路径扫描。
/// 详情逻辑在 <see cref="CardDetailViewModel"/>（角色/场景共用）。
/// </summary>
public partial class CharaWindowViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly ScannerService _scanner;

    public CharaWindowViewModel(
        ConfigService config,
        ScannerService scanner,
        DownloadManager downloads,
        SideloadDatabaseService sideloadDb,
        GameLauncherService launcher)
    {
        _config = config;
        _scanner = scanner;
        Detail = new CardDetailViewModel(config, scanner, downloads, sideloadDb, launcher);
    }

    /// <summary>详情面板（选择变化时自动加载）</summary>
    public CardDetailViewModel Detail { get; }

    /// <summary>目录扫描到的全部卡片路径（控件负责过滤/排序）</summary>
    [ObservableProperty]
    private List<string> _allPaths = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private string? _selectedPath;

    public bool HasSelection => SelectedPath is not null;

    /// <summary>网格空态文案：未设游戏路径时给引导</summary>
    public string EmptyText => _config.GetCharaDir() is null
        ? "未设置游戏路径，请先在首页「游戏路径」中选择游戏目录"
        : "暂无数据";

    partial void OnSelectedPathChanged(string? value) => _ = Detail.LoadAsync(value, () => SelectedPath);

    /// <summary>扫描角色卡目录（窗口加载/点刷新）。对应原版 getAllFiles(charaFemalePath, .png)</summary>
    public void LoadCardPaths()
    {
        var dir = _config.GetCharaDir();
        AllPaths = dir is null
            ? new List<string>()
            : _scanner.ScanDirectory(dir, new ScanOptions { TargetExtension = { ".png" } });
        OnPropertyChanged(nameof(EmptyText));
    }
}
