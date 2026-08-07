namespace MemoryTimeline.SyncApi.Domain;

/// <summary>
/// Server-side record of a device-neutral capture (design §6.1). Metadata only;
/// content lives in <see cref="ServerArtifact"/> rows and on disk.
/// </summary>
public class ServerCapture
{
    /// <summary>Capture ID (client-generated GUID, stored in "D" format).</summary>
    public string CaptureId { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The device the capture originated on — always taken from the access token.</summary>
    public string SourceDeviceId { get; set; } = string.Empty;

    public string SourcePlatform { get; set; } = string.Empty;

    /// <summary>memory | idea | trip_log | reminder | question | unclassified.</summary>
    public string CaptureType { get; set; } = string.Empty;

    public DateTime CapturedAtUtc { get; set; }

    public string? TimezoneId { get; set; }

    public int? LocalOffsetMinutes { get; set; }

    public string? TripId { get; set; }

    public string? TitleHint { get; set; }

    public string? UserNote { get; set; }

    /// <summary>Optional serialized LocationContext (camelCase JSON). Never logged.</summary>
    public string? LocationJson { get; set; }

    public int ClientSchemaVersion { get; set; }

    /// <summary>Server-side capture status (SyncCaptureStatus values).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Canonical revision; bumped on every server-visible change.</summary>
    public long Revision { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
