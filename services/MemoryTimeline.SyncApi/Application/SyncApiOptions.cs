namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Configuration for the sync service, bound from the "SyncApi" section.
/// All values have working defaults so the service runs with an empty section.
/// </summary>
public class SyncApiOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "SyncApi";

    /// <summary>
    /// Directory holding the SQLite database, artifact blobs, signing key, and
    /// pairing code. Relative paths resolve against the working directory.
    /// </summary>
    public string DataDir { get; set; } = "data";

    /// <summary>
    /// Owner pairing code required by device registration. When unset, a code
    /// is generated on first run and persisted to {DataDir}/pairing-code.txt.
    /// </summary>
    public string? PairingCode { get; set; }

    /// <summary>Access token (JWT) lifetime in minutes.</summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 60;

    /// <summary>Refresh token lifetime in days.</summary>
    public int RefreshTokenLifetimeDays { get; set; } = 30;

    /// <summary>Maximum accepted bytes per uploaded artifact part (default 8 MiB).</summary>
    public long MaxPartSizeBytes { get; set; } = 8_388_608;

    /// <summary>
    /// Base64 HMAC-SHA256 token signing key (at least 32 bytes). When unset, a
    /// key is generated on first run and persisted to {DataDir}/signing.key.
    /// </summary>
    public string? TokenSigningKeyBase64 { get; set; }
}
