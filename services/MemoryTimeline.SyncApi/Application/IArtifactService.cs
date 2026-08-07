using MemoryTimeline.SyncApi.Domain;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Multipart artifact upload/download with integrity validation (design §11.4):
/// initiate declares length + SHA-256, parts stream to disk, complete assembles
/// and verifies before the artifact becomes downloadable.
/// </summary>
public interface IArtifactService
{
    /// <summary>
    /// Declares an artifact on a capture. A client-supplied artifactId is used
    /// only when it parses as a GUID (it becomes a directory name); otherwise
    /// the server mints one. Re-initiating the same artifact on the same
    /// capture replays the upload parameters.
    /// </summary>
    Task<ServiceResult<ArtifactInitiateResult>> InitiateAsync(
        Device caller, string captureId, ArtifactInitiateRequest request, CancellationToken cancellationToken);

    /// <summary>Streams one raw part to disk (1-based part number; re-PUT overwrites, retry-safe).</summary>
    Task<ServiceResult<bool>> UploadPartAsync(
        Device caller, string artifactId, int partNumber, Stream content, long? declaredLength, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies part count, total length, and SHA-256 against both the request
    /// and the initiate declaration, then assembles the blob. On success the
    /// capture is marked received and a capture change with the audio artifact
    /// summary is appended — the change that triggers Windows ingestion.
    /// </summary>
    Task<ServiceResult<ArtifactCompleteResponse>> CompleteAsync(
        Device caller, string artifactId, ArtifactCompleteRequest request, CancellationToken cancellationToken);

    /// <summary>Resolves a complete artifact's blob path and media type for streaming.</summary>
    Task<ServiceResult<ArtifactDownloadInfo>> GetDownloadAsync(
        Device caller, string artifactId, CancellationToken cancellationToken);
}

/// <summary>An initiate outcome: the wire response plus whether a row was created (201) or replayed (200).</summary>
public sealed record ArtifactInitiateResult(ArtifactInitiateResponse Response, bool Created);

/// <summary>A downloadable artifact blob: absolute path and MIME type.</summary>
public sealed record ArtifactDownloadInfo(string FilePath, string MediaType);
