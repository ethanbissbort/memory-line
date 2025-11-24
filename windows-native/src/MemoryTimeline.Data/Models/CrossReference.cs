using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents a relationship between two events identified by RAG analysis.
/// </summary>
[Table("cross_references")]
public class CrossReference
{
    [Key]
    [Column("reference_id")]
    public string ReferenceId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("event_id_1")]
    public string EventId1 { get; set; } = string.Empty;

    [Required]
    [Column("event_id_2")]
    public string EventId2 { get; set; } = string.Empty;

    [Required]
    [Column("relationship_type")]
    [MaxLength(50)]
    public string RelationshipType { get; set; } = string.Empty;

    [Column("confidence_score")]
    public double? ConfidenceScore { get; set; }

    [Column("analysis_details")]
    public string? AnalysisDetails { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Relationship type enumeration.
/// </summary>
public static class RelationshipType
{
    public const string Causal = "causal";
    public const string Thematic = "thematic";
    public const string Temporal = "temporal";
    public const string Person = "person";
    public const string Location = "location";
    public const string Other = "other";

    public static readonly string[] AllTypes =
    {
        Causal, Thematic, Temporal, Person, Location, Other
    };
}
