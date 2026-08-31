using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.ViewModels;

/// <summary>下载任务列表行项（字段随 DownloadManager 事件实时刷新）</summary>
public partial class DownloadTaskItemViewModel : ObservableObject
{
    /// <summary>任务标识（Mod 名）</summary>
    public required string Id { get; init; }

    /// <summary>任务创建时间（排序用，对应原版 startTime）</summary>
    public required DateTime CreatedAt { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(ShowCancel))]
    [NotifyPropertyChangedFor(nameof(ShowRetry))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private DownloadTaskStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private long _downloaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private long _total = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double _speed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double _percent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _errorMessage;

    public bool IsDownloading => Status == DownloadTaskStatus.Downloading;
    public bool IsFailed => Status == DownloadTaskStatus.Failed;

    /// <summary>原版操作按钮状态机：下载中 → 取消；失败/已取消 → 重试；已完成 → 无操作</summary>
    public bool ShowCancel => IsDownloading;
    public bool ShowRetry => Status is DownloadTaskStatus.Failed or DownloadTaskStatus.Cancelled;

    /// <summary>状态文本（对应原版 TaskItem statusText）</summary>
    public string StatusText => Status switch
    {
        DownloadTaskStatus.Downloading => DownloadingText(),
        DownloadTaskStatus.Completed => Total > 0 ? FormatUtils.FormatBytes(Total) : "完成",
        DownloadTaskStatus.Failed => string.IsNullOrEmpty(ErrorMessage) ? "下载失败" : ErrorMessage!,
        DownloadTaskStatus.Cancelled => "已取消",
        _ => "等待中",
    };

    /// <summary>下载中：已下载/总量 · 百分比 · 速度（· 剩余时间）；Total 未知（-1）时只显示已下载（照原版）</summary>
    private string DownloadingText()
    {
        var sizeText = Total > 0
            ? $"{FormatUtils.FormatBytes(Downloaded)} / {FormatUtils.FormatBytes(Total)}"
            : FormatUtils.FormatBytes(Downloaded);
        var text = $"{sizeText} · {Percent:0}% · {FormatUtils.FormatSpeed(Speed)}";
        var remaining = FormatUtils.EstimateRemainingTime(Downloaded, Total, Speed);
        if (remaining >= 0)
            text += $" · {FormatUtils.FormatTime(remaining)}";
        return text;
    }

    /// <summary>从任务同步全部字段</summary>
    public void Update(DownloadTask task)
    {
        Status = task.Status;
        Downloaded = task.Downloaded;
        Total = task.Total;
        Speed = task.Speed;
        Percent = task.Percent;
        ErrorMessage = task.ErrorMessage;
    }

    public static DownloadTaskItemViewModel From(DownloadTask task)
    {
        var item = new DownloadTaskItemViewModel { Id = task.Id, CreatedAt = task.CreatedAt };
        item.Update(task);
        return item;
    }
}

/// <summary>
/// 下载任务管理窗口 ViewModel（对应原版 Download.tsx + useDownloadManager）：
/// 统计栏（活跃/失败/总速度）、全部取消/清除完成、四 Tab 过滤、取消/重试。
/// 数据源为 DownloadManager 单例：关闭窗口不中断下载，事件订阅一次（VM 与窗口单例同寿）。
/// </summary>
public partial class DownloadWindowViewModel : ObservableObject
{
    private readonly DownloadManager _downloads;
    private readonly List<DownloadTaskItemViewModel> _all = new();

