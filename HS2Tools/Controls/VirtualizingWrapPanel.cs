using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HS2Tools.Controls;

/// <summary>
/// 虚拟化 Wrap 面板（WPF 无内置 VirtualizingWrapPanel）。
/// 简化前提：所有子项尺寸固定（ItemWidth × ItemHeight），因此列数/行号可直接按索引算出，
/// 只需实现经典 VirtualizingPanel + IScrollInfo 配方（按可视区实现化、离屏回收）。
/// 须放在 CanContentScroll=true 的 ScrollViewer 中使用。被挂为滚动面板后，
/// ScrollContentPresenter 会以无限高测量本面板：此时沿用上一趟有限视口决定实现化范围，
/// 且 DesiredSize 回报 extent——把 ∞ 原样返回会被 WPF 抛 InvalidOperationException。
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(160.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(296.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>子项固定宽度（含外边距）</summary>
    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    /// <summary>子项固定高度（含外边距）</summary>
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    private Size _extent;
    private Size _viewport;
    private Point _offset;

    private int ColumnCount(double width) => Math.Max(1, (int)Math.Floor(width / ItemWidth));

    private int ItemCount => (ItemsControl.GetItemsOwner(this)?.Items.Count) ?? 0;

    /// <summary>当前已实现的容器数（测试探针）</summary>
    internal int RealizedCount => InternalChildren.Count;

    // ==================== 虚拟化核心 ====================

    protected override Size MeasureOverride(Size availableSize)
    {
        var itemCount = ItemCount;
        var columns = ColumnCount(availableSize.Width);
        var rows = itemCount == 0 ? 0 : (itemCount + columns - 1) / columns;

        // 无限高测量的两种来源：滚动面板委托（ScrollOwner 非空，常态）与非滚动宿主（退化，全量实现化）。
        // 前者沿用上一趟有限视口——首趟测量必为有限（面板须先存在才能被挂为滚动信息），视口必有值。
        var infiniteMeasure = double.IsPositiveInfinity(availableSize.Height);
        var viewportHeight = !infiniteMeasure ? availableSize.Height
            : ScrollOwner is not null ? _viewport.Height
            : rows * ItemHeight;

        _extent = new Size(columns * ItemWidth, rows * ItemHeight);
        _viewport = new Size(availableSize.Width, viewportHeight);
        SetVerticalOffset(_offset.Y); // 钳制偏移（窗口变宽后行数变少）
        ScrollOwner?.InvalidateScrollInfo();

        if (itemCount == 0)
        {
            CleanUpItems(0, -1);
            return infiniteMeasure ? _extent : availableSize;
        }

        // 可视范围（多实现化一行做缓冲）
        var firstRow = Math.Max(0, (int)Math.Floor(_offset.Y / ItemHeight));
        var visibleRows = (int)Math.Ceiling(viewportHeight / ItemHeight) + 1;
        var firstIndex = firstRow * columns;
        var lastIndex = Math.Min(itemCount - 1, (firstRow + visibleRows) * columns - 1);

        CleanUpItems(firstIndex, lastIndex);

        var generator = ItemContainerGenerator;
        var startPos = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;
        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (var i = firstIndex; i <= lastIndex; i++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var newlyRealized);
                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                        AddInternalChild(child);
                    else
                        InsertInternalChild(childIndex, child);
                    generator.PrepareItemContainer(child);
                }
                child.Measure(new Size(ItemWidth, ItemHeight));
            }
        }

        // ∞ 不得作为 DesiredSize 返回（WPF 抛 InvalidOperationException），统一回报 extent
        return infiniteMeasure ? _extent : availableSize;
    }

    /// <summary>回收可视范围外的容器</summary>
    private void CleanUpItems(int firstVisibleIndex, int lastVisibleIndex)
    {
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        for (var i = children.Count - 1; i >= 0; i--)
        {
            var pos = new GeneratorPosition(i, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(pos);
            if (itemIndex < firstVisibleIndex || itemIndex > lastVisibleIndex)
            {
                generator.Remove(pos, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    /// <summary>IndexFromContainer 在具体类上（接口未暴露），其余生成器操作走接口</summary>
    private ItemContainerGenerator ConcreteGenerator => (ItemContainerGenerator)ItemContainerGenerator;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnCount(finalSize.Width);
        var generator = ConcreteGenerator;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var itemIndex = generator.IndexFromContainer(child);
            if (itemIndex < 0)
                continue;
            var row = itemIndex / columns;
            var col = itemIndex % columns;
            // IScrollInfo 约定：ScrollContentPresenter 不替内容做滚动平移，
            // 偏移由面板在排列时自行扣除（少了这一步：一滚动可视区就空白）
            child.Arrange(new Rect(col * ItemWidth, row * ItemHeight - _offset.Y, ItemWidth, ItemHeight));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }

    // ==================== IScrollInfo（像素单位滚动） ====================

    public ScrollViewer? ScrollOwner { get; set; }
    public bool CanHorizontallyScroll { get => false; set { } }
    public bool CanVerticallyScroll { get => true; set { } }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;

    public void SetVerticalOffset(double offset)
    {
        var clamped = Math.Max(0, Math.Min(offset, Math.Max(0, _extent.Height - _viewport.Height)));
        if (_offset.Y == clamped)
            return;
        _offset.Y = clamped;
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public void SetHorizontalOffset(double offset) { }

    public void LineUp() => SetVerticalOffset(_offset.Y - ItemHeight / 4);
    public void LineDown() => SetVerticalOffset(_offset.Y + ItemHeight / 4);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - ItemHeight);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + ItemHeight);
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }

    /// <summary>把目标子项滚动到可视区内（键盘焦点/查找用）</summary>
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            if (InternalChildren[i] != visual)
                continue;
            var itemIndex = ConcreteGenerator.IndexFromContainer(InternalChildren[i]);
            if (itemIndex < 0)
                break;
            var columns = ColumnCount(_viewport.Width);
            var y = itemIndex / columns * ItemHeight;
            if (y < _offset.Y)
                SetVerticalOffset(y);
            else if (y + ItemHeight > _offset.Y + _viewport.Height)
                SetVerticalOffset(y + ItemHeight - _viewport.Height);
            break;
        }
        return rectangle;
    }
}
