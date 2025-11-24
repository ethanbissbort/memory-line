using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Core.Models;

/// <summary>
/// Represents the layout information for rendering an event on the timeline.
/// </summary>
public class EventLayout
{
    /// <summary>
    /// Gets or sets the event being laid out.
    /// </summary>
    public Event Event { get; set; } = null!;

    /// <summary>
    /// Gets or sets the X position (horizontal, time-based) in pixels.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the Y position (vertical, stacking) in pixels.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Gets or sets the width in pixels.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the height in pixels.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets the track/layer number for vertical stacking.
    /// </summary>
    public int Track { get; set; }

    /// <summary>
    /// Gets or sets whether this event is currently visible in the viewport.
    /// </summary>
    public bool IsVisible { get; set; }

    /// <summary>
    /// Gets or sets the opacity for fade-in/fade-out animations.
    /// </summary>
    public double Opacity { get; set; } = 1.0;
}

/// <summary>
/// Helper class for calculating event layouts on the timeline.
/// </summary>
public static class EventLayoutEngine
{
    /// <summary>
    /// Calculates the layout for a list of events, handling overlaps by stacking.
    /// </summary>
    public static List<EventLayout> CalculateLayout(
        IEnumerable<Event> events,
        ZoomLevel zoom,
        DateTime referenceDate,
        double viewportStart,
        double viewportEnd,
        double trackHeight = 30.0)
    {
        var layouts = new List<EventLayout>();
        var tracks = new List<List<EventLayout>>();

        foreach (var evt in events.OrderBy(e => e.StartDate))
        {
            var x = TimelineScale.GetPixelPosition(evt.StartDate, referenceDate, zoom);
            var width = TimelineScale.GetEventWidth(evt.StartDate, evt.EndDate, zoom);

            // Check if event is visible in current viewport
            var isVisible = (x + width >= viewportStart) && (x <= viewportEnd);

            // Find first track where event doesn't overlap
            var trackIndex = FindAvailableTrack(tracks, x, width);

            var layout = new EventLayout
            {
                Event = evt,
                X = x,
                Y = trackIndex * trackHeight,
                Width = width,
                Height = 24.0, // Standard event height
                Track = trackIndex,
                IsVisible = isVisible
            };

            layouts.Add(layout);

            // Add to track for overlap detection
            if (trackIndex >= tracks.Count)
            {
                tracks.Add(new List<EventLayout>());
            }
            tracks[trackIndex].Add(layout);
        }

        return layouts;
    }

    /// <summary>
    /// Finds the first available track where an event doesn't overlap with existing events.
    /// </summary>
    private static int FindAvailableTrack(List<List<EventLayout>> tracks, double x, double width)
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            if (!HasOverlap(tracks[i], x, width))
            {
                return i;
            }
        }

        // No available track, create new one
        return tracks.Count;
    }

    /// <summary>
    /// Checks if there's an overlap between the given position and any events in the track.
    /// </summary>
    private static bool HasOverlap(List<EventLayout> track, double x, double width)
    {
        var eventEnd = x + width;

        foreach (var layout in track)
        {
            var existingEnd = layout.X + layout.Width;

            // Check for overlap (with small buffer to prevent touching events)
            const double buffer = 4.0; // 4 pixels between events
            if (x < existingEnd + buffer && eventEnd > layout.X - buffer)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Filters layouts to only those visible in the current viewport.
    /// </summary>
    public static List<EventLayout> GetVisibleLayouts(
        List<EventLayout> allLayouts,
        double viewportStart,
        double viewportEnd)
    {
        return allLayouts
            .Where(layout =>
            {
                var eventEnd = layout.X + layout.Width;
                return (eventEnd >= viewportStart) && (layout.X <= viewportEnd);
            })
            .ToList();
    }

    /// <summary>
    /// Calculates the total height needed for all tracks.
    /// </summary>
    public static double CalculateTotalHeight(List<EventLayout> layouts, double trackHeight = 30.0)
    {
        if (!layouts.Any())
            return trackHeight; // Minimum height

        var maxTrack = layouts.Max(l => l.Track);
        return (maxTrack + 1) * trackHeight;
    }
}
