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

    private string _loadedGameId;
    private string _loadedGamePath;

    public CharaWindowViewModel(
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
        Detail = new CardDetailViewModel(config, scanner, downloads, sideloadDb, launcher, analysisCache, "chara");
        _config.Changed += (_, _) => UiDispatch.Run(ReloadIfGameContextChanged);
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

    private void ReloadIfGameContextChanged()
    {
        if (_config.Settings.CurrentGame == _loadedGameId
            && _config.Settings.Current.GamePath == _loadedGamePath)
            return;
        SelectedPath = null;
        LoadCardPaths();
    }

    /// <summary>扫描角色卡目录；路径未通过当前游戏 exe 校验时清空，避免误展示其他游戏卡片。</summary>
    public void LoadCardPaths()
    {
        _loadedGameId = _config.Settings.CurrentGame;
        _loadedGamePath = _config.Settings.Current.GamePath;
        var dir = _config.GetCharaDir();
        AllPaths = dir is null || !_config.IsCurrentGamePathValid
            ? new List<string>()
            : _scanner.ScanDirectory(dir, new ScanOptions { TargetExtension = { ".png" } });
        OnPropertyChanged(nameof(EmptyText));
    }
}
