using System.Globalization;
using System.Windows.Data;

namespace Claxe.Controls;

/// <summary>Turns a length into its negative (used to offset a copy of the backdrop).</summary>
public sealed class NegateConverter : IValueConverter
{
    public static NegateConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? -d : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
