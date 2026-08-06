using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Local sync outbox record. Domain changes that must eventually be published to
/// the sync service are written here in the SAME SaveChanges as the domain change
/// itself, so the local database and remote sync state cannot silently diverge.
/// A (future, Phase 1) sync worker publishes undelivered records in outbox_id
/// order and marks them delivered.
/// See docs/design/IOS-ROADTRIP-COMPANION-SYSTEM-DESIGN.md §7.6.
/// </summary>
[Table("sync_outbox")]
public class SyncOutbox
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("outbox_id")]
    public long OutboxId { get; set; }

    [Required]
    [Column("entity_type")]
    [MaxLength(40)]
    public string EntityType { get; set; } = string.Empty;

    [Required]
    [Column("entity_id")]
    public string EntityId { get; set; } = string.Empty;

    [Required]
    [Column("operation")]
    [MaxLength(10)]
    public string Operation { get; set; } = SyncOutboxOperation.Upsert;

    /// <summary>
    /// Metadata-only JSON payload. Must never contain transcripts, audio bytes,
    /// secrets, or precise location (privacy rule §14.5).
    /// </summary>
    [Column("payload_json")]
    public string? PayloadJson { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("delivered_at")]
    public DateTime? DeliveredAt { get; set; }

    [Column("attempt_count")]
    public int AttemptCount { get; set; }

    [Column("last_error")]
    public string? LastError { get; set; }
}

/// <summary>
/// Outbox operations.
/// </summary>
public static class SyncOutboxOperation
{
    public const string Upsert = "upsert";
    public const string Delete = "delete";
}

/// <summary>
/// Entity type discriminators used in sync outbox records.
/// </summary>
public static class SyncEntityType
{
    public const string Capture = "capture";
    public const string CaptureArtifact = "capture_artifact";
    public const string RecordingQueue = "recording_queue";
    public const string PendingEvent = "pending_event";
    public const string Event = "event";
}
