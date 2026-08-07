namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Issues device-bound credentials (design §14.1): short-lived HS256 JWT
/// access tokens (claims sub=deviceId, owner=ownerId) and opaque refresh
/// tokens stored server-side only as SHA-256 hashes.
/// </summary>
public interface ISyncTokenService
{
    /// <summary>Creates a signed access token for the device.</summary>
    AccessTokenResult CreateAccessToken(string deviceId, string ownerId);

    /// <summary>Creates a new random refresh token plus the hash to persist. The raw token is never stored or logged.</summary>
    RefreshTokenResult CreateRefreshToken();

    /// <summary>Lowercase hex SHA-256 of a refresh token, for constant-time comparison against the stored hash.</summary>
    string HashRefreshToken(string refreshToken);
}

/// <summary>A signed access token and its expiry.</summary>
public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

/// <summary>A freshly minted refresh token, its storable hash, and its expiry.</summary>
public sealed record RefreshTokenResult(string Token, string TokenHashHex, DateTime ExpiresAtUtc);
