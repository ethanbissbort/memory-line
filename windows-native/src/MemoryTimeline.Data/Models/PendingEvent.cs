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
    public string Status { get; set; } = PendingStatus.PendingReview;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("reviewed_at")]
    public DateTime? ReviewedAt { get; set; }

    // Navigation properties
    public virtual RecordingQueue? Queue { get; set; }
}

/// <summary>
/// Pending event status enumeration.
/// </summary>
public static class PendingStatus
{
    public const string PendingReview = "pending_review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static readonly string[] AllStatuses =
    {
        PendingReview, Approved, Rejected
    };
}
