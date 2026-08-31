using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.ViewModels;

/// <summary>本地模组列表行项（GUID / 名称 / 版本 / 使用次数 / 路径）</summary>
public partial class ModItemViewModel : ObservableObject
{
    public required string Guid { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Path { get; init; }

    /// <summary>使用次数（与 ModUsage 大小写不敏感匹配）</summary>
    public required int UsedCount { get; init; }
}

/// <summary>
/// 本地模组窗口 ViewModel（对应原版 Mods/LocalMods.tsx）：
/// 统计三卡（本地 Mods 总数 / 被引用 Mods 数 / 总引用次数，与首页数据概览同口径）、
/// 所有/未使用筛选、刷新重扫 mods 目录 zipmod。
/// </summary>
public partial class ModsWindowViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly ScannerService _scanner;
    private List<ModItemViewModel> _all = new();

    public ModsWindowViewModel(ConfigService config, ScannerService scanner)
    {
        _config = config;
        _scanner = scanner;

        Reload();
        Mods.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        // 其他窗口完成分析/修改配置后列表自行刷新（单例服务与窗口同寿，无需退订）
        _config.Changed += (_, _) => UiDispatch.Run(Reload);
    }

    /// <summary>筛选后的展示行（DataGrid 负责点击列排序/虚拟化）</summary>
    public ObservableCollection<ModItemViewModel> Mods { get; } = new();

    [ObservableProperty] private int _modCount;
    [ObservableProperty] private int _usageCount;
    [ObservableProperty] private int _totalRefs;

    /// <summary>只显示未使用（使用次数为 0）的 Mods（对应原版 Radio 筛选 value="2"）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _showUnusedOnly;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(RefreshButtonText))]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _isRefreshing;

    public string RefreshButtonText => IsRefreshing ? "扫描中..." : "刷新模组列表";

    public bool IsEmpty => Mods.Count == 0;

    public string EmptyText => IsRefreshing
        ? "正在扫描本地 Mods..."
        : ModCount == 0
            ? "暂无本地 Mods，请先设置游戏目录并点击「刷新模组列表」"
            : "没有未使用的 Mods";

    partial void OnShowUnusedOnlyChanged(bool value) => ApplyFilter();

    /// <summary>从 Config.Settings 重建主列表与统计（对应原版 useMemo(mods/useage)）</summary>
    private void Reload()
    {
        var settings = _config.Settings.Current;

        // 原版 createUsageMap：usage key 转小写建 Map，查找时 guid 也转小写 → 整体大小写不敏感；
        // 大小写重复时后者覆盖（与 JS Map.set 一致），故用 OrdinalIgnoreCase 字典手工填充
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (guid, count) in settings.ModUsage)
            usage[guid] = count;

        _all = settings.LocalMods
            .Select(kv => new ModItemViewModel
            {
                Guid = kv.Key,
                Name = kv.Value.Name,
                Version = kv.Value.Version,
                Path = kv.Value.Path,
                UsedCount = usage.TryGetValue(kv.Key, out var c) ? c : 0, // 原版 getUsageCount：未命中为 0
            })
            // 原版排序：guid → version → path（localeCompare）
            .OrderBy(m => m.Guid, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 统计口径与首页数据概览一致（Config.Settings 直取）
        ModCount = settings.LocalMods.Count;
        UsageCount = settings.ModUsage.Count;
        TotalRefs = settings.ModUsage.Values.Sum();

        ApplyFilter();
        OnPropertyChanged(nameof(EmptyText));
    }

    private void ApplyFilter()
    {
        Mods.Clear();
        foreach (var item in _all)
        {
            if (!ShowUnusedOnly || item.UsedCount == 0)
                Mods.Add(item);
        }
    }

    private bool CanRefresh() => !IsRefreshing;

    /// <summary>
    /// 刷新（对应原版 scanMods）：ScanDirectory(.zipmod) + ReadZipModBatchAsync，
    /// 结果写回 Config.Settings.Current.LocalMods（Changed 事件驱动 Reload）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            var mods = new Dictionary<string, ModInfo>();
            var dir = _config.GetModsDir();
            if (dir is not null)
            {
                var files = _scanner.ScanDirectory(dir, new ScanOptions { TargetExtension = { ".zipmod" } });
                mods = await _scanner.ReadZipModBatchAsync(files, onError: LogScanError);
            }
            _config.Update(s => s.Current.LocalMods = mods);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private static void LogScanError(string message) => App.LogException(new Exception(message));
}
