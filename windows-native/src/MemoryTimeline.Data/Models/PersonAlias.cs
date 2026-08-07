using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// An alternative name for a person ("Bob" for "Robert"). Aliases feed
/// extraction (so Claude resolves nicknames to the canonical contact),
/// person matching, and merge history (a merged-away person's name is kept
/// as an alias of the survivor).
///
/// The alias column is unique case-insensitively across the WHOLE table
/// (NOCASE collation): a given spelling can belong to at most one person.
/// </summary>
[Table("person_aliases")]
public class PersonAlias
{
    [Key]
    [Column("alias_id")]
    public string AliasId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The person this alias belongs to (FK, cascade on delete).</summary>
    [Required]
    [Column("person_id")]
    public string PersonId { get; set; } = string.Empty;

    /// <summary>The alternative spelling/name.</summary>
    [Required]
    [MaxLength(200)]
    [Column("alias")]
    public string Alias { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Person Person { get; set; } = null!;
}
