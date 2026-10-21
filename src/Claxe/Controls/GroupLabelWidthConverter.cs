using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Claxe.Controls;

/// <summary>
/// Width of a tab group's label: [IsGroupStart, group name] → the room the tab strip keeps in
/// front of the group's first tab. With "label" as parameter, the coloured pill's own width.
/// </summary>
public sealed class GroupLabelWidthConverter : IMultiValueConverter
{
    public static GroupLabelWidthConverter Instance { get; } = new();

    private const double Margin = 8;
    private const double Padding = 20;
    private const double EmptyWidth = 16;
    private const double MaxText = 140;
    private static readonly Typeface Font = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not true)
            return 0.0;

        var name = (values[1] as string)?.Trim() ?? "";
        var pill = name.Length == 0
            ? EmptyWidth
            : Math.Min(MaxText, new FormattedText(name, culture, FlowDirection.LeftToRight, Font, 12, Brushes.Black, 1).WidthIncludingTrailingWhitespace) + Padding;
        pill = Math.Ceiling(pill);
        return parameter as string == "label" ? pill : pill + Margin;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
