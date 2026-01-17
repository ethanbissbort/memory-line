using Microsoft.UI.Xaml.Data;

namespace MemoryTimeline.Converters;

/// <summary>
/// Converts DateTime to a formatted string.
/// Default format: "MMM d, yyyy"
/// Use ConverterParameter to specify a custom format.
/// </summary>
public class DateFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime dateTime)
        {
            var format = parameter as string ?? "MMM d, yyyy";
            return dateTime.ToString(format);
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            var format = parameter as string ?? "MMM d, yyyy";
            return dateTimeOffset.ToString(format);
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
