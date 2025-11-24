namespace MemoryTimeline.Core.Models;

/// <summary>
/// Represents statistics about the timeline.
/// </summary>
public class TimelineStatistics
{
    /// <summary>
    /// Gets or sets the total number of events.
    /// </summary>
    public int TotalEvents { get; set; }

    /// <summary>
    /// Gets or sets the number of events currently visible in the viewport.
    /// </summary>
    public int VisibleEvents { get; set; }

    /// <summary>
    /// Gets or sets the date of the earliest event.
    /// </summary>
    public DateTime? EarliestDate { get; set; }

    /// <summary>
    /// Gets or sets the date of the latest event.
    /// </summary>
    public DateTime? LatestDate { get; set; }

    /// <summary>
    /// Gets or sets the total time span covered by events.
    /// </summary>
    public TimeSpan? TimeSpan { get; set; }

    /// <summary>
    /// Gets or sets event counts by category.
    /// </summary>
    public Dictionary<string, int> EventsByCategory { get; set; } = new();

    /// <summary>
    /// Gets or sets event counts by year.
    /// </summary>
    public Dictionary<int, int> EventsByYear { get; set; } = new();

    /// <summary>
    /// Gets or sets event counts by month (for current year).
    /// </summary>
    public Dictionary<int, int> EventsByMonth { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of eras.
    /// </summary>
    public int TotalEras { get; set; }

    /// <summary>
    /// Gets or sets the number of tags.
    /// </summary>
    public int TotalTags { get; set; }

    /// <summary>
    /// Gets or sets the average events per year.
    /// </summary>
    public double AverageEventsPerYear { get; set; }

    /// <summary>
    /// Gets or sets the busiest month (most events).
    /// </summary>
    public (int Year, int Month, int Count)? BusiestMonth { get; set; }

    /// <summary>
    /// Gets the total time span in years.
    /// </summary>
    public double TotalYears => TimeSpan?.TotalDays / 365.25 ?? 0;

    /// <summary>
    /// Gets the total time span in months.
    /// </summary>
    public double TotalMonths => TimeSpan?.TotalDays / 30.44 ?? 0;

    /// <summary>
    /// Calculates a summary description of the timeline.
    /// </summary>
    public string GetSummaryDescription()
    {
        if (TotalEvents == 0)
            return "No events in timeline";

        var years = (int)TotalYears;
        var monthDesc = years > 0
            ? $"{years} year{(years != 1 ? "s" : "")}"
            : $"{(int)TotalMonths} month{((int)TotalMonths != 1 ? "s" : "")}";

        return $"{TotalEvents} events spanning {monthDesc}";
    }
}
