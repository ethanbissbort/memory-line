using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents a category for eras (Education, Employment, Relationship, etc.)
/// </summary>
[Table("era_categories")]
public class EraCategory
{
    [Key]
    [Column("category_id")]
    public string CategoryId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(100)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(10)]
    [Column("icon_glyph")]
    public string? IconGlyph { get; set; }

    [Required]
    [MaxLength(9)]
    [Column("default_color")]
    public string DefaultColor { get; set; } = "#808080";

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("is_visible")]
    public bool IsVisible { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public virtual ICollection<Era> Eras { get; set; } = new List<Era>();
}

/// <summary>
/// Default era categories to seed the database.
/// </summary>
public static class DefaultEraCategories
{
    public static readonly List<EraCategory> All = new()
    {
        new() { CategoryId = "cat-education", Name = "Education", DefaultColor = "#0078D4", IconGlyph = "\uE7BE", SortOrder = 1 },
        new() { CategoryId = "cat-employment", Name = "Employment", DefaultColor = "#107C10", IconGlyph = "\uE821", SortOrder = 2 },
        new() { CategoryId = "cat-relationship", Name = "Relationship", DefaultColor = "#E74856", IconGlyph = "\uEB51", SortOrder = 3 },
        new() { CategoryId = "cat-residence", Name = "Residence", DefaultColor = "#8764B8", IconGlyph = "\uE80F", SortOrder = 4 },
        new() { CategoryId = "cat-health", Name = "Health", DefaultColor = "#00B7C3", IconGlyph = "\uE95E", SortOrder = 5 },
        new() { CategoryId = "cat-project", Name = "Project", DefaultColor = "#FF8C00", IconGlyph = "\uE8F1", SortOrder = 6 },
        new() { CategoryId = "cat-other", Name = "Other", DefaultColor = "#6B6B6B", IconGlyph = "\uE7C3", SortOrder = 7 },
    };
}
