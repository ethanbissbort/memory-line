using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents a vector embedding for an event for semantic similarity search.
/// </summary>
[Table("event_embeddings")]
public class EventEmbedding
{
    [Key]
    [Column("embedding_id")]
    public string EmbeddingId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("event_id")]
    public string EventId { get; set; } = string.Empty;

    [Required]
    [Column("embedding_vector")]
    public string EmbeddingVector { get; set; } = string.Empty;

    [Required]
    [Column("embedding_provider")]
    [MaxLength(50)]
    public string EmbeddingProvider { get; set; } = string.Empty;

    [Required]
    [Column("embedding_model")]
    [MaxLength(100)]
    public string EmbeddingModel { get; set; } = string.Empty;

    [Column("embedding_dimension")]
    public int EmbeddingDimension { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Alias properties for backward compatibility
    [NotMapped]
    public string EventEmbeddingId
    {
        get => EmbeddingId;
        set => EmbeddingId = value;
    }

    [NotMapped]
    public string Embedding
    {
        get => EmbeddingVector;
        set => EmbeddingVector = value;
    }

    [NotMapped]
    public string Model
    {
        get => EmbeddingModel;
        set => EmbeddingModel = value;
    }

    // Navigation properties
    public virtual Event Event { get; set; } = null!;
}
