using MemoryTimeline.Core.DTOs;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Resolves a capture artifact's opaque storage locator to a local, readable
/// file path that the transcription pipeline can consume. This is the seam the
/// Phase 1 sync client plugs into (download + cache); Phase 0 only handles
/// locators that are already local file paths.
/// </summary>
public interface IArtifactResolver
{
    Task<ArtifactResolution> ResolveAsync(RemoteCaptureEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of resolving an artifact locator to a local file.
/// </summary>
public class ArtifactResolution
{
    public bool Success { get; init; }

    /// <summary>Local file path holding the artifact bytes, when successful.</summary>
    public string? LocalPath { get; init; }

    public string? Error { get; init; }

    public static ArtifactResolution Resolved(string localPath) => new() { Success = true, LocalPath = localPath };

    public static ArtifactResolution Failed(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Phase 0 resolver: the storage locator is a local file path (test client or
/// same-machine handoff). Replaced by a sync-service download client in Phase 1.
/// </summary>
public class LocalFileArtifactResolver : IArtifactResolver
{
    public Task<ArtifactResolution> ResolveAsync(RemoteCaptureEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var locator = envelope.StorageLocator;
        if (string.IsNullOrWhiteSpace(locator))
        {
            return Task.FromResult(ArtifactResolution.Failed("Storage locator is empty."));
        }

        return Task.FromResult(File.Exists(locator)
            ? ArtifactResolution.Resolved(locator)
            : ArtifactResolution.Failed($"Artifact file not found: {locator}"));
    }
}
