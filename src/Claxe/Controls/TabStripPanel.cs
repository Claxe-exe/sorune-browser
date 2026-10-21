using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Claxe.Controls;

/// <summary>
/// Lays tabs out in one row with equal widths: each tab gets MaxTabWidth until the
/// strip fills up, then they shrink evenly down to MinTabWidth. Past that the strip
/// scrolls horizontally (mouse wheel / EnsureVisible) with faded edges.
/// </summary>
public sealed class TabStripPanel : Panel
{
    private double _tabWidth;
    private double _viewport;
    private double _extent;

    /// <summary>How far the active tab's feet and glow may reach outside the panel.</summary>
    private const double Bleed = 10;
    private const double FadeWidth = 28;

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

        UpdateEdgeFade(finalSize.Width);
        return finalSize;
    }

    /// <summary>Clips scrolled-away tabs but lets the active tab's feet and glow spill over the edges.</summary>
    protected override Geometry GetLayoutClip(Size layoutSlotSize)
        => new RectangleGeometry(new Rect(-Bleed, -Bleed, RenderSize.Width + 2 * Bleed, RenderSize.Height + Bleed));

    /// <summary>Fades the strip's edges while tabs are hidden on that side, so overflow is visible.</summary>
    private void UpdateEdgeFade(double width)
    {
        var fadeLeft = HorizontalOffset > 0.5;
        var fadeRight = HorizontalOffset < _extent - _viewport - 0.5;
        if (!fadeLeft && !fadeRight || width <= 2 * FadeWidth)
        {
            OpacityMask = null;
            return;
        }

        var edge = FadeWidth / width;
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        mask.GradientStops.Add(new GradientStop(fadeLeft ? Colors.Transparent : Colors.Black, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, edge));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 1 - edge));
        mask.GradientStops.Add(new GradientStop(fadeRight ? Colors.Transparent : Colors.Black, 1));
        mask.Freeze();
        OpacityMask = mask;
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
