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

    /// <summary>已按其扫描的游戏 ID（Changed 时与 Settings.CurrentGame 比较，识别"游戏被切换"）</summary>
    private string _loadedGameId;

    public CharaWindowViewModel(
        ConfigService config,
        ScannerService scanner,
        DownloadManager downloads,
        SideloadDatabaseService sideloadDb,
        GameLauncherService launcher)
    {
        _config = config;
        _scanner = scanner;
        _loadedGameId = config.Settings.CurrentGame;
        Detail = new CardDetailViewModel(config, scanner, downloads, sideloadDb, launcher);
        // 切换游戏后用新游戏目录重扫（只认 CurrentGame 变化；收藏等其它 Changed 不重扫）
        _config.Changed += (_, _) => UiDispatch.Run(ReloadIfGameChanged);
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

    /// <summary>CurrentGame 变化（别处切换游戏）时按新游戏目录重扫；收藏等其它配置变化不触发</summary>
    private void ReloadIfGameChanged()
    {
        if (_config.Settings.CurrentGame == _loadedGameId)
            return;
        SelectedPath = null; // 旧游戏的选中卡片与详情随之清空
        LoadCardPaths();
    }

    /// <summary>扫描角色卡目录（窗口加载/点刷新/游戏切换）。对应原版 getAllFiles(charaFemalePath, .png)</summary>
    public void LoadCardPaths()
    {
        _loadedGameId = _config.Settings.CurrentGame;
        var dir = _config.GetCharaDir();
        AllPaths = dir is null
            ? new List<string>()
            : _scanner.ScanDirectory(dir, new ScanOptions { TargetExtension = { ".png" } });
        OnPropertyChanged(nameof(EmptyText));
    }
}
