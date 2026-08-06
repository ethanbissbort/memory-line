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

    // --- Device-neutral provenance (iOS companion sync groundwork, design §7.1) ---
    // AudioFilePath above remains the RESOLVED LOCAL CACHE PATH used for processing;
    // the identity of a recording is its capture, not a Windows path.

    /// <summary>Globally unique capture ID this queue item was created from. Unique when set.</summary>
    [Column("source_capture_id")]
    public string? SourceCaptureId { get; set; }

    [Column("source_device_id")]
    public string? SourceDeviceId { get; set; }

    [Required]
    [Column("source_platform")]
    [MaxLength(20)]
    public string SourcePlatform { get; set; } = CapturePlatform.Windows;

    /// <summary>Artifact whose bytes AudioFilePath is a local materialization of.</summary>
    [Column("audio_artifact_id")]
    public string? AudioArtifactId { get; set; }

    [Column("original_file_name")]
    public string? OriginalFileName { get; set; }

    [Column("content_sha256")]
    [MaxLength(64)]
    public string? ContentSha256 { get; set; }

    [Column("sync_state")]
    [MaxLength(30)]
    public string? SyncState { get; set; }

    [Column("received_at")]
    public DateTime? ReceivedAt { get; set; }

    /// <summary>
    /// Fine-grained processing stage (design §7.3). The four-state Status column
    /// stays externally compatible; this adds mobile-grade progress reporting.
    /// </summary>
    [Column("processing_stage")]
    [MaxLength(30)]
    public string? ProcessingStage { get; set; }

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

/// <summary>
/// Fine-grained queue processing stages (design §7.3). Orthogonal to
/// <see cref="QueueStatus"/>: status remains the coarse externally-compatible
/// state machine, stage describes where inside it an item currently is.
/// </summary>
public static class QueueProcessingStage
{
    public const string PendingDownload = "pending_download";
    public const string Verifying = "verifying";
    public const string ReadyForTranscription = "ready_for_transcription";
    public const string Transcribing = "transcribing";
    public const string Extracting = "extracting";
    public const string ReviewReady = "review_ready";
    public const string Completed = "completed";
    public const string FailedRetryable = "failed_retryable";
    public const string FailedConfiguration = "failed_configuration";
    public const string FailedPermanent = "failed_permanent";
}

/// <summary>
/// Synchronization state of a queue item relative to the sync service.
/// </summary>
public static class QueueSyncState
{
    /// <summary>Created on this machine; no sync involvement yet.</summary>
    public const string LocalOnly = "local_only";
    /// <summary>Ingested from a remote capture; receipt not yet acknowledged upstream.</summary>
    public const string Received = "received";
    /// <summary>Durable receipt acknowledged to the sync service.</summary>
    public const string Acknowledged = "acknowledged";
}
