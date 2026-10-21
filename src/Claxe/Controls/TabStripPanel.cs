using System.Windows;
using System.Windows.Controls;

namespace Claxe.Controls;

/// <summary>
/// Lays tabs out in one row with equal widths: each tab gets MaxTabWidth until the
/// strip fills up, then they shrink evenly down to MinTabWidth. Past that the strip
/// scrolls horizontally (mouse wheel / EnsureVisible).
/// </summary>
public sealed class TabStripPanel : Panel
{
    private double _tabWidth;
    private double _viewport;
    private double _extent;

    public TabStripPanel() => ClipToBounds = true;

    public double MaxTabWidth { get; set; } = 240;
    public double MinTabWidth { get; set; } = 96;

    /// <summary>Space kept free to the right (new tab button + a strip to drag the window).</summary>
    public double ReservedWidth { get; set; } = 120;

    /// <summary>Current width of a single tab, used for drag-to-reorder hit testing.</summary>
    public double TabWidth => _tabWidth;

    public double HorizontalOffset { get; private set; }

    public bool CanScroll => _extent > _viewport + 0.5;

    public void ScrollBy(double delta) => SetOffset(HorizontalOffset + delta);

    public void EnsureVisible(int index)
    {
        if (index < 0 || _tabWidth <= 0)
            return;

        var left = index * _tabWidth;
        if (left < HorizontalOffset)
            SetOffset(left);
        else if (left + _tabWidth > HorizontalOffset + _viewport)
            SetOffset(left + _tabWidth - _viewport);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        if (count == 0)
        {
            _extent = _viewport = 0;
            return default;
        }

        var usable = double.IsInfinity(availableSize.Width)
            ? MaxTabWidth * count
            : Math.Max(MinTabWidth, availableSize.Width - ReservedWidth);
        _tabWidth = Math.Clamp(Math.Floor(usable / count), MinTabWidth, MaxTabWidth);
        _extent = _tabWidth * count;
        _viewport = Math.Min(_extent, usable);

        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(_tabWidth, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        HorizontalOffset = Math.Clamp(HorizontalOffset, 0, Math.Max(0, _extent - _viewport));
        return new Size(_viewport, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = -HorizontalOffset;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(x, 0, _tabWidth, finalSize.Height));
            x += _tabWidth;
        }

        return finalSize;
    }

    private void SetOffset(double offset)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, _extent - _viewport));
        if (Math.Abs(offset - HorizontalOffset) < 0.5)
            return;

        HorizontalOffset = offset;
        InvalidateArrange();
    }
}