    public DownloadWindowViewModel(DownloadManager downloads)
    {
        _downloads = downloads;

        RebuildFromTasks(); // 首次打开时从任务表重建（此前可能已有进行中的下载）
        AllTasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsAllEmpty));
        ActiveTasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsActiveEmpty));
        CompletedTasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsCompletedEmpty));
        FailedTasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsFailedEmpty));

        // 任务事件 → UI 线程刷新（单例服务与窗口同寿，无需退订）
        _downloads.TaskAdded += (_, t) => UiDispatch.Run(() => OnTaskAdded(t));
        _downloads.TaskProgress += (_, t) => UiDispatch.Run(() => OnTaskChanged(t));
        _downloads.TaskFinished += (_, t) => UiDispatch.Run(() => OnTaskChanged(t));
    }

    /// <summary>全部任务（下载中优先，同状态按开始时间倒序）</summary>
    public ObservableCollection<DownloadTaskItemViewModel> AllTasks { get; } = new();

    /// <summary>下载中</summary>
    public ObservableCollection<DownloadTaskItemViewModel> ActiveTasks { get; } = new();

    /// <summary>已完成</summary>
    public ObservableCollection<DownloadTaskItemViewModel> CompletedTasks { get; } = new();

    /// <summary>失败（原版 getFailedTasks 仅含 error 状态，不含已取消——已取消只在「全部」Tab 出现）</summary>
    public ObservableCollection<DownloadTaskItemViewModel> FailedTasks { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCancelAll))]
    [NotifyPropertyChangedFor(nameof(ShowClearFinished))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCancelAll))]
    [NotifyPropertyChangedFor(nameof(ShowClearFinished))]
    private int _activeCount;

    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _failedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSpeedText))]
    private double _totalSpeed;

    /// <summary>总速度（活跃任务 Speed 求和，FormatSpeed）</summary>
    public string TotalSpeedText => FormatUtils.FormatSpeed(TotalSpeed);

    /// <summary>有活跃任务时显示「全部取消」（对应原版 activeCount > 0）</summary>
    public bool ShowCancelAll => ActiveCount > 0;

    /// <summary>
    /// 有可清除任务（非下载中）时显示「清除完成」。
    /// 原版 hasClearable 的 cancelled 检查是死代码（failedTasks 恒为 error 状态），
    /// 这里按意图实现：存在任一非下载中任务即可清除，与 ClearFinished（清除全部非下载中）自洽。
    /// </summary>
    public bool ShowClearFinished => TotalCount > ActiveCount;

    public bool IsAllEmpty => AllTasks.Count == 0;
    public bool IsActiveEmpty => ActiveTasks.Count == 0;
    public bool IsCompletedEmpty => CompletedTasks.Count == 0;
    public bool IsFailedEmpty => FailedTasks.Count == 0;

    /// <summary>排序：下载中优先，然后按开始时间倒序（对应原版 TaskList sortedTasks）</summary>
    internal static int CompareItems(DownloadTaskItemViewModel a, DownloadTaskItemViewModel b)
    {
        var aDownloading = a.Status == DownloadTaskStatus.Downloading;
        var bDownloading = b.Status == DownloadTaskStatus.Downloading;
        if (aDownloading != bDownloading)
            return aDownloading ? -1 : 1;
        return b.CreatedAt.CompareTo(a.CreatedAt);
    }

    private void OnTaskAdded(DownloadTask task)
    {
        // 终态同名任务重新 StartDownload 会再发 TaskAdded：先移除同 Id 旧行，避免双行
        _all.RemoveAll(i => i.Id == task.Id);
        _all.Add(DownloadTaskItemViewModel.From(task));
        _all.Sort(CompareItems);
        RefillViews();
        RefreshStats();
    }

    private void OnTaskChanged(DownloadTask task)
    {
        var item = _all.FirstOrDefault(i => i.Id == task.Id);
        if (item is null)
            return;
        var statusChanged = item.Status != task.Status;
        item.Update(task);
        if (statusChanged)
        {
            // 状态迁移（含 Retry 回到下载中）影响排序与 Tab 归属
            _all.Sort(CompareItems);
            RefillViews();
        }
        RefreshStats();
    }

    /// <summary>按当前 _all 重建四个 Tab 视图（进度刷新不动集合，避免 UI 抖动）</summary>
    private void RefillViews()
    {
        Refill(AllTasks, _all);
        Refill(ActiveTasks, _all.Where(i => i.Status == DownloadTaskStatus.Downloading));
        Refill(CompletedTasks, _all.Where(i => i.Status == DownloadTaskStatus.Completed));
        Refill(FailedTasks, _all.Where(i => i.Status == DownloadTaskStatus.Failed));
    }

    private static void Refill(ObservableCollection<DownloadTaskItemViewModel> target, IEnumerable<DownloadTaskItemViewModel> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    private void RefreshStats()
    {
        TotalCount = _all.Count;
        ActiveCount = _all.Count(i => i.Status == DownloadTaskStatus.Downloading);
        CompletedCount = _all.Count(i => i.Status == DownloadTaskStatus.Completed);
        FailedCount = _all.Count(i => i.Status == DownloadTaskStatus.Failed);
        TotalSpeed = _all.Where(i => i.Status == DownloadTaskStatus.Downloading).Sum(i => i.Speed);
    }

    /// <summary>从 DownloadManager 任务表全量重建（首次打开 / 清除完成后）</summary>
    private void RebuildFromTasks()
    {
        _all.Clear();
        foreach (var task in _downloads.Tasks)
            _all.Add(DownloadTaskItemViewModel.From(task));
        _all.Sort(CompareItems);
        RefillViews();
        RefreshStats();
    }

    /// <summary>取消下载（状态迁移由 TaskFinished 事件驱动）</summary>
    [RelayCommand]
    private void Cancel(DownloadTaskItemViewModel item) => _downloads.Cancel(item.Id);

    /// <summary>重试（断点续传；DownloadManager 会先重置字段并发 TaskProgress）</summary>
    [RelayCommand]
    private void Retry(DownloadTaskItemViewModel item) => _downloads.Retry(item.Id);

    [RelayCommand]
    private void CancelAll() => _downloads.CancelAll();

    /// <summary>清除所有非下载中任务（ClearFinished 无事件，需手动重建视图）</summary>
    [RelayCommand]
    private void ClearFinished()
    {
        _downloads.ClearFinished();
        RebuildFromTasks();
    }
}
