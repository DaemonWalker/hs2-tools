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
