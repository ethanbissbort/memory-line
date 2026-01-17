using Microsoft.UI.Xaml.Data;

namespace MemoryTimeline.Converters;

/// <summary>
/// Converts a normalized value (0-1) to a pixel height for density charts.
/// Default max height: 100 pixels.
/// Use ConverterParameter to specify a custom max height.
/// </summary>
public class NormalizedToHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var maxHeight = 100.0;
        if (parameter is string paramStr && double.TryParse(paramStr, out var customMax))
        {
            maxHeight = customMax;
        }

        if (value is double normalizedValue)
        {
            return Math.Max(4, normalizedValue * maxHeight);
        }

        return 4.0; // Minimum height
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
