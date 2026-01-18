namespace MemoryTimeline.Core.Models;

/// <summary>
/// Centralized coordinate transformation service for timeline navigation.
/// Follows Adobe Premiere's dual coordinate system model (date-space vs screen-space).
///
/// The relationship is:
///   screenX = (date - scrollOffset).TotalDays * pixelsPerDay
///   date = scrollOffset.AddDays(screenX / pixelsPerDay)
/// </summary>
public class TimelineCoordinateConverter
{
    /// <summary>
    /// The leftmost visible date (scroll offset in date-space).
    /// </summary>
    public DateTime ScrollOffset { get; set; }

    /// <summary>
    /// Current zoom level expressed as pixels per day.
    /// </summary>
    public double PixelsPerDay { get; set; }

    /// <summary>
    /// Visible width in pixels.
    /// </summary>
    public double ViewportWidth { get; set; }

    /// <summary>
    /// Creates a converter from a TimelineViewport.
    /// </summary>
    public static TimelineCoordinateConverter FromViewport(TimelineViewport viewport)
    {
        return new TimelineCoordinateConverter
        {
            ScrollOffset = viewport.StartDate,
            PixelsPerDay = viewport.PixelsPerDay,
            ViewportWidth = viewport.ViewportWidth
        };
    }

    /// <summary>
    /// Converts a date to screen-space X coordinate.
    /// </summary>
    public double DateToScreen(DateTime date)
    {
        return (date - ScrollOffset).TotalDays * PixelsPerDay;
    }

    /// <summary>
    /// Converts a screen-space X coordinate to date.
    /// </summary>
    public DateTime ScreenToDate(double screenX)
    {
        return ScrollOffset.AddDays(screenX / PixelsPerDay);
    }

    /// <summary>
    /// Gets the leftmost visible date.
    /// </summary>
    public DateTime VisibleStartDate => ScrollOffset;

    /// <summary>
    /// Gets the rightmost visible date.
    /// </summary>
    public DateTime VisibleEndDate => ScrollOffset.AddDays(ViewportWidth / PixelsPerDay);

    /// <summary>
    /// Gets the duration of the visible time span.
    /// </summary>
    public TimeSpan VisibleDuration => VisibleEndDate - VisibleStartDate;

    /// <summary>
    /// Gets the visible duration in days.
    /// </summary>
    public double VisibleDays => ViewportWidth / PixelsPerDay;

    /// <summary>
    /// Checks if a date is within the visible viewport.
    /// </summary>
    public bool IsDateVisible(DateTime date)
    {
        return date >= VisibleStartDate && date <= VisibleEndDate;
    }

    /// <summary>
    /// Checks if a date range overlaps with the visible viewport.
    /// </summary>
    public bool IsRangeVisible(DateTime start, DateTime? end)
    {
        var rangeEnd = end ?? start;
        return rangeEnd >= VisibleStartDate && start <= VisibleEndDate;
    }
}

/// <summary>
/// Configuration for time ruler tick marks with adaptive density.
/// Follows Adobe Premiere's "nice numbers" algorithm.
/// </summary>
public class TimeRulerConfig
{
    /// <summary>
    /// Major tick interval in days.
    /// </summary>
    public double MajorTickIntervalDays { get; set; }

    /// <summary>
    /// Number of minor ticks between major ticks.
    /// </summary>
    public int MinorTicksPerMajor { get; set; }

    /// <summary>
    /// Label format for major ticks.
    /// </summary>
    public string LabelFormat { get; set; } = "MMM yyyy";

    /// <summary>
    /// The type of interval (for formatting purposes).
    /// </summary>
    public TimeRulerIntervalType IntervalType { get; set; }

