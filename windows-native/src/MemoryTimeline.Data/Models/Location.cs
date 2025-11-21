using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents a location mentioned in events.
/// </summary>
[Table("locations")]
public class Location
{
    [Key]
    [Column("location_id")]
    public string LocationId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(200)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<EventLocation> EventLocations { get; set; } = new List<EventLocation>();
}

/// <summary>
/// Junction table for Event-Location many-to-many relationship.
/// </summary>
[Table("event_locations")]
public class EventLocation
{
    [Required]
    [Column("event_id")]
    public string EventId { get; set; } = string.Empty;

    [Required]
    [Column("location_id")]
    public string LocationId { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Event Event { get; set; } = null!;
    public virtual Location Location { get; set; } = null!;
}
