using System.Globalization;
using System.Windows.Data;

namespace Claxe.Controls;

/// <summary>Shifts the find bar so it sits under the right end of the address bar.</summary>
public sealed class FindOffsetConverter : IValueConverter
{
    private const double FindBarWidth = 380;

    public static FindOffsetConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double width ? Math.Max(0, width - FindBarWidth) : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
