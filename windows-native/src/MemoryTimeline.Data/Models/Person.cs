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
