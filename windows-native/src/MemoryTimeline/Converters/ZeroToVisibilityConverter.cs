using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace MemoryTimeline.Converters;

/// <summary>
/// Converts zero (or null) to Visibility.Visible and non-zero to Visibility.Collapsed.
/// Useful for showing empty states when count is 0.
/// </summary>
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null)
            return Visibility.Visible;

        if (value is int intVal)
            return intVal == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (value is long longVal)
            return longVal == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (value is double doubleVal)
            return doubleVal == 0 ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
