using System.Globalization;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Core.Models;

/// <summary>
/// Formats event dates honestly according to their <see cref="DatePrecision"/>:
/// a year-precision memory renders as "1998", not as a fabricated exact day.
/// Uses <see cref="CultureInfo.InvariantCulture"/> (English month/season names)
/// so output is stable across machines, matching the fixed-format date strings
/// used by the existing DTO displays.
/// </summary>
public static class DateDisplay
{
    /// <summary>
    /// Formats an anchor date at its precision:
    /// Exact "14 Mar 2019, 10:30" - Day "14 Mar 2019" - Month "March 2019" -
    /// Season "Summer 1998" - Year "1998" - Decade "1990s" - Unknown "Date unknown".
    ///
    /// Season naming matches <see cref="DatePrecisionExtensions.GetWindow"/>'s
    /// meteorological windows, and winter is labeled with the year its December
    /// STARTS in: Dec 1998, Jan 1999, and Feb 1999 all fall in the same window
    /// and all render as "Winter 1998" (one label per window, never two labels
    /// for one winter).
    ///
    /// An <paramref name="end"/> date is appended as a range ("14 Mar 2019 -
    /// 20 Mar 2019") only for Exact/Day precision; at coarser precisions the
    /// single window description already conveys the spread, so end is ignored.
    /// </summary>
    public static string FormatPrecise(DateTime anchor, DatePrecision precision, DateTime? end = null)
    {
        var culture = CultureInfo.InvariantCulture;

        switch (precision)
        {
            case DatePrecision.Exact:
                return AppendEnd(anchor.ToString("d MMM yyyy, HH:mm", culture), precision, end, culture);

            case DatePrecision.Day:
                return AppendEnd(anchor.ToString("d MMM yyyy", culture), precision, end, culture);

            case DatePrecision.Month:
                return anchor.ToString("MMMM yyyy", culture);

            case DatePrecision.Season:
                return FormatSeason(anchor, culture);

            case DatePrecision.Year:
                return anchor.Year.ToString(culture);

            case DatePrecision.Decade:
                return string.Create(culture, $"{anchor.Year / 10 * 10}s");

            case DatePrecision.Unknown:
            default:
                return "Date unknown";
        }
    }

    /// <summary>
    /// Short human label for a precision value ("Day", "Season", ...), for
    /// chips and pickers.
    /// </summary>
    public static string GetPrecisionLabel(DatePrecision precision)
    {
        return precision switch
        {
            DatePrecision.Exact => "Exact time",
            DatePrecision.Day => "Day",
            DatePrecision.Month => "Month",
            DatePrecision.Season => "Season",
            DatePrecision.Year => "Year",
            DatePrecision.Decade => "Decade",
            _ => "Unknown"
        };
    }

    private static string FormatSeason(DateTime anchor, CultureInfo culture)
    {
        // Meteorological seasons, consistent with DatePrecisionExtensions.GetWindow.
        // Winter is labeled by the year of its starting December: Jan/Feb anchors
        // belong to the winter that started the PREVIOUS year.
        var (name, year) = anchor.Month switch
        {
            12 => ("Winter", anchor.Year),
            1 or 2 => ("Winter", Math.Max(1, anchor.Year - 1)),
            3 or 4 or 5 => ("Spring", anchor.Year),
            6 or 7 or 8 => ("Summer", anchor.Year),
            _ => ("Autumn", anchor.Year)
        };

        return string.Create(culture, $"{name} {year}");
    }

    private static string AppendEnd(string formatted, DatePrecision precision, DateTime? end, CultureInfo culture)
    {
        if (!end.HasValue)
        {
            return formatted;
        }

        var endText = precision == DatePrecision.Exact
            ? end.Value.ToString("d MMM yyyy, HH:mm", culture)
            : end.Value.ToString("d MMM yyyy", culture);

        return endText == formatted ? formatted : $"{formatted} - {endText}";
    }
}
