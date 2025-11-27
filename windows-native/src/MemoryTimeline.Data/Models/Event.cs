using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents a timeline event.
/// </summary>
[Table("events")]
public class Event
{
    [Key]
    [Column("event_id")]
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(500)]
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Column("start_date")]
    public DateTime StartDate { get; set; }

    [Column("end_date")]
    public DateTime? EndDate { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("category")]
    [MaxLength(50)]
    public string? Category { get; set; }

    [Column("era_id")]
    public string? EraId { get; set; }

    [Column("audio_file_path")]
    public string? AudioFilePath { get; set; }

    [Column("raw_transcript")]
    public string? RawTranscript { get; set; }

    [Column("extraction_metadata")]
    public string? ExtractionMetadata { get; set; }

    [Column("location")]
    [MaxLength(200)]
    public string? Location { get; set; }

    [Column("confidence")]
    public double? Confidence { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Era? Era { get; set; }
    public virtual EventEmbedding? Embedding { get; set; }
    public virtual ICollection<EventTag> EventTags { get; set; } = new List<EventTag>();
    public virtual ICollection<EventPerson> EventPeople { get; set; } = new List<EventPerson>();
    public virtual ICollection<EventLocation> EventLocations { get; set; } = new List<EventLocation>();

    // Computed property for Tags collection
    [NotMapped]
    public IEnumerable<Tag> Tags => EventTags.Select(et => et.Tag);
}

/// <summary>
/// Event category enumeration.
/// </summary>
public static class EventCategory
{
    public const string Milestone = "milestone";
    public const string Work = "work";
    public const string Education = "education";
    public const string Relationship = "relationship";
    public const string Travel = "travel";
    public const string Achievement = "achievement";
    public const string Challenge = "challenge";
    public const string Era = "era";
    public const string Other = "other";

    public static readonly string[] AllCategories =
    {
        Milestone, Work, Education, Relationship,
        Travel, Achievement, Challenge, Era, Other
    };
}
