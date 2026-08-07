using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MemoryTimeline.Converters;

/// <summary>
/// Converts a hex color string ("#RRGGBB") into the soft horizontal gradient
/// used for a timeline event's date-uncertainty underlay: the category/era
/// color at low opacity in the middle, fading to fully transparent at both
/// edges. Malformed input falls back to neutral gray so the band still reads
/// as an uncertainty cue rather than vanishing.
/// </summary>
/// <remarks>
/// This lives in the app project, not in <c>TimelineEventDto</c>: Core is
/// UI-framework-neutral and must not reference WinUI brush types.
/// </remarks>
public class HexToUncertaintyBrushConverter : IValueConverter
{
    private const byte MidAlpha = 0x30;
    private static readonly Color FallbackColor = Color.FromArgb(255, 128, 128, 128);

    /// <summary>Builds the edge-faded gradient for a hex color string.</summary>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var color = value is string hex && TryParseHexColor(hex, out var parsed)
            ? parsed
            : FallbackColor;

        var mid = Color.FromArgb(MidAlpha, color.R, color.G, color.B);
        var edge = Color.FromArgb(0x00, color.R, color.G, color.B);

        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint = new Windows.Foundation.Point(1, 0.5)
        };
        brush.GradientStops.Add(new GradientStop { Color = edge, Offset = 0.0 });
        brush.GradientStops.Add(new GradientStop { Color = mid, Offset = 0.12 });
        brush.GradientStops.Add(new GradientStop { Color = mid, Offset = 0.88 });
        brush.GradientStops.Add(new GradientStop { Color = edge, Offset = 1.0 });
        return brush;
    }

    /// <summary>Not supported.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Parses "RRGGBB" with an optional leading '#'. Returns false (never
    /// throws) for any other shape.
    /// </summary>
    private static bool TryParseHexColor(string hex, out Color color)
    {
        color = FallbackColor;

        var digits = hex.Trim().TrimStart('#');
        if (digits.Length != 6)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[3];
        for (var i = 0; i < 3; i++)
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

        color = Color.FromArgb(255, bytes[0], bytes[1], bytes[2]);
        return true;
    }
}
