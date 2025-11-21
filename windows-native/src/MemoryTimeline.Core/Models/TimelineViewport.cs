namespace MemoryTimeline.Core.Models;

/// <summary>
/// Zoom level for timeline visualization.
/// </summary>
public enum ZoomLevel
{
    Year,
    Month,
    Week,
    Day
}

/// <summary>
/// Timeline viewport representing the visible area.
/// </summary>
public class TimelineViewport
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public ZoomLevel ZoomLevel { get; set; }
    public double PixelsPerDay { get; set; }
    public int ViewportWidth { get; set; }
    public int ViewportHeight { get; set; }

    public TimeSpan VisibleTimeSpan => EndDate - StartDate;
    public int TotalDays => (int)VisibleTimeSpan.TotalDays;

    /// <summary>
    /// Converts a date to a pixel position in the viewport.
    /// </summary>
    public double DateToPixel(DateTime date)
    {
        if (date < StartDate) return 0;
        if (date > EndDate) return ViewportWidth;

        var days = (date - StartDate).TotalDays;
        return days * PixelsPerDay;
    }

    /// <summary>
    /// Converts a pixel position to a date.
    /// </summary>
    public DateTime PixelToDate(double pixel)
    {
        var days = pixel / PixelsPerDay;
        return StartDate.AddDays(days);
    }

    /// <summary>
    /// Checks if a date is visible in the viewport.
    /// </summary>
    public bool IsDateVisible(DateTime date)
    {
        return date >= StartDate && date <= EndDate;
    }
}

/// <summary>
/// Zoom configuration for different zoom levels.
/// </summary>
public static class ZoomConfig
{
    public static double GetPixelsPerDay(ZoomLevel level)
    {
        return level switch
        {
            ZoomLevel.Year => 0.5,
            ZoomLevel.Month => 5.0,
            ZoomLevel.Week => 20.0,
            ZoomLevel.Day => 100.0,
            _ => 5.0
        };
    }

    public static TimeSpan GetDefaultTimeSpan(ZoomLevel level)
    {
        return level switch
        {
            ZoomLevel.Year => TimeSpan.FromDays(365 * 10), // 10 years
            ZoomLevel.Month => TimeSpan.FromDays(365 * 2), // 2 years
            ZoomLevel.Week => TimeSpan.FromDays(180), // 6 months
            ZoomLevel.Day => TimeSpan.FromDays(30), // 1 month
            _ => TimeSpan.FromDays(365)
        };
    }

    public static string GetDisplayName(ZoomLevel level)
    {
        return level switch
        {
            ZoomLevel.Year => "Year",
            ZoomLevel.Month => "Month",
            ZoomLevel.Week => "Week",
            ZoomLevel.Day => "Day",
            _ => "Month"
        };
    }
}
