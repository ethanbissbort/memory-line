using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using MemoryTimeline.SyncApi.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Self-issued HS256 JWT access tokens plus 32-byte base64url refresh tokens
/// (design §14.1). The signing key comes from <see cref="SyncServerEnvironment"/>.
/// </summary>
public sealed class SyncTokenService : ISyncTokenService
{
    /// <summary>Issuer and audience of self-issued tokens.</summary>
    public const string Issuer = "memory-line-sync";

    /// <summary>Audience of self-issued tokens (same as issuer for the self-hosted service).</summary>
    public const string Audience = "memory-line-sync";

    /// <summary>Claim carrying the owner ID.</summary>
    public const string OwnerClaim = "owner";

    /// <summary>Claim carrying the device ID.</summary>
    public const string DeviceClaim = "sub";

    private readonly SigningCredentials _signingCredentials;
    private readonly SyncApiOptions _options;

    public SyncTokenService(SyncServerEnvironment environment, IOptions<SyncApiOptions> options)
    {
        _options = options.Value;
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(environment.SigningKey),
            SecurityAlgorithms.HmacSha256);
    }

    public AccessTokenResult CreateAccessToken(string deviceId, string ownerId)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Claims = new Dictionary<string, object>
            {
                [DeviceClaim] = deviceId,
                [OwnerClaim] = ownerId,
            },
            SigningCredentials = _signingCredentials,
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(handler.CreateToken(descriptor));
        return new AccessTokenResult(token, expires);
    }

    public RefreshTokenResult CreateRefreshToken()
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expires = DateTime.UtcNow.AddDays(_options.RefreshTokenLifetimeDays);
        return new RefreshTokenResult(token, HashRefreshToken(token), expires);
    }

    public string HashRefreshToken(string refreshToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();
}
