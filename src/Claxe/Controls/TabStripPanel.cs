using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Claxe.Controls;

/// <summary>
/// Lays tabs out in one row: compact (pinned) tabs get a fixed narrow width, the rest share
/// the remaining space evenly (MaxTabWidth down to MinTabWidth). Past that the strip scrolls
/// horizontally (mouse wheel / EnsureVisible) with faded edges.
/// </summary>
public sealed class TabStripPanel : Panel
{
    /// <summary>Set on a tab container to give it the narrow pinned width.</summary>
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
        "IsCompact", typeof(bool), typeof(TabStripPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetIsCompact(DependencyObject d) => (bool)d.GetValue(IsCompactProperty);
    public static void SetIsCompact(DependencyObject d, bool value) => d.SetValue(IsCompactProperty, value);

    /// <summary>Extra width in front of a tab (a tab group's label).</summary>
    public static readonly DependencyProperty LeadingWidthProperty = DependencyProperty.RegisterAttached(
        "LeadingWidth", typeof(double), typeof(TabStripPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static double GetLeadingWidth(DependencyObject d) => (double)d.GetValue(LeadingWidthProperty);
    public static void SetLeadingWidth(DependencyObject d, double value) => d.SetValue(LeadingWidthProperty, value);

    /// <summary>The tab itself takes no room (its group is collapsed); only its leading part stays.</summary>
    public static readonly DependencyProperty IsCollapsedProperty = DependencyProperty.RegisterAttached(
        "IsCollapsed", typeof(bool), typeof(TabStripPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetIsCollapsed(DependencyObject d) => (bool)d.GetValue(IsCollapsedProperty);
    public static void SetIsCollapsed(DependencyObject d, bool value) => d.SetValue(IsCollapsedProperty, value);

    /// <summary>How far the active tab's feet and glow may reach outside the panel.</summary>
    private const double Bleed = 10;
    private const double FadeWidth = 28;

    private double[] _offsets = [];
    private double[] _widths = [];
    private double _tabWidth;
    private double _viewport;
    private double _extent;

    public double MaxTabWidth { get; set; } = 240;
    public double MinTabWidth { get; set; } = 96;
    public double CompactWidth { get; set; } = 48;

    /// <summary>Space kept free to the right (new tab button + a strip to drag the window).</summary>
    public double ReservedWidth { get; set; } = 120;

    /// <summary>Width of a regular (not pinned) tab.</summary>
    public double TabWidth => _tabWidth;

    public double HorizontalOffset { get; private set; }

    public bool CanScroll => _extent > _viewport + 0.5;

    public void ScrollBy(double delta) => SetOffset(HorizontalOffset + delta);

    /// <summary>Index of the tab under an x position in panel coordinates (scroll included).</summary>
    public int IndexAt(double x)
    {
        x += HorizontalOffset;
        for (var i = 0; i < _offsets.Length; i++)
        {
            if (x < _offsets[i] + _widths[i])
                return i;
        }
        return _offsets.Length - 1;
    }

    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= _offsets.Length)
            return;

        var left = _offsets[index];
        var right = left + _widths[index];
        if (left < HorizontalOffset)
            SetOffset(left);
        else if (right > HorizontalOffset + _viewport)
            SetOffset(right - _viewport);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;
        var count = children.Count;
        if (count == 0)
        {
            _offsets = _widths = [];
            _extent = _viewport = 0;
            return default;
        }

        var shown = children.Cast<UIElement>().Where(c => !GetIsCollapsed(c)).ToList();
        var compact = shown.Count(GetIsCompact);
        var regular = shown.Count - compact;
        var leading = children.Cast<UIElement>().Sum(GetLeadingWidth);
        var usable = double.IsInfinity(availableSize.Width)
            ? leading + compact * CompactWidth + MaxTabWidth * Math.Max(regular, 1)
            : Math.Max(MinTabWidth, availableSize.Width - ReservedWidth);
        _tabWidth = regular == 0 ? 0 : Math.Clamp(Math.Floor((usable - leading - compact * CompactWidth) / regular), MinTabWidth, MaxTabWidth);

        _offsets = new double[count];
        _widths = new double[count];
        double x = 0, height = 0;
        for (var i = 0; i < count; i++)
        {
            var child = children[i];
            var width = GetLeadingWidth(child) + (GetIsCollapsed(child) ? 0 : GetIsCompact(child) ? CompactWidth : _tabWidth);
            _offsets[i] = x;
            _widths[i] = width;
            x += width;
            child.Measure(new Size(width, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        _extent = x;
        _viewport = Math.Min(_extent, usable);
        HorizontalOffset = Math.Clamp(HorizontalOffset, 0, Math.Max(0, _extent - _viewport));
        return new Size(_viewport, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        for (var i = 0; i < children.Count && i < _offsets.Length; i++)
            children[i].Arrange(new Rect(_offsets[i] - HorizontalOffset, 0, _widths[i], finalSize.Height));

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
