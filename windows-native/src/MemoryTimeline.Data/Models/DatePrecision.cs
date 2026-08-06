namespace MemoryTimeline.Data.Models;

/// <summary>
/// How much of an event's <c>StartDate</c> to believe. The anchor date is
/// always populated (so ordering and range queries keep working); this enum
/// records the coarsest unit the date is actually known to.
/// Stored as INTEGER in SQLite - the numeric values are part of the schema
/// contract and must never be reordered.
/// </summary>
public enum DatePrecision
{
    /// <summary>The exact date and time are known.</summary>
    Exact = 0,

    /// <summary>The calendar day is known (default; matches all pre-existing rows).</summary>
    Day = 1,

    /// <summary>Only the month and year are known.</summary>
    Month = 2,

    /// <summary>Only the (~3-month meteorological) season and year are known.</summary>
    Season = 3,

    /// <summary>Only the year is known.</summary>
    Year = 4,

    /// <summary>Only the calendar decade is known.</summary>
    Decade = 5,

    /// <summary>No usable date information; the anchor is a placeholder.</summary>
    Unknown = 6
}

/// <summary>
/// Pure date-window math for <see cref="DatePrecision"/>. Kept in the Data
/// layer (no UI concepts) so repositories and services can both use it.
/// </summary>
public static class DatePrecisionExtensions
{
    /// <summary>
    /// Returns the inclusive [Earliest, Latest] window implied by an anchor
    /// date and its precision, used when no explicit
    /// EarliestPossible/LatestPossible override is stored.
    ///
    /// Windows end one second before the next period starts (e.g. Decade 1990
    /// = 1990-01-01 00:00:00 .. 1999-12-31 23:59:59). Seasons are the
    /// meteorological ones: Dec-Feb winter, Mar-May spring, Jun-Aug summer,
    /// Sep-Nov autumn; a December anchor's winter window crosses the year
    /// boundary into Jan/Feb of the NEXT year, and a Jan/Feb anchor's window
    /// starts in December of the PREVIOUS year.
    ///
    /// <see cref="DatePrecision.Exact"/> and <see cref="DatePrecision.Unknown"/>
    /// both return a degenerate window (the anchor itself): Exact because the
    /// moment is fully known, Unknown because no window can honestly be
    /// derived - callers that care must treat Unknown specially rather than
    /// trusting its window.
    /// </summary>
    public static (DateTime Earliest, DateTime Latest) GetWindow(DateTime anchor, DatePrecision precision)
    {
        switch (precision)
        {
            case DatePrecision.Day:
            {
                var start = anchor.Date;
                return (start, EndOfPeriodStartingAt(start, monthsInPeriod: 0, daysInPeriod: 1));
            }

            case DatePrecision.Month:
            {
                var start = new DateTime(anchor.Year, anchor.Month, 1);
                return (start, EndOfPeriodStartingAt(start, monthsInPeriod: 1));
            }

            case DatePrecision.Season:
            {
                var start = GetSeasonStart(anchor);
                return (start, EndOfPeriodStartingAt(start, monthsInPeriod: 3));
            }

            case DatePrecision.Year:
            {
                var start = new DateTime(anchor.Year, 1, 1);
                return (start, EndOfPeriodStartingAt(start, monthsInPeriod: 12));
            }

            case DatePrecision.Decade:
            {
                var start = new DateTime(anchor.Year / 10 * 10, 1, 1);
                return (start, EndOfPeriodStartingAt(start, monthsInPeriod: 120));
            }

            case DatePrecision.Exact:
            case DatePrecision.Unknown:
            default:
                return (anchor, anchor);
        }
    }

    /// <summary>
    /// Start of the meteorological season containing the anchor. December
    /// belongs to the winter that STARTS in the anchor's year; January and
    /// February belong to the winter that started the PREVIOUS December.
    /// </summary>
    private static DateTime GetSeasonStart(DateTime anchor)
    {
        return anchor.Month switch
        {
            12 => new DateTime(anchor.Year, 12, 1),
            // Clamp at the calendar's lower bound (year 1 has no previous December).
            1 or 2 => anchor.Year <= 1
                ? DateTime.MinValue
                : new DateTime(anchor.Year - 1, 12, 1),
            3 or 4 or 5 => new DateTime(anchor.Year, 3, 1),
            6 or 7 or 8 => new DateTime(anchor.Year, 6, 1),
            _ => new DateTime(anchor.Year, 9, 1)
        };
    }

    /// <summary>
    /// Inclusive end of a period: one second before the next period starts,
    /// clamped to <see cref="DateTime.MaxValue"/> when the next period would
    /// fall outside the representable calendar (year 9999).
    /// </summary>
    private static DateTime EndOfPeriodStartingAt(DateTime periodStart, int monthsInPeriod, int daysInPeriod = 0)
    {
        // Overflow guard: adding beyond year 9999 throws, so clamp instead.
        var totalMonths = (periodStart.Year - 1) * 12 + (periodStart.Month - 1) + monthsInPeriod;
        if (totalMonths / 12 + 1 > 9999)
        {
            return DateTime.MaxValue;
        }

        if (daysInPeriod > 0 && periodStart.Date >= DateTime.MaxValue.Date)
        {
            return DateTime.MaxValue;
        }

        var nextPeriodStart = monthsInPeriod > 0
            ? periodStart.AddMonths(monthsInPeriod)
            : periodStart.AddDays(daysInPeriod);

        return nextPeriodStart.AddSeconds(-1);
    }
}

/// <summary>
/// Defensive parser between <see cref="DatePrecision"/> and the lowercase
/// string values used in the LLM extraction schema
/// (exact|day|month|season|year|decade|unknown).
/// </summary>
public static class DatePrecisionParser
{
    /// <summary>
    /// Parses a raw precision string case-insensitively. Null, empty, or
    /// unrecognized input falls back to <see cref="DatePrecision.Day"/> (the
    /// safe default matching all pre-existing rows); the literal string
    /// "unknown" maps to <see cref="DatePrecision.Unknown"/>.
    /// </summary>
    public static DatePrecision Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DatePrecision.Day;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "exact" => DatePrecision.Exact,
            "day" => DatePrecision.Day,
            "month" => DatePrecision.Month,
            "season" => DatePrecision.Season,
            "year" => DatePrecision.Year,
            "decade" => DatePrecision.Decade,
            "unknown" => DatePrecision.Unknown,
            _ => DatePrecision.Day
        };
    }

    /// <summary>
    /// Canonical lowercase string for a precision (inverse of <see cref="Parse"/>).
    /// </summary>
    public static string ToStringValue(DatePrecision precision)
    {
        return precision switch
        {
            DatePrecision.Exact => "exact",
            DatePrecision.Day => "day",
            DatePrecision.Month => "month",
            DatePrecision.Season => "season",
            DatePrecision.Year => "year",
            DatePrecision.Decade => "decade",
            DatePrecision.Unknown => "unknown",
            _ => "day"
        };
    }
}
