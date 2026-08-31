using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Controls;
using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.ViewModels;

/// <summary>详情面板中一个 Mod 依赖项（状态随 DownloadManager 事件实时刷新）</summary>
public partial class DetailModItem : ObservableObject
{
    /// <summary>Mod GUID（任务 Id 与之一致）</summary>
    public required string Guid { get; init; }

    /// <summary>本地已拥有（扫描结果 LocalMods 中存在）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOwned))]
    [NotifyPropertyChangedFor(nameof(ShowDownload))]
    [NotifyPropertyChangedFor(nameof(ShowUnavailable))]
    private bool _isLocal;

    /// <summary>sideload 库中有下载链接</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownload))]
    [NotifyPropertyChangedFor(nameof(ShowUnavailable))]
    private bool _hasUrl;

    /// <summary>下载任务状态（无任务为 null）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(ShowCompleted))]
    [NotifyPropertyChangedFor(nameof(ShowOwned))]
    [NotifyPropertyChangedFor(nameof(ShowDownload))]
    [NotifyPropertyChangedFor(nameof(ShowUnavailable))]
    private DownloadTaskStatus? _status;

    [ObservableProperty]
    private double _percent;

    /// <summary>原版 DownloadButton 状态机：下载中 → 进度+取消；已完成 → 已完成；已拥有 → 已拥有；无链接 → 禁用；否则 → 下载</summary>
    public bool IsDownloading => Status == DownloadTaskStatus.Downloading;
    public bool ShowCompleted => Status == DownloadTaskStatus.Completed;
    public bool ShowOwned => !IsDownloading && !ShowCompleted && IsLocal;
    public bool ShowDownload => !IsDownloading && !ShowCompleted && !IsLocal && HasUrl;
    public bool ShowUnavailable => !IsDownloading && !ShowCompleted && !IsLocal && !HasUrl;
}

/// <summary>
/// 卡片详情面板 ViewModel（角色/场景共用）：
/// 名称/描述、真实修改时间与大小（A6）、Mod 依赖列表 + 单项下载 + 一键下载缺失（A4）、打开所在文件夹。
/// </summary>
public partial class CardDetailViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly ScannerService _scanner;
    private readonly DownloadManager _downloads;
    private readonly SideloadDatabaseService _sideloadDb;
    private readonly GameLauncherService _launcher;

    public CardDetailViewModel(
        ConfigService config,
        ScannerService scanner,
        DownloadManager downloads,
        SideloadDatabaseService sideloadDb,
        GameLauncherService launcher)
    {
        _config = config;
        _scanner = scanner;
        _downloads = downloads;
        _sideloadDb = sideloadDb;
        _launcher = launcher;

        // 下载进度/完成 → 详情列表对应项状态刷新（单例服务与窗口同寿，无需退订）
        _downloads.TaskProgress += (_, t) => UiDispatch.Run(() => UpdateItemStatus(t));
        _downloads.TaskFinished += (_, t) => UiDispatch.Run(() => UpdateItemStatus(t));
    }

    [ObservableProperty] private string _detailName = "";
    [ObservableProperty] private string? _detailDescription;
    [ObservableProperty] private BitmapImage? _detailImage;
    [ObservableProperty] private string _detailFilePath = "";
    [ObservableProperty] private string _detailModified = "";
    [ObservableProperty] private string _detailSize = "";
    [ObservableProperty] private int _localCount;
    [ObservableProperty] private int _missingCount;

    public ObservableCollection<DetailModItem> ModItems { get; } = new();

    /// <summary>当前详情加载任务（测试可等待；选择变化时总是替换为新任务）</summary>
    internal Task? LoadTask { get; private set; }

    /// <summary>加载详情（名称/Mod/大图/文件信息），IO 在后台线程。调用方负责在选择变化时触发</summary>
    public Task LoadAsync(string? path, Func<string?> currentSelection)
    {
        LoadTask = LoadCoreAsync(path, currentSelection);
        return LoadTask;
    }

    private async Task LoadCoreAsync(string? path, Func<string?> currentSelection)
    {
        if (path is null)
            return;

        try
        {
            DetailFilePath = path;

            var (names, mods, image) = await Task.Run(() =>
            {
                var n = _scanner.ReadPngNames(path);
                var m = _scanner.ReadPngMods(path);
                var img = ThumbnailCache.DecodeBase64(_scanner.ReadPngImage(path));
                return (n, m, img);
            });

            if (currentSelection() != path)
                return; // 加载期间已切换选择，丢弃过期结果

            DetailName = names.FirstOrDefault() ?? "未知";
            DetailDescription = names.Count > 1 ? names[1] : null;
            DetailImage = image;

            // A6：真实文件信息（原版为硬编码假数据）
            var fi = new FileInfo(path);
            DetailModified = fi.Exists ? fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm") : "-";
            DetailSize = fi.Exists ? FormatUtils.FormatBytes(fi.Length) : "-";

            ModItems.Clear();
            foreach (var guid in mods)
            {
                var task = FindTask(guid);
                ModItems.Add(new DetailModItem
                {
                    Guid = guid,
                    IsLocal = _config.Settings.Current.LocalMods.ContainsKey(guid),
                    HasUrl = _sideloadDb.Database.ContainsKey(guid),
                    Status = task?.Status,
                    Percent = task?.Percent ?? 0,
                });
            }
            RefreshCounts();
        }
        catch (Exception ex)
        {
            // 加载失败保持面板旧数据/空态，记日志（LoadTask 由调用方/测试观察，不得留下未观察的 faulted task）
            ErrorLog.Log(ex);
        }
    }

    private DownloadTask? FindTask(string guid) => _downloads.Tasks.FirstOrDefault(t => t.Id == guid);

    private void UpdateItemStatus(DownloadTask task)
    {
        var item = ModItems.FirstOrDefault(i => i.Guid == task.Id);
        if (item is null)
            return;
        item.Status = task.Status;
        item.Percent = task.Percent;
    }

    private void RefreshCounts()
    {
        LocalCount = ModItems.Count(i => i.IsLocal);
        MissingCount = ModItems.Count - LocalCount;
    }

    /// <summary>测试用：非 null 时覆盖下载 base URL（默认取当前游戏档案的 SideloadBaseUrl）</summary>
    internal string? DownloadBaseUrlOverride;

    /// <summary>单项下载（对应原版 DownloadButton onClick）</summary>
    [RelayCommand]
    private void DownloadMod(DetailModItem item)
    {
        var dir = _config.GetModDownloadDir();
        if (dir is null || !_sideloadDb.Database.TryGetValue(item.Guid, out var url))
            return;
        _downloads.StartDownload(item.Guid, url, dir,
            DownloadBaseUrlOverride ?? _config.CurrentProfile.SideloadBaseUrl);
        // StartDownload 后任务进入下载中：即时刷新按钮态（事件随后也会到）
        UpdateItemStatus(FindTask(item.Guid)!);
    }

    [RelayCommand]
    private void CancelMod(DetailModItem item) => _downloads.Cancel(item.Guid);

    /// <summary>一键下载缺失（A4：原版为无事件的占位按钮，C# 版做成真实功能）</summary>
    [RelayCommand]
    private void DownloadAllMissing()
    {
        foreach (var item in ModItems.Where(i => i.ShowDownload).ToList())
            DownloadMod(item);
    }

    /// <summary>打开所在文件夹（explorer /select,）</summary>
    [RelayCommand]
    private void OpenInFolder()
    {
        if (!string.IsNullOrEmpty(DetailFilePath))
            _launcher.OpenInFolder(DetailFilePath);
    }
}
