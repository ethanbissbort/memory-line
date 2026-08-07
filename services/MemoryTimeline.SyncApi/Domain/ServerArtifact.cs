namespace MemoryTimeline.SyncApi.Domain;

/// <summary>
/// Server-side record of a capture artifact (design §6.2, §11.4). Declared
/// integrity data (expected length/hash) is validated against the assembled
/// bytes before the artifact is accepted as complete.
/// </summary>
public class ServerArtifact
{
    /// <summary>Artifact ID (GUID, "D" format). Also names the on-disk part directory and blob.</summary>
    public string ArtifactId { get; set; } = string.Empty;

    public string CaptureId { get; set; } = string.Empty;

    /// <summary>audio_original | audio_normalized | transcript | waveform | image | metadata.</summary>
    public string ArtifactType { get; set; } = string.Empty;

    public string? MediaType { get; set; }

    public string? OriginalFileName { get; set; }

    /// <summary>Byte length declared at initiate; must match the assembled bytes.</summary>
    public long ExpectedByteLength { get; set; }

    /// <summary>Lowercase hex SHA-256 declared at initiate; must match the assembled bytes.</summary>
    public string ExpectedSha256 { get; set; } = string.Empty;

    /// <summary>Measured byte length of the assembled blob; set on completion.</summary>
    public long? ActualByteLength { get; set; }

    /// <summary>Measured lowercase hex SHA-256 of the assembled blob; set on completion.</summary>
    public string? ActualSha256 { get; set; }

    /// <summary>pending | uploading | complete | failed (ArtifactState values).</summary>
    public string State { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
