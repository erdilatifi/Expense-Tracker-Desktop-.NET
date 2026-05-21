using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FinanceTracker.Converters;

/// <summary>
/// Returns Visible when all bound string values are equal, Collapsed otherwise.
/// </summary>
public sealed class StringEqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return Visibility.Collapsed;
        var first = values[0]?.ToString();
        return values.Skip(1).All(v => string.Equals(first, v?.ToString(), StringComparison.OrdinalIgnoreCase))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
