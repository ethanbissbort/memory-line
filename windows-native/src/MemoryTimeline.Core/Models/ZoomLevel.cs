namespace MemoryTimeline.Core.Models;

/// <summary>
/// Represents the zoom level for the timeline view.
/// </summary>
public enum ZoomLevel
{
    /// <summary>
    /// Year view - shows years at a glance (1 pixel ≈ 1 month)
    /// Recommended for viewing long time spans (decades)
    /// </summary>
    Year = 0,

    /// <summary>
    /// Month view - shows months in detail (1 pixel ≈ 1 day)
    /// Recommended for viewing annual timelines
    /// </summary>
    Month = 1,

    /// <summary>
    /// Week view - shows weeks in detail (1 pixel ≈ 4 hours)
    /// Recommended for viewing monthly timelines
    /// </summary>
    Week = 2,

    /// <summary>
    /// Day view - shows days in detail (1 pixel ≈ 30 minutes)
    /// Recommended for viewing weekly timelines
    /// </summary>
    Day = 3
}

/// <summary>
/// Helper class for timeline scaling calculations.
/// </summary>
public static class TimelineScale
{
    /// <summary>
    /// Gets the number of pixels per day for the given zoom level.
    /// </summary>
    public static double GetPixelsPerDay(ZoomLevel zoom)
    {
        return zoom switch
        {
            ZoomLevel.Year => 0.1,      // ~30 pixels per year (0.1 * 365)
            ZoomLevel.Month => 3.0,     // ~90 pixels per month (3.0 * 30)
            ZoomLevel.Week => 50.0,     // ~350 pixels per week (50.0 * 7)
            ZoomLevel.Day => 800.0,     // ~800 pixels per day
            _ => 1.0
        };
    }

    /// <summary>
    /// Gets the number of days visible in a viewport of the given width.
    /// </summary>
    public static double GetVisibleDays(ZoomLevel zoom, double viewportWidth)
    {
        var pixelsPerDay = GetPixelsPerDay(zoom);
        return viewportWidth / pixelsPerDay;
    }

    /// <summary>
    /// Gets the pixel position for a given date at the specified zoom level.
    /// </summary>
    public static double GetPixelPosition(DateTime date, DateTime referenceDate, ZoomLevel zoom)
    {
        var daysDifference = (date - referenceDate).TotalDays;
        var pixelsPerDay = GetPixelsPerDay(zoom);
        return daysDifference * pixelsPerDay;
    }

    /// <summary>
    /// Gets the date for a given pixel position at the specified zoom level.
    /// </summary>
    public static DateTime GetDateFromPixel(double pixelPosition, DateTime referenceDate, ZoomLevel zoom)
    {
        var pixelsPerDay = GetPixelsPerDay(zoom);
        var days = pixelPosition / pixelsPerDay;
        return referenceDate.AddDays(days);
    }

    /// <summary>
    /// Gets the pixel width for an event at the given zoom level.
    /// </summary>
    public static double GetEventWidth(DateTime startDate, DateTime? endDate, ZoomLevel zoom)
    {
        if (!endDate.HasValue)
        {
            // Single-point event: minimum width
            return GetMinimumEventWidth(zoom);
        }

        var duration = (endDate.Value - startDate).TotalDays;
        var pixelsPerDay = GetPixelsPerDay(zoom);
        var width = duration * pixelsPerDay;

        // Ensure minimum width for visibility
        return Math.Max(width, GetMinimumEventWidth(zoom));
    }

    /// <summary>
    /// Gets the minimum event width in pixels for the given zoom level.
    /// </summary>
    public static double GetMinimumEventWidth(ZoomLevel zoom)
    {
        return zoom switch
        {
            ZoomLevel.Year => 2.0,      // Very small dots
            ZoomLevel.Month => 4.0,     // Small dots
            ZoomLevel.Week => 8.0,      // Medium bubbles
            ZoomLevel.Day => 24.0,      // Full bubbles
            _ => 4.0
        };
    }

    /// <summary>
    /// Gets the grid interval (in days) for drawing tick marks at the given zoom level.
    /// </summary>
    public static int GetGridInterval(ZoomLevel zoom)
    {
        return zoom switch
        {
            ZoomLevel.Year => 365,      // Yearly ticks
            ZoomLevel.Month => 30,      // Monthly ticks
            ZoomLevel.Week => 7,        // Weekly ticks
            ZoomLevel.Day => 1,         // Daily ticks
            _ => 30
        };
    }

    /// <summary>
    /// Gets the next zoom level when zooming in.
    /// </summary>
    public static ZoomLevel ZoomIn(ZoomLevel current)
    {
        return current < ZoomLevel.Day ? current + 1 : current;
    }

    /// <summary>
    /// Gets the previous zoom level when zooming out.
    /// </summary>
    public static ZoomLevel ZoomOut(ZoomLevel current)
    {
        return current > ZoomLevel.Year ? current - 1 : current;
    }

    /// <summary>
    /// Checks if the given zoom level can zoom in further.
    /// </summary>
    public static bool CanZoomIn(ZoomLevel current)
    {
        return current < ZoomLevel.Day;
    }

    /// <summary>
    /// Checks if the given zoom level can zoom out further.
    /// </summary>
    public static bool CanZoomOut(ZoomLevel current)
    {
        return current > ZoomLevel.Year;
    }

    /// <summary>
    /// Gets a human-readable name for the zoom level.
    /// </summary>
    public static string GetZoomLevelName(ZoomLevel zoom)
    {
        return zoom switch
        {
            ZoomLevel.Year => "Year View",
            ZoomLevel.Month => "Month View",
            ZoomLevel.Week => "Week View",
            ZoomLevel.Day => "Day View",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Gets a description of what is visible at the given zoom level.
    /// </summary>
    public static string GetZoomLevelDescription(ZoomLevel zoom)
    {
        return zoom switch
        {
            ZoomLevel.Year => "View decades at a glance",
            ZoomLevel.Month => "View years in detail",
            ZoomLevel.Week => "View months in detail",
            ZoomLevel.Day => "View weeks in detail",
            _ => ""
        };
    }
}
