namespace MemoryTimeline.SyncContracts;

/// <summary>
/// Wire contracts for device registration and token exchange
/// (Memory Line Sync API v1, design §11.2 / §14.1). Property names serialize
/// as camelCase: the service uses ASP.NET Core JSON defaults and clients must
/// use <c>JsonSerializerDefaults.Web</c>.
/// </summary>
public class DeviceRegisterRequest
{
    /// <summary>Owner pairing code proving the caller may join this sync domain.</summary>
    public string PairingCode { get; set; } = string.Empty;

    /// <summary>windows | ios | other.</summary>
    public string Platform { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? AppVersion { get; set; }

    /// <summary>Optional device public key (base64) for future artifact envelope encryption.</summary>
    public string? PublicKey { get; set; }
}

public class DeviceRegisterResponse
{
    public string DeviceId { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public DateTime AccessTokenExpiresAtUtc { get; set; }

    public string RefreshToken { get; set; } = string.Empty;
}

public class TokenRefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class TokenRefreshResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public DateTime AccessTokenExpiresAtUtc { get; set; }

    /// <summary>Rotated refresh token; the previous one is invalidated.</summary>
    public string RefreshToken { get; set; } = string.Empty;
}

public class DeviceInfoResponse
{
    public string DeviceId { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTime? LastSeenAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
}
