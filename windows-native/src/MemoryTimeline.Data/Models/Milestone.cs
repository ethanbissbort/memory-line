using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Type of milestone marker for visual display.
/// </summary>
public enum MilestoneType
{
    /// <summary>Diamond shape - standard milestone</summary>
    Standard = 0,

    /// <summary>Star shape - checkpoint/achievement</summary>
    Checkpoint = 1,

    /// <summary>Triangle down - kickoff/start event</summary>
    Kickoff = 2,

    /// <summary>Arrow right - signoff/completion event</summary>
    Signoff = 3
}

/// <summary>
/// Represents a significant date/event marker on the timeline.
/// </summary>
[Table("milestones")]
public class Milestone
{
    [Key]
    [Column("milestone_id")]
    public string MilestoneId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(200)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column("date")]
    public DateTime Date { get; set; }

    [Column("type")]
    public MilestoneType Type { get; set; } = MilestoneType.Standard;

    [Column("linked_era_id")]
    public string? LinkedEraId { get; set; }

    [MaxLength(9)]
    [Column("color_override")]
    public string? ColorOverride { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    [ForeignKey("LinkedEraId")]
    public virtual Era? LinkedEra { get; set; }

    /// <summary>
    /// Gets the display color (override if set, otherwise based on type).
    /// </summary>
    [NotMapped]
    public string DisplayColor => ColorOverride ?? GetDefaultColorForType(Type);

    private static string GetDefaultColorForType(MilestoneType type) => type switch
    {
        MilestoneType.Standard => "#0078D4",   // Blue
        MilestoneType.Checkpoint => "#FF8C00", // Orange
        MilestoneType.Kickoff => "#E74856",    // Red
        MilestoneType.Signoff => "#107C10",    // Green
        _ => "#808080"
    };
}
