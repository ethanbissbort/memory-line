using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents an audio recording in the processing queue.
/// </summary>
[Table("recording_queue")]
public class RecordingQueue
{
    [Key]
    [Column("queue_id")]
    public string QueueId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("audio_file_path")]
    public string AudioFilePath { get; set; } = string.Empty;

    [Required]
    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = QueueStatus.Pending;

    [Column("duration_seconds")]
    public double? DurationSeconds { get; set; }

    [Column("file_size_bytes")]
    public long? FileSizeBytes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("processed_at")]
    public DateTime? ProcessedAt { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    // Navigation properties
    public virtual ICollection<PendingEvent> PendingEvents { get; set; } = new List<PendingEvent>();
}

/// <summary>
/// Queue status enumeration.
/// </summary>
public static class QueueStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public static readonly string[] AllStatuses =
    {
        Pending, Processing, Completed, Failed
    };
}
