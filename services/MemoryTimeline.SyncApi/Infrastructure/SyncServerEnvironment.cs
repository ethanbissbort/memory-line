namespace MemoryTimeline.SyncApi.Infrastructure;

/// <summary>
/// Resolved on-disk state of the sync service: absolute data paths plus the
/// materialized signing key and pairing code. Built once at startup by
/// <see cref="SyncApiBootstrapper"/> and registered as a singleton.
/// </summary>
public sealed class SyncServerEnvironment
{
    /// <summary>Absolute path of the data directory.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>Absolute path of the artifact blob/part directory ({DataDir}/artifacts).</summary>
    public required string ArtifactsDirectory { get; init; }

    /// <summary>Absolute path of the SQLite database file ({DataDir}/sync.db).</summary>
    public required string DatabasePath { get; init; }

    /// <summary>HMAC-SHA256 token signing key bytes. Never logged.</summary>
    public required byte[] SigningKey { get; init; }

    /// <summary>Owner pairing code required for device registration. Logged once at startup only.</summary>
    public required string PairingCode { get; init; }
}
