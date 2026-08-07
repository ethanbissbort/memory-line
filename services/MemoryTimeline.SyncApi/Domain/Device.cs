namespace MemoryTimeline.SyncApi.Domain;

/// <summary>
/// A registered client device (design §6.7, §14.1). Holds the hashed refresh
/// token (never the token itself), the device's acknowledged sync cursor, and
/// revocation state. Revoked devices are rejected on every authenticated call.
/// </summary>
public class Device
{
    /// <summary>Device ID (GUID, "D" format) — the JWT <c>sub</c> claim.</summary>
    public string DeviceId { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    /// <summary>windows | ios | other.</summary>
    public string Platform { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? AppVersion { get; set; }

    /// <summary>Optional device public key (base64) for future artifact envelope encryption.</summary>
    public string? PublicKey { get; set; }

    /// <summary>
    /// Lowercase hex SHA-256 of the current refresh token; rotated on every
    /// refresh. Declared as an EF concurrency token so concurrent rotations
    /// conflict instead of silently overwriting each other.
    /// </summary>
    public string RefreshTokenHash { get; set; } = string.Empty;

    public DateTime RefreshTokenExpiresAtUtc { get; set; }

    /// <summary>
    /// Hash of the refresh token that was current before the last rotation,
    /// honored until <see cref="PreviousRefreshTokenExpiresAtUtc"/> so a client
    /// that never received the rotation response can recover.
    /// </summary>
    public string? PreviousRefreshTokenHash { get; set; }

    /// <summary>End of the recovery grace window for the previous refresh token.</summary>
    public DateTime? PreviousRefreshTokenExpiresAtUtc { get; set; }

    /// <summary>Highest change ID this device has durably applied (sync diagnostics).</summary>
    public long AckedCursor { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastSeenAtUtc { get; set; }

    /// <summary>Set when the device is revoked; revoked devices fail auth with 401.</summary>
    public DateTime? RevokedAtUtc { get; set; }
}
