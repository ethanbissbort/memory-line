using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents a saved search configuration for quick access to frequently used searches.
/// </summary>
[Table("saved_searches")]
public class SavedSearch
{
    [Key]
    [Column("saved_search_id")]
    public string SavedSearchId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(200)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("search_term")]
    public string? SearchTerm { get; set; }

    [Column("categories")]
    public string? Categories { get; set; } // JSON array of category strings

    [Column("tag_ids")]
    public string? TagIds { get; set; } // JSON array of tag IDs

    [Column("person_ids")]
    public string? PersonIds { get; set; } // JSON array of person IDs

    [Column("location_ids")]
    public string? LocationIds { get; set; } // JSON array of location IDs

    [Column("era_ids")]
    public string? EraIds { get; set; } // JSON array of era IDs

    [Column("start_date")]
    public DateTime? StartDate { get; set; }

    [Column("end_date")]
    public DateTime? EndDate { get; set; }

    [Column("has_audio")]
    public bool? HasAudio { get; set; }

    [Column("has_transcript")]
    public bool? HasTranscript { get; set; }

    [Column("min_confidence")]
    public double? MinConfidence { get; set; }

    [Column("sort_by")]
    [MaxLength(50)]
    public string SortBy { get; set; } = "date_desc";

    [Column("page_size")]
    public int PageSize { get; set; } = 25;

    [Column("is_favorite")]
    public bool IsFavorite { get; set; }

    [Column("use_count")]
    public int UseCount { get; set; }

    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Sort options for search results.
/// </summary>
public static class SearchSortOptions
{
    public const string DateDesc = "date_desc";
    public const string DateAsc = "date_asc";
    public const string TitleAsc = "title_asc";
    public const string TitleDesc = "title_desc";
    public const string Relevance = "relevance";
    public const string CreatedDesc = "created_desc";
    public const string CreatedAsc = "created_asc";

    public static readonly string[] AllOptions =
    {
        DateDesc, DateAsc, TitleAsc, TitleDesc, Relevance, CreatedDesc, CreatedAsc
    };

    public static string GetDisplayName(string option) => option switch
    {
        DateDesc => "Date (Newest First)",
        DateAsc => "Date (Oldest First)",
        TitleAsc => "Title (A-Z)",
        TitleDesc => "Title (Z-A)",
        Relevance => "Relevance",
        CreatedDesc => "Recently Added",
        CreatedAsc => "Oldest Added",
        _ => option
    };
}
