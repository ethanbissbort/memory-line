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
public enum RelationshipType
{
    Causal,
    Thematic,
    Temporal,
    Person,
    Location,
    Other
}

/// <summary>
/// Extension methods for RelationshipType enum.
/// </summary>
public static class RelationshipTypeExtensions
{
    public static string ToStringValue(this RelationshipType type)
    {
        return type switch
        {
            RelationshipType.Causal => "causal",
            RelationshipType.Thematic => "thematic",
            RelationshipType.Temporal => "temporal",
            RelationshipType.Person => "person",
            RelationshipType.Location => "location",
            RelationshipType.Other => "other",
            _ => "other"
        };
    }

    public static RelationshipType FromString(string value)
    {
        return value?.ToLowerInvariant() switch
        {
            "causal" => RelationshipType.Causal,
            "thematic" => RelationshipType.Thematic,
            "temporal" => RelationshipType.Temporal,
            "person" => RelationshipType.Person,
            "location" => RelationshipType.Location,
            _ => RelationshipType.Other
        };
    }

    public static RelationshipType[] AllTypes()
    {
        return new[]
        {
            RelationshipType.Causal,
            RelationshipType.Thematic,
            RelationshipType.Temporal,
            RelationshipType.Person,
            RelationshipType.Location,
            RelationshipType.Other
        };
    }
}
