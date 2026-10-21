using System.Windows;
using System.Windows.Media;

namespace Claxe.Controls;

/// <summary>
/// Active tab silhouette: rounded top corners and concave "feet" at the bottom that
/// flow into the toolbar. The feet extend <see cref="FootRadius"/> beyond the body,
/// so the element is laid out with a matching negative horizontal margin.
/// </summary>
public sealed class TabShape : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(TabShape),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(TabShape),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(TabShape),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(TabShape),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FootRadiusProperty = DependencyProperty.Register(
        nameof(FootRadius), typeof(double), typeof(TabShape),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Drawn along the sides and top only (never along the bottom edge).</summary>
    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double FootRadius
    {
        get => (double)GetValue(FootRadiusProperty);
        set => SetValue(FootRadiusProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var f = Math.Min(FootRadius, h / 2);
        var r = Math.Clamp(CornerRadius, 0, Math.Min((w - 2 * f) / 2, h - f));
        if (w <= 2 * f || h <= f)
            return;

        if (Fill is { } fill)
            dc.DrawGeometry(fill, null, BuildOutline(w, h, f, r, closed: true));

        if (Stroke is { } stroke && StrokeThickness > 0)
        {
            // Inset by half the pen so the line stays inside the shape and isn't clipped.
            var inset = StrokeThickness / 2;
            var pen = new Pen(stroke, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.PushTransform(new TranslateTransform(0, inset));
            dc.DrawGeometry(null, pen, BuildOutline(w, h - inset, f, r, closed: false));
            dc.Pop();
        }
    }

    private static StreamGeometry BuildOutline(double w, double h, double f, double r, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, h), isFilled: closed, isClosed: closed);
            // Left foot (concave), up the left side, rounded top-left corner.
            ctx.ArcTo(new Point(f, h - f), new Size(f, f), 0, false, SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(new Point(f, r), true, true);
            ctx.ArcTo(new Point(f + r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            // Top edge, rounded top-right corner, down the right side, right foot.
            ctx.LineTo(new Point(w - f - r, 0), true, true);
            ctx.ArcTo(new Point(w - f, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(w - f, h - f), true, true);
            ctx.ArcTo(new Point(w, h), new Size(f, f), 0, false, SweepDirection.Counterclockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }
}
