using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Core.Models;

/// <summary>
/// Represents a multi-dimensional search filter for faceted search.
/// </summary>
public class SearchFilter
{
    public string? SearchTerm { get; set; }
    public List<string> Categories { get; set; } = new();
    public List<string> TagIds { get; set; } = new();
    public List<string> PersonIds { get; set; } = new();
    public List<string> LocationIds { get; set; } = new();
    public List<string> EraIds { get; set; } = new();
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool? HasAudio { get; set; }
    public bool? HasTranscript { get; set; }
    public double? MinConfidence { get; set; }
    public string SortBy { get; set; } = SearchSortOptions.DateDesc;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(SearchTerm) ||
        Categories.Any() ||
        TagIds.Any() ||
        PersonIds.Any() ||
        LocationIds.Any() ||
        EraIds.Any() ||
        StartDate.HasValue ||
        EndDate.HasValue ||
        HasAudio.HasValue ||
        HasTranscript.HasValue ||
        MinConfidence.HasValue;

    public SearchFilter Clone() => new SearchFilter
    {
        SearchTerm = SearchTerm,
        Categories = new List<string>(Categories),
        TagIds = new List<string>(TagIds),
        PersonIds = new List<string>(PersonIds),
        LocationIds = new List<string>(LocationIds),
        EraIds = new List<string>(EraIds),
        StartDate = StartDate,
        EndDate = EndDate,
        HasAudio = HasAudio,
        HasTranscript = HasTranscript,
        MinConfidence = MinConfidence,
        SortBy = SortBy,
        PageNumber = PageNumber,
        PageSize = PageSize
    };
}

/// <summary>
/// Represents paginated search results with facet counts.
/// </summary>
public class SearchResults
{
    public List<Event> Events { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public SearchFacets Facets { get; set; } = new();
    public TimeSpan SearchDuration { get; set; }
}

/// <summary>
/// Facet counts for filtering options.
/// </summary>
public class SearchFacets
{
    public Dictionary<string, int> Categories { get; set; } = new();
    public Dictionary<string, FacetItem> Tags { get; set; } = new();
    public Dictionary<string, FacetItem> People { get; set; } = new();
    public Dictionary<string, FacetItem> Locations { get; set; } = new();
    public Dictionary<string, FacetItem> Eras { get; set; } = new();
    public int WithAudioCount { get; set; }
    public int WithTranscriptCount { get; set; }
    public DateTime? EarliestDate { get; set; }
    public DateTime? LatestDate { get; set; }
}

/// <summary>
/// Represents a facet item with name and count.
/// </summary>
public class FacetItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public string? Color { get; set; }
}

/// <summary>
/// Represents an autocomplete suggestion.
/// </summary>
public class AutocompleteSuggestion
{
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "event", "tag", "person", "location", "era"
    public string? Id { get; set; }
    public double Score { get; set; }
    public string? Context { get; set; }
}

/// <summary>
/// Available page size options.
/// </summary>
public static class PageSizeOptions
{
    public static readonly int[] Options = { 10, 25, 50, 100, 200 };
    public const int Default = 25;
}
