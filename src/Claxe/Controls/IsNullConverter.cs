using System.Globalization;
using System.Windows.Data;

namespace Claxe.Controls;

/// <summary>
/// True when the value is null (for "show the fallback until the real thing arrives" triggers).
/// With ConverterParameter="collapse" it yields a Visibility instead: Collapsed when null.
/// </summary>
public sealed class IsNullConverter : IValueConverter
{
    public static IsNullConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => parameter as string == "collapse"
            ? value is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible
            : value is null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