    /// <summary>
    /// Calculates the optimal ruler configuration based on current zoom level.
    /// Uses Premiere-style "nice numbers" algorithm for comfortable label spacing.
    /// </summary>
    /// <param name="pixelsPerDay">Current zoom level</param>
    /// <param name="targetPixelGap">Desired pixels between major ticks (80-150 recommended)</param>
    public static TimeRulerConfig Calculate(double pixelsPerDay, double targetPixelGap = 120)
    {
        // Calculate the time interval that fits the target pixel gap
        var targetIntervalDays = targetPixelGap / pixelsPerDay;

        // Snap to "nice" intervals based on the scale
        return targetIntervalDays switch
        {
            // Very zoomed in: show days/weeks
            < 3 => new TimeRulerConfig
            {
                MajorTickIntervalDays = 1,
                MinorTicksPerMajor = 4, // 6-hour marks
                LabelFormat = "ddd d",
                IntervalType = TimeRulerIntervalType.Day
            },
            < 10 => new TimeRulerConfig
            {
                MajorTickIntervalDays = 7,
                MinorTicksPerMajor = 7, // daily marks
                LabelFormat = "MMM d",
                IntervalType = TimeRulerIntervalType.Week
            },
            // Medium zoom: show weeks/months
            < 35 => new TimeRulerConfig
            {
                MajorTickIntervalDays = 7,
                MinorTicksPerMajor = 7,
                LabelFormat = "MMM d",
                IntervalType = TimeRulerIntervalType.Week
            },
            < 60 => new TimeRulerConfig
            {
                MajorTickIntervalDays = 30,
                MinorTicksPerMajor = 4, // roughly weekly
                LabelFormat = "MMM yyyy",
                IntervalType = TimeRulerIntervalType.Month
            },
            // Zoomed out: show months/years
            < 180 => new TimeRulerConfig
            {
                MajorTickIntervalDays = 30,
                MinorTicksPerMajor = 2, // bi-weekly
                LabelFormat = "MMM yyyy",
                IntervalType = TimeRulerIntervalType.Month
            },
            < 400 => new TimeRulerConfig
            {
                MajorTickIntervalDays = 90, // quarterly
                MinorTicksPerMajor = 3,
                LabelFormat = "MMM yyyy",
                IntervalType = TimeRulerIntervalType.Quarter
            },
            // Very zoomed out: show years
            < 730 => new TimeRulerConfig
            {
                MajorTickIntervalDays = 365,
                MinorTicksPerMajor = 4, // quarterly
                LabelFormat = "yyyy",
                IntervalType = TimeRulerIntervalType.Year
            },
            _ => new TimeRulerConfig
            {
                MajorTickIntervalDays = 365 * 5, // 5-year intervals
                MinorTicksPerMajor = 5, // yearly
                LabelFormat = "yyyy",
                IntervalType = TimeRulerIntervalType.FiveYear
            }
        };
    }

    /// <summary>
    /// Generates tick marks for the visible viewport.
    /// </summary>
    public IEnumerable<TimeRulerTick> GenerateTicks(TimelineCoordinateConverter converter)
    {
        var ticks = new List<TimeRulerTick>();
        var minorIntervalDays = MajorTickIntervalDays / MinorTicksPerMajor;

        // Find the first major tick before or at the visible start
        var firstTickDate = GetAlignedDate(converter.VisibleStartDate);

        // Generate ticks from before visible area to after (with buffer)
        var currentDate = firstTickDate.AddDays(-MajorTickIntervalDays);
        var endDate = converter.VisibleEndDate.AddDays(MajorTickIntervalDays);

        int minorCounter = 0;
        while (currentDate <= endDate)
        {
            var screenX = converter.DateToScreen(currentDate);
            var isMajor = minorCounter % MinorTicksPerMajor == 0;

            ticks.Add(new TimeRulerTick
            {
                Date = currentDate,
                ScreenX = screenX,
                IsMajor = isMajor,
                Label = isMajor ? currentDate.ToString(LabelFormat) : null
            });

            currentDate = currentDate.AddDays(minorIntervalDays);
            minorCounter++;
        }

        return ticks;
    }

