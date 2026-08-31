using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HS2Tools.Controls;

namespace HS2Tools.Tests;

public class ThumbnailCacheTests
{
    [Fact]
    public void Get_Miss_ReturnsNull()
    {
        var cache = new ThumbnailCache(2);
        Assert.Null(cache.Get("a"));
    }

    [Fact]
    public void EvictsLeastRecentlyUsed_BeyondCapacity()
    {
        var cache = new ThumbnailCache(2);
        var img = NewFrozenImage();
        cache.Set("a", img);
        cache.Set("b", img);
        cache.Set("c", img); // a 被淘汰

        Assert.Equal(2, cache.Count);
        Assert.Null(cache.Get("a"));
        Assert.NotNull(cache.Get("b"));
        Assert.NotNull(cache.Get("c"));
    }

    [Fact]
    public void Get_RefreshesRecency()
    {
        var cache = new ThumbnailCache(2);
        var img = NewFrozenImage();
        cache.Set("a", img);
        cache.Set("b", img);
        _ = cache.Get("a"); // a 提到最前
        cache.Set("c", img); // b 被淘汰

        Assert.NotNull(cache.Get("a"));
        Assert.Null(cache.Get("b"));
    }

    [Fact]
    public void DecodeBase64_Invalid_ReturnsNull()
    {
        Assert.Null(ThumbnailCache.DecodeBase64(null));
        Assert.Null(ThumbnailCache.DecodeBase64(""));
        Assert.Null(ThumbnailCache.DecodeBase64("not-base64!!!"));
    }

    [Fact]
    public void DecodeBase64_RoundTrips_RealPng()
    {
        RunInSta(() =>
        {
            var png = MakeRealPngBase64();
            var image = ThumbnailCache.DecodeBase64(png);
            Assert.NotNull(image);
            Assert.Equal(2, image.PixelWidth);
            Assert.True(image.IsFrozen);
        });
    }

    /// <summary>合成真实 2x2 PNG 的 base64（WPF 编码器，无渲染环境也可工作）</summary>
    internal static string MakeRealPngBase64()
    {
        var bmp = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, 2, 2), new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255 }, 8, 0);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static BitmapImage NewFrozenImage()
    {
        BitmapImage? result = null;
        RunInSta(() => result = ThumbnailCache.DecodeBase64(MakeRealPngBase64()));
        return result!;
    }

    internal static void RunInSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }
}

/// <summary>VirtualizingWrapPanel 与 CardGridControl 的 STA 冒烟测试</summary>
public class CardGridControlTests
{
    [Fact]
    public void VirtualizingWrapPanel_RealizesOnlyVisibleRange()
    {
        ThumbnailCacheTests.RunInSta(() =>
        {
            var items = Enumerable.Range(0, 200).Select(i => $"item{i}").ToList();
            var factory = new FrameworkElementFactory(typeof(VirtualizingWrapPanel));
            factory.SetValue(VirtualizingWrapPanel.ItemWidthProperty, 100.0);
            factory.SetValue(VirtualizingWrapPanel.ItemHeightProperty, 100.0);
            var itemsControl = new ItemsControl
            {
                Width = 320,
                Height = 200,
                ItemsSource = items,
                ItemsPanel = new ItemsPanelTemplate(factory),
                // 无 Application 环境默认主题不可用，显式给模板
                Template = new ControlTemplate(typeof(ItemsControl))
                {
                    VisualTree = new FrameworkElementFactory(typeof(ItemsPresenter)),
                },
            };

            itemsControl.ApplyTemplate();
            itemsControl.Measure(new Size(320, 200));
            itemsControl.Arrange(new Rect(0, 0, 320, 200));
            itemsControl.UpdateLayout();

            var panel = FindDescendant<VirtualizingWrapPanel>(itemsControl);
            Assert.NotNull(panel);
            // 3 列 × (2 行可视 + 1 行缓冲) = 至多 9 个容器，远小于 200
            Assert.True(panel.RealizedCount is > 0 and <= 12,
                $"realized {panel.RealizedCount}");
        });
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit)
                return hit;
            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    [Fact]
    public void VirtualizingWrapPanel_ScrollOffset_ChangesRealizedSet()
    {
        ThumbnailCacheTests.RunInSta(() =>
        {
            var panel = new VirtualizingWrapPanel { ItemWidth = 100, ItemHeight = 100 };
            // 无 ItemsOwner 时 ItemCount=0：只验证滚动钳制不炸
            panel.Measure(new Size(300, 200));
            panel.SetVerticalOffset(10_000);
            Assert.Equal(0, panel.VerticalOffset); // 无内容 → 偏移钳到 0
        });
    }

