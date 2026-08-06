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

    /// <summary>
    /// Geographic latitude in decimal degrees [-90, 90], or null while the
    /// place has no coordinates yet (F10). Filled from photo EXIF GPS, a
    /// manual map pin drop, or (opt-in) geocoding.
    /// </summary>
    [Column("latitude")]
    public double? Latitude { get; set; }

    /// <summary>Geographic longitude in decimal degrees [-180, 180], or null (see <see cref="Latitude"/>).</summary>
    [Column("longitude")]
    public double? Longitude { get; set; }

    /// <summary>Optional place classification (e.g. "city", "venue"); free text, currently informational.</summary>
    [Column("place_type")]
    [MaxLength(50)]
    public string? PlaceType { get; set; }

    /// <summary>
    /// The full display name the geocoder resolved (e.g. "Paris, Île-de-France,
    /// France"), kept alongside the user's own short <see cref="Name"/>.
    /// </summary>
    [Column("canonical_name")]
    [MaxLength(300)]
    public string? CanonicalName { get; set; }

    /// <summary>
    /// When the coordinates were produced by a geocoding lookup (UTC). Null for
    /// coordinates that came from photo EXIF or a manual pin drop - so this
    /// doubles as the "did this place name ever leave the machine?" marker.
    /// </summary>
    [Column("geocoded_at")]
    public DateTime? GeocodedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True when both coordinates are present.</summary>
    [NotMapped]
    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;

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
