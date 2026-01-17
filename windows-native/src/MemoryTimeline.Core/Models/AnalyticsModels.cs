namespace MemoryTimeline.Core.Models;

/// <summary>
/// Comprehensive analytics data for the timeline.
/// </summary>
public class TimelineAnalytics
{
    // Overview statistics
    public int TotalEvents { get; set; }
    public int TotalEras { get; set; }
    public int TotalTags { get; set; }
    public int TotalPeople { get; set; }
    public int TotalLocations { get; set; }
    public int EventsWithAudio { get; set; }
    public int EventsWithTranscript { get; set; }
    public DateTime? EarliestEventDate { get; set; }
    public DateTime? LatestEventDate { get; set; }
    public TimeSpan? TimelineSpan { get; set; }

    // Category distribution
    public List<CategoryDistribution> CategoryDistribution { get; set; } = new();

    // Timeline density (events per time period)
    public List<TimelineDensityPoint> DailyDensity { get; set; } = new();
    public List<TimelineDensityPoint> MonthlyDensity { get; set; } = new();
    public List<TimelineDensityPoint> YearlyDensity { get; set; } = new();

    // Tag cloud data
    public List<TagCloudItem> TagCloud { get; set; } = new();

    // People network
    public PeopleNetwork PeopleNetwork { get; set; } = new();

    // Activity heatmap (by day of week and hour, or by month)
    public List<ActivityHeatmapCell> ActivityByDayOfWeek { get; set; } = new();
    public List<ActivityHeatmapCell> ActivityByMonth { get; set; } = new();
    public List<ActivityHeatmapCell> ActivityByYear { get; set; } = new();

    // Trends
    public List<TrendPoint> EventCreationTrend { get; set; } = new();
    public List<TrendPoint> CategoryTrends { get; set; } = new();
}

/// <summary>
/// Category distribution data for pie/bar charts.
/// </summary>
public class CategoryDistribution
{
    public string Category { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
    public string Color { get; set; } = "#808080";
}

/// <summary>
/// Timeline density data point.
/// </summary>
public class TimelineDensityPoint
{
    public DateTime Date { get; set; }
    public string Label { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public double NormalizedValue { get; set; } // 0-1 scale
}

/// <summary>
/// Tag cloud item with size weight.
/// </summary>
public class TagCloudItem
{
    public string TagId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#808080";
    public int Count { get; set; }
    public double Weight { get; set; } // 1-5 scale for font size
    public int FontSize { get; set; } // Computed font size in pixels
}

/// <summary>
/// People network for graph visualization.
/// </summary>
public class PeopleNetwork
{
    public List<PersonNode> Nodes { get; set; } = new();
    public List<PersonEdge> Edges { get; set; } = new();
}

/// <summary>
/// Person node in the network graph.
/// </summary>
public class PersonNode
{
    public string PersonId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Relationship { get; set; }
    public int EventCount { get; set; }
    public double Size { get; set; } // Node size based on event count
    public string Color { get; set; } = "#4A90D9";
}

/// <summary>
/// Edge connecting two people in the network.
/// </summary>
public class PersonEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public int SharedEventCount { get; set; }
    public double Weight { get; set; } // Edge thickness
    public List<string> SharedEventIds { get; set; } = new();
}

/// <summary>
/// Activity heatmap cell.
/// </summary>
public class ActivityHeatmapCell
{
    public int X { get; set; } // Column index (e.g., day of week 0-6, or month 0-11)
    public int Y { get; set; } // Row index (e.g., hour 0-23, or year)
    public string XLabel { get; set; } = string.Empty;
    public string YLabel { get; set; } = string.Empty;
    public int Value { get; set; }
    public double Intensity { get; set; } // 0-1 normalized
    public string Color { get; set; } = "#E8E8E8";
}

/// <summary>
/// Trend data point for line charts.
/// </summary>
public class TrendPoint
{
    public DateTime Date { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Category { get; set; }
    public double Value { get; set; }
}

/// <summary>
/// Color palette for categories and visualizations.
/// </summary>
public static class AnalyticsColors
{
    public static readonly Dictionary<string, string> CategoryColors = new()
    {
        { "milestone", "#FF6B6B" },
        { "work", "#4ECDC4" },
        { "education", "#45B7D1" },
        { "relationship", "#F7DC6F" },
        { "travel", "#82E0AA" },
        { "achievement", "#BB8FCE" },
        { "challenge", "#F8B739" },
        { "era", "#85929E" },
        { "other", "#AEB6BF" }
    };

    public static readonly string[] HeatmapGradient =
    {
        "#EBEDF0", "#9BE9A8", "#40C463", "#30A14E", "#216E39"
    };

    public static string GetCategoryColor(string category) =>
        CategoryColors.TryGetValue(category.ToLowerInvariant(), out var color) ? color : "#AEB6BF";

    public static string GetHeatmapColor(double intensity) =>
        intensity switch
        {
            0 => HeatmapGradient[0],
            < 0.25 => HeatmapGradient[1],
            < 0.5 => HeatmapGradient[2],
            < 0.75 => HeatmapGradient[3],
            _ => HeatmapGradient[4]
        };
}
