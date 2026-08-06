using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Represents a capture source in the processing queue. Historically this was
/// always an audio recording; it now also covers pasted/imported text sources
/// (see <see cref="SourceType"/>) which skip speech-to-text entirely.
/// </summary>
[Table("recording_queue")]
public class RecordingQueue
{
    [Key]
    [Column("queue_id")]
    public string QueueId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Path to the recorded audio file. CONVENTION: non-audio sources
    /// (<see cref="SourceType"/> != <see cref="QueueSourceType.Audio"/>) store
    /// <see cref="string.Empty"/> here — the column is NOT NULL on existing
    /// databases and SQLite's ALTER TABLE ADD COLUMN cannot relax that, so the
    /// [Required] attribute stays. Every downstream consumer (playback, file
    /// operations, duration display) must guard against an empty path before
    /// touching the file system.
    /// </summary>
    [Required]
    [Column("audio_file_path")]
    public string AudioFilePath { get; set; } = string.Empty;

    /// <summary>
    /// What kind of source this queue row represents. Audio rows carry a WAV
    /// file to transcribe; Text/Imported rows carry their content inline in
    /// <see cref="Transcript"/> and never run speech-to-text.
    /// </summary>
    [Column("source_type")]
    public QueueSourceType SourceType { get; set; } = QueueSourceType.Audio;

    /// <summary>
    /// Optional user-facing label for the source, e.g. "Journal 2014-03".
    /// </summary>
    [Column("source_label")]
    [MaxLength(200)]
    public string? SourceLabel { get; set; }

    /// <summary>
    /// The source text. For Text/Imported sources this holds the pasted or
    /// imported content itself. For Audio sources it caches the Whisper
    /// transcript after the first successful transcription so retries (e.g.
    /// after a flaky LLM call) reuse it instead of re-running speech-to-text.
    /// </summary>
    [Column("transcript")]
    public string? Transcript { get; set; }

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
/// Discriminates what kind of capture a <see cref="RecordingQueue"/> row is.
/// Stored as INTEGER by EF Core's default enum-to-int conversion.
/// </summary>
public enum QueueSourceType
{
    /// <summary>A recorded audio file that needs speech-to-text.</summary>
    Audio = 0,

    /// <summary>Pasted text (journal entry, email, chat export) — no audio.</summary>
    Text = 1,

    /// <summary>Text imported from an external file/tool — no audio.</summary>
    Imported = 2
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
