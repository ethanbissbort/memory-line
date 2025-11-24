using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents a tag for categorizing events.
/// </summary>
[Table("tags")]
public class Tag
{
    [Key]
    [Column("tag_id")]
    public string TagId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(100)]
    [Column("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<EventTag> EventTags { get; set; } = new List<EventTag>();
}

/// <summary>
/// Junction table for Event-Tag many-to-many relationship.
/// </summary>
[Table("event_tags")]
public class EventTag
{
    [Required]
    [Column("event_id")]
    public string EventId { get; set; } = string.Empty;

    [Required]
    [Column("tag_id")]
    public string TagId { get; set; } = string.Empty;

    [Column("confidence_score")]
    public double ConfidenceScore { get; set; } = 1.0;

    [Column("is_manual")]
    public bool IsManual { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Event Event { get; set; } = null!;
    public virtual Tag Tag { get; set; } = null!;
}
