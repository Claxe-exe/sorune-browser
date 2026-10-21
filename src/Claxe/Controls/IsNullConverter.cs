using System.Globalization;
using System.Windows.Data;

namespace Claxe.Controls;

/// <summary>True when the value is null (for "show the fallback until the real thing arrives" triggers).</summary>
public sealed class IsNullConverter : IValueConverter
{
    public static IsNullConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value is null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