    /// <summary>
    /// Aligns a date to the nearest interval boundary.
    /// </summary>
    private DateTime GetAlignedDate(DateTime date)
    {
        return IntervalType switch
        {
            TimeRulerIntervalType.Day => date.Date,
            TimeRulerIntervalType.Week => date.Date.AddDays(-(int)date.DayOfWeek),
            TimeRulerIntervalType.Month => new DateTime(date.Year, date.Month, 1),
            TimeRulerIntervalType.Quarter => new DateTime(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1),
            TimeRulerIntervalType.Year => new DateTime(date.Year, 1, 1),
            TimeRulerIntervalType.FiveYear => new DateTime((date.Year / 5) * 5, 1, 1),
            _ => date.Date
        };
    }
}

/// <summary>
/// Type of interval for time ruler formatting.
/// </summary>
public enum TimeRulerIntervalType
{
    Day,
    Week,
    Month,
    Quarter,
    Year,
    FiveYear
}

/// <summary>
/// Represents a single tick mark on the time ruler.
/// </summary>
public class TimeRulerTick
{
    /// <summary>
    /// The date this tick represents.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Screen X position in pixels.
    /// </summary>
    public double ScreenX { get; set; }

    /// <summary>
    /// Whether this is a major tick (with label) or minor tick.
    /// </summary>
    public bool IsMajor { get; set; }

    /// <summary>
    /// Label text for major ticks, null for minor ticks.
    /// </summary>
    public string? Label { get; set; }
}

/// <summary>
/// Handles zoom operations with anchor point preservation.
/// Like Premiere, zooming can be anchored to cursor position, center, or playhead.
/// </summary>
public static class ZoomHelper
{
    /// <summary>
    /// Zooms the viewport while keeping a specific date at the same screen position.
    /// This is the Premiere-style zoom anchoring behavior.
    /// </summary>
    /// <param name="viewport">Current viewport</param>
    /// <param name="anchorDate">Date to keep at same screen position</param>
    /// <param name="newZoomLevel">New zoom level</param>
    public static void ZoomCenteredOn(TimelineViewport viewport, DateTime anchorDate, ZoomLevel newZoomLevel)
    {
        // Calculate where anchor currently sits as fraction of viewport (0 = left edge, 1 = right edge)
        var anchorFraction = (anchorDate - viewport.StartDate).TotalDays / viewport.VisibleDays;

        // Clamp to valid range
        anchorFraction = Math.Max(0, Math.Min(1, anchorFraction));

        // Get new pixels per day
        var newPixelsPerDay = TimelineScale.GetPixelsPerDay(newZoomLevel);
        var newVisibleDays = viewport.ViewportWidth / newPixelsPerDay;

        // Calculate new scroll offset to keep anchor at same screen position
        var newStartDate = anchorDate.AddDays(-anchorFraction * newVisibleDays);

        // Update viewport
        viewport.ZoomLevel = newZoomLevel;
        viewport.PixelsPerDay = newPixelsPerDay;
        viewport.StartDate = newStartDate;
        viewport.EndDate = newStartDate.AddDays(newVisibleDays);
        viewport.CenterDate = newStartDate.AddDays(newVisibleDays / 2);
    }

    /// <summary>
    /// Zooms the viewport while keeping the center date fixed.
    /// </summary>
    public static void ZoomCenteredOnCenter(TimelineViewport viewport, ZoomLevel newZoomLevel)
    {
        ZoomCenteredOn(viewport, viewport.CenterDate, newZoomLevel);
    }

    /// <summary>
    /// Zooms the viewport while keeping a screen position fixed.
    /// </summary>
    public static void ZoomCenteredOnScreenPosition(TimelineViewport viewport, double screenX, ZoomLevel newZoomLevel)
    {
        var anchorDate = viewport.PixelToDate(screenX);
        ZoomCenteredOn(viewport, anchorDate, newZoomLevel);
    }
}
