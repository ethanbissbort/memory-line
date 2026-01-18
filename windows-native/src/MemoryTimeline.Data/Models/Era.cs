using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents a life phase or period with color coding.
/// </summary>
[Table("eras")]
public class Era
{
    [Key]
    [Column("era_id")]
    public string EraId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(200)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    [Column("subtitle")]
    public string? Subtitle { get; set; }

    [Required]
    [Column("start_date")]
    public DateTime StartDate { get; set; }

    [Column("end_date")]
    public DateTime? EndDate { get; set; }

    [Column("category_id")]
    public string? CategoryId { get; set; }

    [Required]
    [MaxLength(9)]
    [Column("color_code")]
    public string ColorCode { get; set; } = "#808080";

    [MaxLength(9)]
    [Column("color_override")]
    public string? ColorOverride { get; set; }

    [Column("display_order")]
    public int DisplayOrder { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Alias property for backward compatibility
    [NotMapped]
    public string Color
    {
        get => ColorCode;
        set => ColorCode = value;
    }

    /// <summary>
    /// Returns true if the era is ongoing (no end date).
    /// </summary>
    [NotMapped]
    public bool IsOngoing => EndDate == null;

    /// <summary>
    /// Gets the effective display color (override if set, otherwise ColorCode or Category default).
    /// </summary>
    [NotMapped]
    public string EffectiveColor => ColorOverride ?? ColorCode;

    // Navigation properties
    [ForeignKey("CategoryId")]
    public virtual EraCategory? Category { get; set; }

    public virtual ICollection<Event> Events { get; set; } = new List<Event>();
    public virtual ICollection<EraTag> EraTags { get; set; } = new List<EraTag>();
    public virtual ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();
}
