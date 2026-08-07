using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents an unfinished editor session ("save as draft") for an event,
/// era, or person. The editor state is serialized as JSON in
/// <see cref="PayloadJson"/>.
/// </summary>
[Table("drafts")]
public class Draft
{
    [Key]
    [Column("draft_id")]
    public string DraftId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The kind of entity this draft is for; one of <see cref="DraftTypes"/>.
    /// </summary>
    [Required]
    [MaxLength(20)]
    [Column("draft_type")]
    public string DraftType { get; set; } = string.Empty;

    /// <summary>
    /// Display title of the draft (the entity's title/name at save time).
    /// </summary>
    [Required]
    [MaxLength(500)]
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The serialized editor payload (JSON).
    /// </summary>
    [Required]
    [Column("payload_json")]
    public string PayloadJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the last modification (stamped automatically on save).
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Well-known values for <see cref="Draft.DraftType"/>.
/// </summary>
public static class DraftTypes
{
    public const string Event = "event";
    public const string Era = "era";
    public const string Person = "person";
}
