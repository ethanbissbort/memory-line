using System.Text.Json;
using MemoryTimeline.SyncApi.Domain;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Mapping between domain entities and the shared wire contracts, plus
/// construction of capture change-log rows. Kept in one place so the capture
/// payload shape stays identical wherever a change is appended.
/// </summary>
internal static class EntityMappers
{
    /// <summary>Maps an artifact row to its wire summary, preferring measured values once complete.</summary>
    public static ArtifactSummary ToSummary(ServerArtifact artifact) => new()
    {
        ArtifactId = artifact.ArtifactId,
        ArtifactType = artifact.ArtifactType,
        MediaType = artifact.MediaType,
        OriginalFileName = artifact.OriginalFileName,
        ByteLength = artifact.ActualByteLength ?? artifact.ExpectedByteLength,
        Sha256 = artifact.ActualSha256 ?? artifact.ExpectedSha256,
        State = artifact.State,
    };

    /// <summary>Maps a capture row plus its artifacts to the wire response.</summary>
    public static CaptureResponse ToCaptureResponse(ServerCapture capture, IEnumerable<ServerArtifact> artifacts) => new()
    {
        CaptureId = capture.CaptureId,
        SourceDeviceId = capture.SourceDeviceId,
        SourcePlatform = capture.SourcePlatform,
        CaptureType = capture.CaptureType,
        CapturedAtUtc = capture.CapturedAtUtc,
        TimezoneId = capture.TimezoneId,
        LocalOffsetMinutes = capture.LocalOffsetMinutes,
        TripId = capture.TripId,
        TitleHint = capture.TitleHint,
        UserNote = capture.UserNote,
        Status = capture.Status,
        ServerRevision = capture.Revision,
        CreatedAtUtc = capture.CreatedAtUtc,
        Artifacts = artifacts.Select(ToSummary).ToList(),
    };

    /// <summary>Maps a device row to the wire device info.</summary>
    public static DeviceInfoResponse ToDeviceInfo(Device device) => new()
    {
        DeviceId = device.DeviceId,
        Platform = device.Platform,
        DisplayName = device.DisplayName,
        LastSeenAtUtc = device.LastSeenAtUtc,
        RevokedAtUtc = device.RevokedAtUtc,
    };

    /// <summary>
    /// Builds a capture upsert change-log row carrying a CaptureChangePayload
    /// (camelCase JSON). <paramref name="audioArtifact"/> is set only after an
    /// audio artifact completes — that change triggers Windows ingestion.
    /// </summary>
    public static SyncChangeRow CreateCaptureChange(ServerCapture capture, ArtifactSummary? audioArtifact, string sourceDeviceId)
    {
        var payload = new CaptureChangePayload
        {
            CaptureId = capture.CaptureId,
            SourceDeviceId = capture.SourceDeviceId,
            SourcePlatform = capture.SourcePlatform,
            CaptureType = capture.CaptureType,
            CapturedAtUtc = capture.CapturedAtUtc,
            TimezoneId = capture.TimezoneId,
            LocalOffsetMinutes = capture.LocalOffsetMinutes,
            TripId = capture.TripId,
            TitleHint = capture.TitleHint,
            UserNote = capture.UserNote,
            ClientSchemaVersion = capture.ClientSchemaVersion,
            Status = capture.Status,
            AudioArtifact = audioArtifact,
        };

        return new SyncChangeRow
        {
            OwnerId = capture.OwnerId,
            EntityType = SyncChangeEntityType.Capture,
            EntityId = capture.CaptureId,
            Operation = SyncOperation.Upsert,
            Revision = capture.Revision,
            SourceDeviceId = sourceDeviceId,
            PayloadJson = JsonSerializer.Serialize(payload, SyncJson.Options),
            ChangedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>Whether the artifact carries capture audio (its completion triggers ingestion).</summary>
    public static bool IsAudioArtifact(ServerArtifact artifact)
        => artifact.ArtifactType is "audio_original" or "audio_normalized";
}
