namespace MemoryTimeline.SyncContracts;

/// <summary>
/// Wire contracts for cursor-based synchronization (design §11.2, §12.1).
/// The server keeps a monotonically ordered change log; clients pull changes
/// after their stored cursor and push their local outbox entries.
/// </summary>
public class SyncChangeDto
{
    /// <summary>Monotonically increasing server change ID — the pull cursor.</summary>
    public long ChangeId { get; set; }

    /// <summary>capture | capture_artifact | recording_queue | pending_event | event.</summary>
    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary>upsert | delete.</summary>
    public string Operation { get; set; } = SyncOperation.Upsert;

    public long Revision { get; set; }

    public DateTime ChangedAtUtc { get; set; }

    /// <summary>Device the change originated on; pulls exclude the caller's own changes.</summary>
    public string? SourceDeviceId { get; set; }

    /// <summary>Entity-type-specific JSON payload (e.g. <see cref="CaptureChangePayload"/>).</summary>
    public string? PayloadJson { get; set; }
}

public class SyncPullResponse
{
    public List<SyncChangeDto> Changes { get; set; } = new();

    /// <summary>Cursor to store after applying every change in this page.</summary>
    public long NextCursor { get; set; }

    public bool HasMore { get; set; }
}

public class SyncPushRequest
{
    public List<SyncPushEntry> Entries { get; set; } = new();
}

public class SyncPushEntry
{
    /// <summary>Client-local outbox sequence; makes each entry idempotent per device.</summary>
    public long ClientSequence { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string Operation { get; set; } = SyncOperation.Upsert;

    public string? PayloadJson { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}

public class SyncPushResponse
{
    public List<SyncPushEntryResult> Results { get; set; } = new();
}

public class SyncPushEntryResult
{
    public long ClientSequence { get; set; }

    public bool Accepted { get; set; }

    /// <summary>True when the entry was already applied by an earlier push (idempotent replay).</summary>
    public bool Duplicate { get; set; }

    public long? ServerChangeId { get; set; }

    public string? Error { get; set; }
}

public class SyncAckRequest
{
    /// <summary>Highest change ID the device has durably applied.</summary>
    public long Cursor { get; set; }
}

/// <summary>Change operations.</summary>
public static class SyncOperation
{
    public const string Upsert = "upsert";
    public const string Delete = "delete";
}

/// <summary>Entity type discriminators used in sync changes.</summary>
public static class SyncChangeEntityType
{
    public const string Capture = "capture";
    public const string CaptureArtifact = "capture_artifact";
    public const string RecordingQueue = "recording_queue";
    public const string PendingEvent = "pending_event";
    public const string Event = "event";

    /// <summary>
    /// Windows-authored processing status for a capture (Phase 3). Payload is
    /// <see cref="CaptureStatusChangePayload"/>. Distinct from
    /// <see cref="Capture"/>, which flows phone -> Windows and carries the audio
    /// artifact to download.
    /// </summary>
    public const string CaptureStatus = "capture_status";
}

/// <summary>
/// Payload carried by a <c>capture</c> upsert change: everything the Windows
/// client needs to build a RemoteCaptureEnvelope and download the audio.
/// </summary>
public class CaptureChangePayload
{
    public string CaptureId { get; set; } = string.Empty;

    public string SourceDeviceId { get; set; } = string.Empty;

    public string SourcePlatform { get; set; } = string.Empty;

    public string CaptureType { get; set; } = "unclassified";

    public DateTime CapturedAtUtc { get; set; }

    public string? TimezoneId { get; set; }

    public int? LocalOffsetMinutes { get; set; }

    public string? TripId { get; set; }

    public string? TitleHint { get; set; }

    public string? UserNote { get; set; }

    public int ClientSchemaVersion { get; set; } = 1;

    /// <summary>Capture status at the time of the change (see <see cref="SyncCaptureStatus"/>).</summary>
    public string Status { get; set; } = SyncCaptureStatus.Received;

    /// <summary>The audio artifact to download, when one is complete.</summary>
    public ArtifactSummary? AudioArtifact { get; set; }
}
