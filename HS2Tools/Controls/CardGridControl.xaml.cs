using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using HS2Tools.Models;
using HS2Tools.Services;

namespace HS2Tools.Controls;

/// <summary>卡片种类（决定默认卡片尺寸/缩略图比例：角色 2:3、场景 16:9）</summary>
public enum CardKind
{
    Chara,
    Scene,
}

/// <summary>
/// 卡片网格控件（对应原版 CardGrid.tsx）。
/// 数据流对齐原版：全量路径（ItemsSource）→ 搜索过滤/排序 → 按批解析可视增量
/// （每批 24，ScannerService.ReadPngPageDataBatchAsync 一次读盘拿名称+缩略图），
/// 滚动接近底部（200px 阈值）自动加载下一批；缩略图 LRU 缓存控内存。
/// 收藏读写统一配置（ConfigService.Settings.Current.Favorites，按游戏隔离）。
/// </summary>
public partial class CardGridControl : UserControl
{
    /// <summary>每批解析数量（对应原版 PAGE_SIZE）</summary>
    public const int PageSize = 24;

    /// <summary>距底部多少像素内触发加载下一批（对应原版 threshold）</summary>
    private const double LoadMoreThreshold = 200;

    private readonly ObservableCollection<CardItemViewModel> _cards = new();
    private readonly HashSet<string> _loadedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ThumbnailCache _thumbnailCache = new();
    private List<string> _sorted = new();

    private ConfigService? _config;
    private ScannerService? _scanner;
    private ScrollViewer? _scroller;
    private CancellationTokenSource? _loadCts;
    private bool _isLoadingMore;

    public CardGridControl()
    {
        InitializeComponent();
        GridItems.ItemsSource = _cards;
        Loaded += OnLoaded;
    }

    /// <summary>选中卡片变化（参数为新选中路径，可为 null）</summary>
    public event EventHandler<string?>? SelectionChanged;

    // ==================== 依赖属性 ====================

