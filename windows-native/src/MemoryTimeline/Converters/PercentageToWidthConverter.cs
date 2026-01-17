using Microsoft.UI.Xaml.Data;

namespace MemoryTimeline.Converters;

/// <summary>
/// Converts a percentage (0-100) to a pixel width for bar charts.
/// Default max width: 200 pixels.
/// Use ConverterParameter to specify a custom max width.
/// </summary>
public class PercentageToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var maxWidth = 200.0;
        if (parameter is string paramStr && double.TryParse(paramStr, out var customMax))
        {
            maxWidth = customMax;
        }

        if (value is double percentage)
        {
            return Math.Max(2, (percentage / 100.0) * maxWidth);
        }

        if (value is int intPercentage)
        {
            return Math.Max(2, (intPercentage / 100.0) * maxWidth);
        }

        return 2.0; // Minimum width
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
