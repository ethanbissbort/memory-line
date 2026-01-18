using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Junction table for many-to-many relationship between Eras and Tags.
/// </summary>
[Table("era_tags")]
public class EraTag
{
    [Column("era_id")]
    public string EraId { get; set; } = string.Empty;

    [MaxLength(100)]
    [Column("tag")]
    public string Tag { get; set; } = string.Empty;

    // Navigation property
    [ForeignKey("EraId")]
    public virtual Era? Era { get; set; }
}
