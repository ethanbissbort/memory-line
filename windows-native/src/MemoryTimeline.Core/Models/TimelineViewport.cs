namespace MemoryTimeline.Core.Models;

/// <summary>
/// Timeline viewport representing the visible area and rendering context.
/// </summary>
public class TimelineViewport
{
    /// <summary>
    /// Gets or sets the start date of the visible viewport.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Gets or sets the end date of the visible viewport.
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// Gets or sets the center date of the viewport (for zooming).
    /// </summary>
    public DateTime CenterDate { get; set; }

    /// <summary>
    /// Gets or sets the current zoom level.
    /// </summary>
    public ZoomLevel ZoomLevel { get; set; }

    /// <summary>
    /// Gets or sets the pixels per day at the current zoom level.
    /// </summary>
    public double PixelsPerDay { get; set; }

    /// <summary>
    /// Gets or sets the viewport width in pixels.
    /// </summary>
    public double ViewportWidth { get; set; }

    /// <summary>
    /// Gets or sets the viewport height in pixels.
    /// </summary>
    public double ViewportHeight { get; set; }

    /// <summary>
    /// Gets or sets the horizontal scroll position in pixels.
    /// </summary>
    public double ScrollPosition { get; set; }

    /// <summary>
    /// Gets the visible time span.
    /// </summary>
    public TimeSpan VisibleTimeSpan => EndDate - StartDate;

    /// <summary>
    /// Gets the total number of days visible.
    /// </summary>
    public double VisibleDays => VisibleTimeSpan.TotalDays;

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

    /// <summary>
    /// Checks if an event is visible in the viewport.
    /// </summary>
    public bool IsEventVisible(DateTime startDate, DateTime? endDate)
    {
        var eventEnd = endDate ?? startDate;
        return (eventEnd >= StartDate) && (startDate <= EndDate);
    }

    /// <summary>
    /// Creates a viewport centered on the given date.
    /// </summary>
    public static TimelineViewport CreateCentered(DateTime centerDate, ZoomLevel zoom, double viewportWidth)
    {
        var pixelsPerDay = TimelineScale.GetPixelsPerDay(zoom);
        var visibleDays = TimelineScale.GetVisibleDays(zoom, viewportWidth);
        var halfDays = visibleDays / 2.0;

        return new TimelineViewport
        {
            CenterDate = centerDate,
            StartDate = centerDate.AddDays(-halfDays),
            EndDate = centerDate.AddDays(halfDays),
            ZoomLevel = zoom,
            PixelsPerDay = pixelsPerDay,
            ViewportWidth = viewportWidth,
            ViewportHeight = 600, // Default height
            ScrollPosition = 0
        };
    }

    /// <summary>
    /// Updates the viewport after a zoom change, maintaining the center date.
    /// </summary>
    public void UpdateForZoom(ZoomLevel newZoom)
    {
        ZoomLevel = newZoom;
        PixelsPerDay = TimelineScale.GetPixelsPerDay(newZoom);

        var visibleDays = TimelineScale.GetVisibleDays(newZoom, ViewportWidth);
        var halfDays = visibleDays / 2.0;

        StartDate = CenterDate.AddDays(-halfDays);
        EndDate = CenterDate.AddDays(halfDays);
    }

    /// <summary>
    /// Pans the viewport by the given number of pixels.
    /// </summary>
    public void Pan(double deltaPixels)
    {
        var deltaDays = deltaPixels / PixelsPerDay;
        StartDate = StartDate.AddDays(-deltaDays);
        EndDate = EndDate.AddDays(-deltaDays);
        CenterDate = CenterDate.AddDays(-deltaDays);
        ScrollPosition += deltaPixels;
    }

    /// <summary>
    /// Sets the viewport to center on a specific date.
    /// </summary>
    public void CenterOn(DateTime date)
    {
        var visibleDays = TimelineScale.GetVisibleDays(ZoomLevel, ViewportWidth);
        var halfDays = visibleDays / 2.0;

        CenterDate = date;
        StartDate = date.AddDays(-halfDays);
        EndDate = date.AddDays(halfDays);
    }
}
