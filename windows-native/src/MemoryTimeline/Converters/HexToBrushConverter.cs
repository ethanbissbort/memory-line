using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MemoryTimeline.Converters;

/// <summary>
/// Converts a hex color string ("#RRGGBB" or "#AARRGGBB") to a
/// <see cref="SolidColorBrush"/>. Invalid or missing input falls back to a
/// neutral gray brush so avatars and swatches always render.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    private static readonly Color FallbackColor = Color.FromArgb(255, 128, 128, 128);

    /// <summary>
    /// Converts a hex color string to a <see cref="SolidColorBrush"/>;
    /// returns a neutral gray brush for malformed input.
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && TryParseHexColor(hex, out var color))
        {
            return new SolidColorBrush(color);
        }

        return new SolidColorBrush(FallbackColor);
    }

    /// <summary>Not supported.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Parses "RRGGBB" or "AARRGGBB" into a <see cref="Color"/>, with the
    /// leading '#' optional (stored color codes are not consistent about it).
    /// Returns false (never throws) for any other shape.
    /// </summary>
    private static bool TryParseHexColor(string hex, out Color color)
    {
        color = FallbackColor;

        var digits = hex.Trim().TrimStart('#');
        if (digits.Length != 6 && digits.Length != 8)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[4];
        var byteCount = digits.Length / 2;
        for (var i = 0; i < byteCount; i++)
        {
            if (!byte.TryParse(
                    digits.AsSpan(i * 2, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out bytes[i]))
            {
                return false;
            }
        }

        color = digits.Length == 6
            ? Color.FromArgb(255, bytes[0], bytes[1], bytes[2])
            : Color.FromArgb(bytes[0], bytes[1], bytes[2], bytes[3]);
        return true;
    }
}
