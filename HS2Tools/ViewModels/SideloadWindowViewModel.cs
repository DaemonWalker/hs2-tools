using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.ViewModels;

/// <summary>Sideload 数据库列表行项（状态随 DownloadManager 事件实时刷新，参考 DetailModItem 状态机）</summary>
public partial class SideloadItemViewModel : ObservableObject
{
    /// <summary>Mod GUID（下载任务 Id 与之一致）</summary>
    public required string Guid { get; init; }

    /// <summary>下载地址（相对路径）</summary>
    public required string Url { get; init; }

    /// <summary>本地已存在（Config.Settings.LocalMods 中存在）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    private bool _existsLocally;

    /// <summary>下载任务状态（无任务为 null）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(DownloadText))]
    private DownloadTaskStatus? _taskStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadText))]
    private double _percent;

    public bool IsDownloading => TaskStatus == DownloadTaskStatus.Downloading;
    public bool IsCompleted => TaskStatus == DownloadTaskStatus.Completed;

    /// <summary>状态列：已存在 / 已下载（本次下载完成，待重扫入 LocalMods）/ 缺失</summary>
    public string StatusText => ExistsLocally ? "已存在" : IsCompleted ? "已下载" : "缺失";

    /// <summary>下载中或已完成禁用按钮（对应原版 downloadingGuids 防重复点击）</summary>
    public bool CanDownload => !ExistsLocally && !IsDownloading && !IsCompleted;

    public string DownloadText => IsDownloading ? $"{Percent:0}%" : IsCompleted ? "已完成" : "下载";
}

/// <summary>
/// BetterRepack 数据库浏览窗口 ViewModel（对应原版 BetterRepack.tsx）：
/// 统计三卡（总数/已存在/缺失）、300ms 防抖搜索（GUID/URL）、单条下载。
/// </summary>
public partial class SideloadWindowViewModel : ObservableObject
{
    /// <summary>默认搜索防抖间隔（对应原版 300ms setTimeout）</summary>
    internal const int DefaultDebounceMs = 300;

    private readonly ConfigService _config;
    private readonly DownloadManager _downloads;
    private readonly SideloadDatabaseService _sideloadDb;
    private List<SideloadItemViewModel> _all = new();
    private int _searchVersion;

    public SideloadWindowViewModel(
        ConfigService config,
        DownloadManager downloads,
        SideloadDatabaseService sideloadDb)
    {
        _config = config;
        _downloads = downloads;
        _sideloadDb = sideloadDb;

        Reload();
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));

        // 下载进度/完成 → 行状态刷新；库更新/配置（LocalMods）变化 → 整体重建
        // （单例服务与窗口同寿，无需退订）
        _downloads.TaskAdded += (_, t) => UiDispatch.Run(() => UpdateItemStatus(t)); // 他窗发起的下载即时反映
        _downloads.TaskProgress += (_, t) => UiDispatch.Run(() => UpdateItemStatus(t));
        _downloads.TaskFinished += (_, t) => UiDispatch.Run(() => UpdateItemStatus(t));
        _sideloadDb.Changed += (_, _) => UiDispatch.Run(Reload);
        _config.Changed += (_, _) => UiDispatch.Run(Reload);
    }

    /// <summary>防抖间隔（测试可缩短；默认 <see cref="DefaultDebounceMs"/>）</summary>
    internal int DebounceMs { get; set; } = DefaultDebounceMs;

    /// <summary>搜索过滤后的展示行（DataGrid 负责点击列排序/虚拟化）</summary>
    public ObservableCollection<SideloadItemViewModel> Items { get; } = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _existingCount;
    [ObservableProperty] private int _missingCount;

    public bool IsEmpty => Items.Count == 0;

    public string EmptyText => TotalCount == 0
        ? "暂无 Sideload 数据，请前往首页点击「更新 Sideload 数据」"
        : "没有匹配的记录";

    partial void OnSearchTextChanged(string value) => _ = DebounceFilterAsync(value);

    /// <summary>
    /// 300ms 防抖（对应原版 clearTimeout + setTimeout）：版本号失效即丢弃。
    /// Task.Delay 无定时器对象需要释放，窗口隐藏/关闭后至多再应用一次过滤，不泄漏。
    /// </summary>
    private async Task DebounceFilterAsync(string value)
    {
        var version = ++_searchVersion;
        await Task.Delay(DebounceMs);
        if (version != _searchVersion)
            return;
        ApplyFilter(value);
    }

    /// <summary>过滤纯函数：GUID 或 URL 子串匹配、大小写不敏感（对应原版 toLowerCase().includes）</summary>
    internal static bool Matches(string guid, string url, string search) =>
        guid.Contains(search, StringComparison.OrdinalIgnoreCase)
        || url.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>应用过滤（防抖到期或数据重建时调用；测试可直接调用跳过防抖）</summary>
    internal void ApplyFilter(string search)
    {
        Items.Clear();
        foreach (var item in _all)
        {
            if (search == "" || Matches(item.Guid, item.Url, search))
                Items.Add(item);
        }
    }

    /// <summary>从 SideloadDb + LocalMods 重建主列表与统计（统计不受搜索词影响，与原版一致）</summary>
    private void Reload()
    {
        var db = _sideloadDb.Database;
        var localMods = _config.Settings.LocalMods;

        _all = db.Select(kv =>
        {
            var task = FindTask(kv.Key);
            return new SideloadItemViewModel
            {
                Guid = kv.Key,
                Url = kv.Value,
                ExistsLocally = localMods.ContainsKey(kv.Key),
                TaskStatus = task?.Status,
                Percent = task?.Percent ?? 0,
            };
        }).ToList();

        TotalCount = db.Count;
        ExistingCount = _all.Count(i => i.ExistsLocally);
        MissingCount = TotalCount - ExistingCount;

        ApplyFilter(SearchText);
        OnPropertyChanged(nameof(EmptyText));
    }

    private DownloadTask? FindTask(string guid) => _downloads.Tasks.FirstOrDefault(t => t.Id == guid);

    private void UpdateItemStatus(DownloadTask task)
    {
        var item = _all.FirstOrDefault(i => i.Guid == task.Id);
        if (item is null)
            return;
        item.TaskStatus = task.Status;
        item.Percent = task.Percent;
    }

    /// <summary>单条下载（对应原版 handleDownload：triggerDownload({ name: guid, url })）</summary>
    [RelayCommand]
    private void Download(SideloadItemViewModel item)
    {
        var dir = _config.GetModDownloadDir();
        if (dir is null)
            return;
        if (_downloads.StartDownload(item.Guid, item.Url, dir))
            UpdateItemStatus(FindTask(item.Guid)!); // 即时刷新按钮态（事件随后也会到）
    }
}
