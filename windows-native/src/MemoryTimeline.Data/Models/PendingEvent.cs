using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents an extracted event awaiting user review.
/// </summary>
[Table("pending_events")]
public class PendingEvent
{
    [Key]
    [Column("pending_id")]
    public string PendingId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("extracted_data")]
    public string ExtractedData { get; set; } = string.Empty;

    [Column("audio_file_path")]
    public string? AudioFilePath { get; set; }

    [Column("transcript")]
    public string? Transcript { get; set; }

    [Column("queue_id")]
    public string? QueueId { get; set; }

    [Required]
    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = PendingStatus.PendingReview.ToStringValue();

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("reviewed_at")]
    public DateTime? ReviewedAt { get; set; }

    // Navigation properties
    public virtual RecordingQueue? RecordingQueue { get; set; }
}

/// <summary>
/// Pending event status enumeration.
/// </summary>
public enum PendingStatus
{
    PendingReview,
    Approved,
    Rejected
}

/// <summary>
/// Extension methods for PendingStatus enum.
/// </summary>
public static class PendingStatusExtensions
{
    public static string ToStringValue(this PendingStatus status)
    {
        return status switch
        {
            PendingStatus.PendingReview => "pending_review",
            PendingStatus.Approved => "approved",
            PendingStatus.Rejected => "rejected",
            _ => "pending_review"
        };
    }

    public static PendingStatus FromString(string value)
    {
        return value?.ToLowerInvariant() switch
        {
            "pending_review" => PendingStatus.PendingReview,
            "approved" => PendingStatus.Approved,
            "rejected" => PendingStatus.Rejected,
            _ => PendingStatus.PendingReview
        };
    }

    public static PendingStatus[] AllStatuses()
    {
        return new[]
        {
            PendingStatus.PendingReview,
            PendingStatus.Approved,
            PendingStatus.Rejected
        };
    }
}
