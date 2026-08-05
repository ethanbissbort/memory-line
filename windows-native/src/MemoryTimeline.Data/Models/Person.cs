using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents a person mentioned in events.
/// </summary>
[Table("people")]
public class Person
{
    [Key]
    [Column("person_id")]
    public string PersonId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(200)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional nickname for the person.
    /// </summary>
    [MaxLength(100)]
    [Column("nickname")]
    public string? Nickname { get; set; }

    /// <summary>
    /// Relationship to the user (e.g. "sister", "coworker").
    /// </summary>
    [MaxLength(100)]
    [Column("relationship")]
    public string? Relationship { get; set; }

    /// <summary>
    /// Email address.
    /// </summary>
    [MaxLength(320)]
    [Column("email")]
    public string? Email { get; set; }

    /// <summary>
    /// Phone number.
    /// </summary>
    [MaxLength(50)]
    [Column("phone")]
    public string? Phone { get; set; }

    /// <summary>
    /// Birthday, if known.
    /// </summary>
    [Column("birthday")]
    public DateTime? Birthday { get; set; }

    /// <summary>
    /// Company or organization.
    /// </summary>
    [MaxLength(200)]
    [Column("company")]
    public string? Company { get; set; }

    /// <summary>
    /// Free-form notes about the person.
    /// </summary>
    [Column("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Path to a local photo file for the person.
    /// </summary>
    [Column("photo_path")]
    public string? PhotoPath { get; set; }

    /// <summary>
    /// Avatar background color as a hex string (e.g. "#FF8C00").
    /// </summary>
    [MaxLength(9)]
    [Column("avatar_color")]
    public string? AvatarColor { get; set; }

    /// <summary>
    /// Whether the person is marked as a favorite.
    /// </summary>
    [Column("is_favorite")]
    public bool IsFavorite { get; set; }

    /// <summary>
    /// Date the user first met the person, if known.
    /// </summary>
    [Column("first_met_date")]
    public DateTime? FirstMetDate { get; set; }

    /// <summary>
    /// Timestamp of the last modification (stamped automatically on save).
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<EventPerson> EventPeople { get; set; } = new List<EventPerson>();
}

/// <summary>
/// Junction table for Event-Person many-to-many relationship.
/// </summary>
[Table("event_people")]
public class EventPerson
{
    [Required]
    [Column("event_id")]
    public string EventId { get; set; } = string.Empty;

    [Required]
    [Column("person_id")]
    public string PersonId { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Event Event { get; set; } = null!;
    public virtual Person Person { get; set; } = null!;
}
