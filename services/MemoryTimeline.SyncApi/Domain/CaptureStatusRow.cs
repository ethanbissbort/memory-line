namespace MemoryTimeline.SyncApi.Domain;

/// <summary>
/// Latest Windows-authored processing status for one capture (design §19
/// Phase 3). Exactly one row per capture, overwritten in place: this is a
/// projection of Windows-side state, not a history — the change log keeps the
/// history. Kept off <see cref="ServerCapture"/> because those columns are the
/// capturing device's own metadata, while every field here is authored by
/// Windows and absent for captures that have not been processed yet.
/// </summary>
public class CaptureStatusRow
{
    /// <summary>Capture this status belongs to (stored in "D" format, as on <see cref="ServerCapture"/>).</summary>
    public string CaptureId { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Coarse lifecycle state (SyncCaptureStatus values).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Finer-grained Windows queue stage (QueueProcessingStage values); null when the publisher had no finer detail.</summary>
    public string? ProcessingStage { get; set; }

    /// <summary>When Windows produced the status (publisher clock; not a sequencing key).</summary>
    public DateTime UpdatedAtUtc { get; set; }

    public bool TranscriptAvailable { get; set; }

    /// <summary>
    /// Leading transcript excerpt, at most
    /// CaptureStatusChangePayload.TranscriptPreviewMaxChars characters. Never
    /// logged (design §14.5).
    /// </summary>
    public string? TranscriptPreview { get; set; }

    /// <summary>Character count of the complete Windows-side transcript.</summary>
    public int? TranscriptCharCount { get; set; }

    public int? PendingEventCount { get; set; }

    public int? ApprovedEventCount { get; set; }

    /// <summary>
    /// User-facing failure reason; by contract never carries transcript
    /// content, file paths, or credentials. Never logged (design §14.5).
    /// </summary>
    public string? FailureReason { get; set; }

    public bool? FailureRetryable { get; set; }

    /// <summary>Monotonic per-capture status revision, carried on the change row.</summary>
    public long Revision { get; set; }

    /// <summary>When the service accepted the most recent status push.</summary>
    public DateTime ReceivedAtUtc { get; set; }
}
