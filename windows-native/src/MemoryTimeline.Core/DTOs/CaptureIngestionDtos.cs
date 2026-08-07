namespace MemoryTimeline.Core.DTOs;

/// <summary>
/// Envelope describing a capture that originated on another device (design §7.2).
/// Metadata mirrors the shared capture contract
/// (shared-contracts/json-schema/capture-envelope.v1.json); the artifact section
/// carries the integrity data ingestion must verify before creating a queue item.
/// </summary>
public class RemoteCaptureEnvelope
{
    /// <summary>Globally unique, device-generated capture ID. Required.</summary>
    public string CaptureId { get; set; } = string.Empty;

    public string? SourceDeviceId { get; set; }

    /// <summary>Originating platform, e.g. "ios".</summary>
    public string SourcePlatform { get; set; } = "ios";

    /// <summary>memory | idea | trip_log | reminder | question | unclassified.</summary>
    public string CaptureType { get; set; } = "unclassified";

    public DateTime CapturedAtUtc { get; set; }

    public string? TimezoneId { get; set; }

    public int? LocalOffsetMinutes { get; set; }

    public string? TitleHint { get; set; }

    public string? UserNote { get; set; }

    public string? TripId { get; set; }

    public int ClientSchemaVersion { get; set; } = 1;

    // --- Audio artifact contract ---

    /// <summary>Device-generated artifact ID; a new one is minted when absent.</summary>
    public string? ArtifactId { get; set; }

    public string? OriginalFileName { get; set; }

    public string? MediaType { get; set; }

    /// <summary>Declared artifact size in bytes; verified against the resolved file. Required.</summary>
    public long ExpectedByteLength { get; set; }

    /// <summary>Declared lowercase-hex SHA-256 of the artifact bytes; verified. Required.</summary>
    public string ExpectedSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Opaque locator understood by <see cref="Services.IArtifactResolver"/>.
    /// Phase 0 resolves local file paths (test client / same-machine handoff);
    /// Phase 1 replaces the resolver with a sync-service download client without
    /// changing this contract.
    /// </summary>
    public string StorageLocator { get; set; } = string.Empty;
}

/// <summary>
/// Why an ingestion attempt failed. Maps to the design's retry classification
/// (§16.2): unavailable → retryable, invalid → permanent, storage → retryable.
/// </summary>
public enum IngestionFailureKind
{
    None = 0,
    /// <summary>Envelope is malformed (missing capture ID, hash, or locator). Permanent.</summary>
    InvalidRequest,
    /// <summary>Artifact could not be resolved/read right now. Retryable.</summary>
    ArtifactUnavailable,
    /// <summary>Artifact bytes do not match the declared hash/length. Permanent.</summary>
    ArtifactInvalid,
    /// <summary>Local persistence failed. Retryable.</summary>
    StorageFailure,
}

/// <summary>
/// Outcome of a capture ingestion attempt (design §7.2).
/// </summary>
public class IngestionResult
{
    public bool Success { get; init; }

    /// <summary>
    /// True when the capture had already been ingested; the existing queue item
    /// is referenced and NO new rows were created (idempotent replay).
    /// </summary>
    public bool AlreadyIngested { get; init; }

    public string? CaptureId { get; init; }

    public string? ArtifactId { get; init; }

    /// <summary>Queue item the capture is (or already was) attached to.</summary>
    public string? QueueId { get; init; }

    public IngestionFailureKind FailureKind { get; init; } = IngestionFailureKind.None;

    public string? FailureReason { get; init; }

    public static IngestionResult Ingested(string captureId, string artifactId, string queueId) => new()
    {
        Success = true,
        CaptureId = captureId,
        ArtifactId = artifactId,
        QueueId = queueId,
    };

    /// <summary>
    /// Idempotent-duplicate outcome. <paramref name="queueId"/> is null when the
    /// capture was ingested before but its queue item has since been deleted by
    /// the user — deletions are never silently resurrected (design §12.2).
    /// </summary>
    public static IngestionResult Duplicate(string captureId, string? artifactId, string? queueId) => new()
    {
        Success = true,
        AlreadyIngested = true,
        CaptureId = captureId,
        ArtifactId = artifactId,
        QueueId = queueId,
    };

    public static IngestionResult Failed(IngestionFailureKind kind, string reason, string? captureId = null) => new()
    {
        Success = false,
        FailureKind = kind,
        FailureReason = reason,
        CaptureId = captureId,
    };
}
