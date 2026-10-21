using System.Windows;
using System.Windows.Controls;

namespace Claxe.Controls;

/// <summary>
/// Lays tabs out in one row with equal widths: each tab gets MaxTabWidth until the
/// strip fills up, then they shrink evenly down to MinTabWidth.
/// </summary>
public sealed class TabStripPanel : Panel
{
    private double _tabWidth;

    public double MaxTabWidth { get; set; } = 240;
    public double MinTabWidth { get; set; } = 56;

    /// <summary>Space kept free to the right (new tab button + a strip to drag the window).</summary>
    public double ReservedWidth { get; set; } = 120;

    /// <summary>Current width of a single tab, used for drag-to-reorder hit testing.</summary>
    public double TabWidth => _tabWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        if (count == 0)
            return default;

        var usable = double.IsInfinity(availableSize.Width)
            ? MaxTabWidth * count
            : Math.Max(0, availableSize.Width - ReservedWidth);
        _tabWidth = Math.Clamp(Math.Floor(usable / count), MinTabWidth, MaxTabWidth);

        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(_tabWidth, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(_tabWidth * count, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(x, 0, _tabWidth, finalSize.Height));
            x += _tabWidth;
        }

        return finalSize;
    }
}
