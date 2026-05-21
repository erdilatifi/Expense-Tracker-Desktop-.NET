using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FinanceTracker.Converters;

/// <summary>
/// Converts boolean values to visibility, with optional inversion via the "Invert" parameter.
/// </summary>
public sealed class VisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value switch
        {
            bool boolValue => boolValue,
            int intValue => intValue > 0,
            _ => value is not null
        };
        if (parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value is Visibility.Visible;
        if (parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }

        return isVisible;
    }
}