    /// <summary>
    /// 回归：真实 ItemTemplate + CanContentScroll 滚动托管下的虚拟化布局。
    /// 旧 XAML 的 Border 样式写了 BasedOn="{StaticResource {x:Type Border}}"，
    /// 而主题字典无此资源（Border 无默认样式），模板应用时抛 XamlParseException，
    /// 布局中断——卡片行渲染在视口底部、上方大片空白（角色卡/场景卡同病）。
    /// </summary>
    [Fact]
    public void CardGrid_RealTemplate_Virtualizes_And_Scrolls()
    {
        ThumbnailCacheTests.RunInSta(() =>
        {
            // 不创建 Application/窗口：UiDispatch 以 Application.Current 为空判内联执行，
            // 建窗体会污染进程级单例，STA 线程退出后其它测试的回调被封送到死 Dispatcher（全量套件曾因此拖挂下载测试）。
            // 用控件自身的真实 DataTemplate（StaticResource 延迟到模板应用时才解析，
            // 仅实例化控件抓不到该类资源错误，必须真正应用模板）
            var itemTemplate = new CardGridControl().GridItems.ItemTemplate;

            // 与 CardGridControl.xaml 相同结构：ScrollViewer(CanContentScroll=True) > ItemsPresenter。
            // ScrollViewer 用主题默认模板（含 PART_ScrollContentPresenter 与 CanContentScroll 的
            // TemplateBinding 同步，即生产里的滚动委托挂接路径；无 Application 也可用）
            var scrollerFactory = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollerFactory.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scrollerFactory.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scrollerFactory.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scrollerFactory.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));

            var panelFactory = new FrameworkElementFactory(typeof(VirtualizingWrapPanel));
            panelFactory.SetValue(VirtualizingWrapPanel.ItemWidthProperty, 168.0);
            panelFactory.SetValue(VirtualizingWrapPanel.ItemHeightProperty, 304.0);

            var items = Enumerable.Range(0, 432)
                .Select(i => new CardItemViewModel { Path = $"p{i}", DisplayName = $"卡{i}" })
                .ToList();
            var itemsControl = new ItemsControl
            {
                ItemsSource = items,
                ItemTemplate = itemTemplate,
                Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = scrollerFactory },
                ItemsPanel = new ItemsPanelTemplate(panelFactory),
            };

            itemsControl.ApplyTemplate();
            itemsControl.Measure(new Size(1067, 721)); // 旧 XAML 在此处抛 XamlParseException（找不到 Border 资源）
            itemsControl.Arrange(new Rect(0, 0, 1067, 721));
            itemsControl.UpdateLayout();

            var panel = FindDescendant<VirtualizingWrapPanel>(itemsControl);
            var scroller = FindDescendant<ScrollViewer>(itemsControl);
            Assert.NotNull(panel);
            Assert.NotNull(scroller);

            var columns = Math.Max(1, (int)(panel.ViewportWidth / 168));
            var rows = (432 + columns - 1) / columns;

            // 偏移 0：首行贴视口顶，只实现化可视行 + 1 行缓冲，extent 覆盖全部行
            Assert.Equal(rows * 304, panel.ExtentHeight);
            Assert.True(panel.RealizedCount is > 0 and <= 30, $"realized {panel.RealizedCount}");
            var firstTop = ((UIElement)VisualTreeHelper.GetChild(panel, 0))
                .TransformToAncestor(itemsControl).Transform(new Point(0, 0)).Y;
            Assert.True(firstTop is >= -1 and < 304, $"首行未贴顶 y={firstTop:0}");

            // 滚动到第 10 行：实现化集合跟随，且该行经滚动委托偏移后贴视口顶。
            // UpdateLayout 走布局管理器队列（每个脏元素独立入队），手动 Measure 根元素不会下钻非脏子树
            panel.SetVerticalOffset(10 * 304);
            itemsControl.UpdateLayout();
            var firstContent = (ContentPresenter)VisualTreeHelper.GetChild(panel, 0);
            Assert.Equal($"p{10 * columns}", ((CardItemViewModel)firstContent.Content).Path);
            var scrolledTop = firstContent.TransformToAncestor(itemsControl).Transform(new Point(0, 0)).Y;
            Assert.True(scrolledTop is >= -1 and < 304, $"滚动后第 10 行未贴顶 y={scrolledTop:0}");
        });
    }

    [Fact]
    public void CardGridControl_Instantiates()
    {
        ThumbnailCacheTests.RunInSta(() =>
        {
            var control = new CardGridControl();
            Assert.Equal(0, control.TotalCount);
            Assert.Equal(CardSortType.Favorite, control.SortType);
        });
    }
}
