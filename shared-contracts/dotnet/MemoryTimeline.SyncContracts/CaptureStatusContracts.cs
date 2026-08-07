namespace MemoryTimeline.SyncContracts;

/// <summary>
/// Payload carried by a <c>capture_status</c> upsert change (design §19 Phase 3,
/// "processing status and review handoff").
///
/// Direction is Windows → service → capture-owning device. Windows is the only
/// writer: it publishes a row into its sync outbox each time a capture's
/// processing stage advances, and the originating phone consumes the change so
/// the user can follow the whole lifecycle without opening Windows. Editing and
/// approval stay on Windows (the phase's explicit boundary), so nothing here is
/// an instruction — it is a read-only projection of Windows-side state.
///
/// Every field except <see cref="CaptureId"/>, <see cref="Status"/> and
/// <see cref="UpdatedAtUtc"/> is optional: a status change published before
/// transcription has no transcript, and a failure has no review counts.
/// </summary>
public class CaptureStatusChangePayload
{
    /// <summary>The capture this status belongs to (the device-generated id).</summary>
    public string CaptureId { get; set; } = string.Empty;

    /// <summary>Coarse lifecycle state — one of <see cref="SyncCaptureStatus"/>.</summary>
    public string Status { get; set; } = SyncCaptureStatus.Received;

    /// <summary>
    /// Finer-grained position inside <see cref="Status"/>, mirroring the Windows
    /// queue's stage vocabulary (pending_download, verifying,
    /// ready_for_transcription, transcribing, extracting, review_ready,
    /// completed, failed_retryable, failed_configuration, failed_permanent).
    /// Null when the publisher has no finer detail than the status.
    /// </summary>
    public string? ProcessingStage { get; set; }

    /// <summary>
    /// When this status was produced on Windows. Consumers must apply changes in
    /// cursor order but may use this for display; it is not a sequencing key.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>True once a transcript exists for the capture on Windows.</summary>
    public bool TranscriptAvailable { get; set; }

    /// <summary>
    /// Leading excerpt of the transcript for on-phone preview, truncated by the
    /// publisher to <see cref="TranscriptPreviewMaxChars"/>. The full transcript
    /// deliberately stays on Windows in this phase — this is a preview, not a
    /// replica, and callers must not treat it as complete.
    /// </summary>
    public string? TranscriptPreview { get; set; }

    /// <summary>Character count of the complete transcript, so the phone can show
    /// how much was truncated without holding the whole text.</summary>
    public int? TranscriptCharCount { get; set; }

    /// <summary>Events extracted from this capture that are awaiting review.</summary>
    public int? PendingEventCount { get; set; }

    /// <summary>Events from this capture that have been approved onto the timeline.</summary>
    public int? ApprovedEventCount { get; set; }

    /// <summary>
    /// User-facing reason when <see cref="Status"/> is
    /// <see cref="SyncCaptureStatus.Failed"/>. Must never contain transcript
    /// content, file paths, or credentials (§14.5).
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>True when the failure is expected to clear on retry.</summary>
    public bool? FailureRetryable { get; set; }

    /// <summary>Maximum characters a publisher may put in <see cref="TranscriptPreview"/>.</summary>
    public const int TranscriptPreviewMaxChars = 600;
}
