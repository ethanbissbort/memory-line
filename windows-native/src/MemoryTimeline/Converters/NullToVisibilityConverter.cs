using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace MemoryTimeline.Converters;

/// <summary>
/// Converts null to Visibility.Collapsed and non-null to Visibility.Visible.
/// Use ConverterParameter="Invert" to reverse the behavior.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isNull = value == null || (value is string s && string.IsNullOrEmpty(s));
        var invert = parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase);

        if (invert)
        {
            return isNull ? Visibility.Visible : Visibility.Collapsed;
        }
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