    /// <summary>全量卡片路径（已扫描；控件内部负责过滤/排序/分批解析）</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IReadOnlyList<string>), typeof(CardGridControl),
        new PropertyMetadata(null, (d, _) => ((CardGridControl)d).ApplyFilterSortAndReset()));

    public IReadOnlyList<string>? ItemsSource
    {
        get => (IReadOnlyList<string>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
        nameof(SearchText), typeof(string), typeof(CardGridControl),
        new PropertyMetadata("", (d, _) => ((CardGridControl)d).ApplyFilterSortAndReset()));

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public static readonly DependencyProperty SortTypeProperty = DependencyProperty.Register(
        nameof(SortType), typeof(CardSortType), typeof(CardGridControl),
        new PropertyMetadata(CardSortType.Favorite, (d, _) => ((CardGridControl)d).ApplyFilterSortAndReset()));

    public CardSortType SortType
    {
        get => (CardSortType)GetValue(SortTypeProperty);
        set => SetValue(SortTypeProperty, value);
    }

    public static readonly DependencyProperty CardKindProperty = DependencyProperty.Register(
        nameof(CardKind), typeof(CardKind), typeof(CardGridControl),
        new PropertyMetadata(CardKind.Chara, (d, _) => ((CardGridControl)d).ApplyCardKindDefaults()));

    public CardKind CardKind
    {
        get => (CardKind)GetValue(CardKindProperty);
        set => SetValue(CardKindProperty, value);
    }

    /// <summary>卡片槽位宽（面板按固定尺寸布局）</summary>
    public static readonly DependencyProperty CardItemWidthProperty = DependencyProperty.Register(
        nameof(CardItemWidth), typeof(double), typeof(CardGridControl), new PropertyMetadata(168.0));

    public double CardItemWidth
    {
        get => (double)GetValue(CardItemWidthProperty);
        set => SetValue(CardItemWidthProperty, value);
    }

    /// <summary>卡片槽位高</summary>
    public static readonly DependencyProperty CardItemHeightProperty = DependencyProperty.Register(
        nameof(CardItemHeight), typeof(double), typeof(CardGridControl), new PropertyMetadata(304.0));

    public double CardItemHeight
    {
        get => (double)GetValue(CardItemHeightProperty);
        set => SetValue(CardItemHeightProperty, value);
    }

    /// <summary>选中路径（双向，供详情面板联动）</summary>
    public static readonly DependencyProperty SelectedPathProperty = DependencyProperty.Register(
        nameof(SelectedPath), typeof(string), typeof(CardGridControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((CardGridControl)d).OnSelectedPathChanged()));

    public string? SelectedPath
    {
        get => (string?)GetValue(SelectedPathProperty);
        set => SetValue(SelectedPathProperty, value);
    }

    /// <summary>空态文案（调用方可按场景给引导语，默认"暂无数据"）</summary>
    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(CardGridControl), new PropertyMetadata("暂无数据"));

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    /// <summary>过滤后的总数（只读，供工具栏"共 N 个"显示）</summary>
    private static readonly DependencyPropertyKey TotalCountPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(TotalCount), typeof(int), typeof(CardGridControl), new PropertyMetadata(0));

    public static readonly DependencyProperty TotalCountProperty = TotalCountPropertyKey.DependencyProperty;

    public int TotalCount
    {
        get => (int)GetValue(TotalCountProperty);
        private set => SetValue(TotalCountPropertyKey, value);
    }

    /// <summary>是否正在加载批次（只读，供工具栏刷新按钮 loading 显示）</summary>
    private static readonly DependencyPropertyKey IsLoadingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsLoading), typeof(bool), typeof(CardGridControl), new PropertyMetadata(false));

    public static readonly DependencyProperty IsLoadingProperty = IsLoadingPropertyKey.DependencyProperty;

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        private set => SetValue(IsLoadingPropertyKey, value);
    }

    // ==================== 初始化与刷新 ====================

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_config is null)
        {
            // 测试环境无 App：保持空数据态
            if (App.Services is { } services)
            {
                _config = services.Config;
                _scanner = services.Scanner;
                _config.Changed += OnConfigChanged;
            }
        }

        if (_scroller is null)
        {
            GridItems.ApplyTemplate();
            _scroller = GridItems.Template.FindName("Scroller", GridItems) as ScrollViewer;
            if (_scroller is not null)
                _scroller.ScrollChanged += OnScrollChanged;
        }

        ApplyFilterSortAndReset();
    }

    /// <summary>刷新（对应原版 CardGridRef.reload：清空已加载并重新过滤/排序/分批解析）</summary>
    public void Reload() => ApplyFilterSortAndReset();

    private void ApplyCardKindDefaults()
    {
        if (CardKind == CardKind.Chara)
        {
            CardItemWidth = 168;
            CardItemHeight = 304; // 名称栏 24 + 缩略图 2:3
        }
        else
        {
            CardItemWidth = 264;
            CardItemHeight = 200; // 名称栏 24 + 缩略图 16:9
        }
    }

    private void ApplyFilterSortAndReset()
    {
        _loadCts?.Cancel();
        _sorted = CardSortHelper.FilterAndSort(
            ItemsSource ?? (IReadOnlyList<string>)Array.Empty<string>(),
            SearchText, SortType, _config?.Settings.Current.Favorites);
        _loadedPaths.Clear();
        _cards.Clear();
        _thumbnailCache.Clear();
        TotalCount = _sorted.Count;
        _scroller?.ScrollToTop();
        UpdateFooter();
        _ = LoadNextBatchAsync();
    }

    // ==================== 分批加载 ====================

    private async Task LoadNextBatchAsync()
    {
        if (_scanner is null || _isLoadingMore)
            return;

        var batch = _sorted.Where(p => !_loadedPaths.Contains(p)).Take(PageSize).ToList();
        if (batch.Count == 0)
            return;

        _isLoadingMore = true;
        IsLoading = true;
        UpdateFooter();

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            var results = await _scanner.ReadPngPageDataBatchAsync(
                batch, concurrency: 4, msg => App.LogException(new Exception(msg)), ct);
            var byPath = results.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);

            foreach (var path in batch) // 按排序顺序追加
            {
                if (ct.IsCancellationRequested)
                    return;
                if (!_loadedPaths.Add(path))
                    continue;
                byPath.TryGetValue(path, out var data);
                _cards.Add(new CardItemViewModel
                {
                    Path = path,
                    // 原版空名显示空白标题；C# 版回退文件名（解析失败的卡不至于无标识）
                    DisplayName = data?.Names.FirstOrDefault() ?? CardSortHelper.FileNameKey(path),
                    Thumbnail = ResolveThumbnail(path, data?.ImageData),
                    IsFavorite = IsFavoritePath(path),
                    IsSelected = string.Equals(path, SelectedPath, StringComparison.OrdinalIgnoreCase),
                });
            }
        }
        catch (OperationCanceledException)
        {
            // 重置/卸载导致的取消
        }
        finally
        {
            _isLoadingMore = false;
            IsLoading = false;
            UpdateFooter();
            // reset（ApplyFilterSortAndReset）取消在飞批次时，新一批加载会被 _isLoadingMore 挡掉，
            // 这里在 finally 末尾补一次接力触发，避免过滤/刷新后列表停在空态
            if (ct.IsCancellationRequested && _cards.Count < _sorted.Count)
                _ = LoadNextBatchAsync();
        }
    }

    private System.Windows.Media.Imaging.BitmapImage? ResolveThumbnail(string path, string? base64)
    {
        var cached = _thumbnailCache.Get(path);
        if (cached is not null)
            return cached;
        var image = ThumbnailCache.DecodeBase64(base64);
        if (image is not null)
            _thumbnailCache.Set(path, image);
        return image;
    }

    private void UpdateFooter()
    {
        if (_isLoadingMore)
            FooterText.Text = $"加载中... ({_cards.Count}/{_sorted.Count})";
        else if (_sorted.Count > 0 && _cards.Count >= _sorted.Count)
            FooterText.Text = $"已加载全部 {_sorted.Count} 个";
        else
            FooterText.Text = "";
    }

    // ==================== 滚动到底加载 ====================

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeight > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - LoadMoreThreshold)
            _ = LoadNextBatchAsync();
    }

    // ==================== 选择与收藏 ====================

    private void Card_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CardItemViewModel card })
            SelectedPath = card.Path; // DP 回调里同步各卡片高亮并触发 SelectionChanged
    }

    private void OnSelectedPathChanged()
    {
        foreach (var card in _cards)
            card.IsSelected = string.Equals(card.Path, SelectedPath, StringComparison.OrdinalIgnoreCase);
        SelectionChanged?.Invoke(this, SelectedPath);
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // 不触发卡片选中
        if (_config is null || sender is not FrameworkElement { DataContext: CardItemViewModel card })
            return;
        _config.Update(s => s.Current.Favorites = CardSortHelper.ToggleFavorite(s.Current.Favorites, card.Path));
        // 星标刷新与（收藏优先模式下的）重排在 OnConfigChanged 中处理
    }

    private bool IsFavoritePath(string path) =>
        _config?.Settings.Current.Favorites.Any(f => CardSortHelper.NormalizePath(f) == CardSortHelper.NormalizePath(path)) == true;

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        foreach (var card in _cards)
            card.IsFavorite = IsFavoritePath(card.Path);

        // 收藏优先模式：收藏变化即时重排（对应原版 favorites 依赖重算 sortedPaths）。
        // 已加载卡片原地重排保持滚动位置；未加载的交给后续批次。
        if (SortType == CardSortType.Favorite)
        {
            _sorted = CardSortHelper.FilterAndSort(
                ItemsSource ?? (IReadOnlyList<string>)Array.Empty<string>(),
                SearchText, SortType, _config?.Settings.Current.Favorites);

            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _sorted.Count; i++)
                order[_sorted[i]] = i;

            var reordered = _cards
                .OrderBy(c => order.TryGetValue(c.Path, out var idx) ? idx : int.MaxValue)
                .ToList();
            for (var i = 0; i < reordered.Count; i++)
            {
                var oldIndex = _cards.IndexOf(reordered[i]);
                if (oldIndex != i)
                    _cards.Move(oldIndex, i);
            }
        }
    }
}
