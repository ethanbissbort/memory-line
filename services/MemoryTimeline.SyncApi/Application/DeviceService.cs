using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoryTimeline.SyncApi.Domain;
using MemoryTimeline.SyncApi.Infrastructure;
using MemoryTimeline.SyncContracts;
using Microsoft.EntityFrameworkCore;

namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// <see cref="IDeviceService"/> over the sync database. Stateless over the
/// context factory (one short-lived context per operation) and registered as
/// a singleton. Never logs pairing codes, tokens, or token hashes.
/// </summary>
public sealed class DeviceService : IDeviceService
{
    /// <summary>Idempotency scope for the unauthenticated register endpoint (no device ID exists yet).</summary>
    private const string RegisterIdempotencyScope = "__register__";

    private static readonly string[] AllowedPlatforms = ["windows", "ios", "other"];

    private readonly IDbContextFactory<SyncDbContext> _contextFactory;
    private readonly ISyncTokenService _tokens;
    private readonly SyncServerEnvironment _environment;
    private readonly ILogger<DeviceService> _logger;

    public DeviceService(
        IDbContextFactory<SyncDbContext> contextFactory,
        ISyncTokenService tokens,
        SyncServerEnvironment environment,
        ILogger<DeviceService> logger)
    {
        _contextFactory = contextFactory;
        _tokens = tokens;
        _environment = environment;
        _logger = logger;
    }

    public async Task<ServiceResult<DeviceRegisterResponse>> RegisterAsync(
        DeviceRegisterRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Client JSON can null out non-nullable DTO strings; normalize before use.
        if (!FixedTimeEquals(request.PairingCode ?? string.Empty, _environment.PairingCode))
        {
            _logger.LogWarning("Device registration rejected: invalid pairing code.");
            return ServiceResult<DeviceRegisterResponse>.Fail(
                StatusCodes.Status401Unauthorized, SyncApiErrorCodes.InvalidPairingCode, "The pairing code is not valid.");
        }

        var platform = (request.Platform ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedPlatforms.Contains(platform))
        {
            return ServiceResult<DeviceRegisterResponse>.Fail(
                StatusCodes.Status400BadRequest, SyncApiErrorCodes.ValidationError,
                "platform must be one of: windows, ios, other.");
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Replay of a paired caller's earlier registration (pairing code already validated above,
        // so the stored response — which contains tokens — is only ever returned to a paired device).
        if (idempotencyKey is not null)
        {
            var existing = await db.IdempotencyRecords.FindAsync(
                [RegisterIdempotencyScope, idempotencyKey], cancellationToken);
            if (existing?.ResponseJson is not null)
            {
                var replayed = JsonSerializer.Deserialize<DeviceRegisterResponse>(existing.ResponseJson, SyncJson.Options);
                if (replayed is not null)
                {
                    return ServiceResult<DeviceRegisterResponse>.Ok(replayed);
                }
            }
        }

        var owner = await db.Owners.OrderBy(o => o.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Owner row missing — the database was not initialized at startup.");

        var now = DateTime.UtcNow;
        var refresh = _tokens.CreateRefreshToken();
        var device = new Device
        {
            DeviceId = Guid.NewGuid().ToString("D"),
            OwnerId = owner.OwnerId,
            Platform = platform,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "Unnamed device" : request.DisplayName.Trim(),
            AppVersion = request.AppVersion,
            PublicKey = request.PublicKey,
            RefreshTokenHash = refresh.TokenHashHex,
            RefreshTokenExpiresAtUtc = refresh.ExpiresAtUtc,
            AckedCursor = 0,
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
        };
        var access = _tokens.CreateAccessToken(device.DeviceId, owner.OwnerId);

        var response = new DeviceRegisterResponse
        {
            DeviceId = device.DeviceId,
            OwnerId = owner.OwnerId,
            AccessToken = access.Token,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = refresh.Token,
        };

        db.Devices.Add(device);
        if (idempotencyKey is not null)
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                DeviceId = RegisterIdempotencyScope,
                IdempotencyKey = idempotencyKey,
                Endpoint = "POST /api/v1/devices/register",
                StatusCode = StatusCodes.Status201Created,
                ResponseJson = JsonSerializer.Serialize(response, SyncJson.Options),
                CreatedAtUtc = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (idempotencyKey is not null)
        {
            // Concurrent registration with the same idempotency key: the whole save rolled
            // back (no orphan device); return the response the winning request stored.
            await using var retryDb = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var winner = await retryDb.IdempotencyRecords.FindAsync(
                [RegisterIdempotencyScope, idempotencyKey], cancellationToken);
            var replayed = winner?.ResponseJson is null
                ? null
                : JsonSerializer.Deserialize<DeviceRegisterResponse>(winner.ResponseJson, SyncJson.Options);
            if (replayed is null)
            {
                throw;
            }

            return ServiceResult<DeviceRegisterResponse>.Ok(replayed);
        }

        _logger.LogInformation(
            "Registered device {DeviceId} (platform {Platform}) for owner {OwnerId}.",
            device.DeviceId, device.Platform, owner.OwnerId);
        return ServiceResult<DeviceRegisterResponse>.Ok(response);
    }

    public async Task<ServiceResult<TokenRefreshResponse>> RefreshAsync(
        string deviceId,
        TokenRefreshRequest request,
        CancellationToken cancellationToken)
    {
        // A single 401 shape for unknown devices and bad tokens avoids device enumeration.
        var invalid = ServiceResult<TokenRefreshResponse>.Fail(
            StatusCodes.Status401Unauthorized, SyncApiErrorCodes.RefreshTokenInvalid,
            "The refresh token is not valid for this device.");

        if (!Guid.TryParse(deviceId, out var deviceGuid) || string.IsNullOrEmpty(request.RefreshToken))
        {
            return invalid;
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var device = await db.Devices.FirstOrDefaultAsync(
            d => d.DeviceId == deviceGuid.ToString("D"), cancellationToken);
        if (device is null || device.RefreshTokenHash.Length == 0)
        {
            return invalid;
        }

        if (device.RevokedAtUtc is not null)
        {
            _logger.LogWarning("Refresh rejected for revoked device {DeviceId}.", device.DeviceId);
            return ServiceResult<TokenRefreshResponse>.Fail(
                StatusCodes.Status401Unauthorized, SyncApiErrorCodes.DeviceRevoked, "This device has been revoked.");
        }

        var presentedHash = _tokens.HashRefreshToken(request.RefreshToken);
        if (!FixedTimeEquals(presentedHash, device.RefreshTokenHash) || device.RefreshTokenExpiresAtUtc <= DateTime.UtcNow)
        {
            _logger.LogWarning("Refresh rejected for device {DeviceId}: stale or expired refresh token.", device.DeviceId);
            return invalid;
        }

        var refresh = _tokens.CreateRefreshToken();
        var access = _tokens.CreateAccessToken(device.DeviceId, device.OwnerId);
        device.RefreshTokenHash = refresh.TokenHashHex;
        device.RefreshTokenExpiresAtUtc = refresh.ExpiresAtUtc;
        device.LastSeenAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return ServiceResult<TokenRefreshResponse>.Ok(new TokenRefreshResponse
        {
            AccessToken = access.Token,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = refresh.Token,
        });
    }

    public async Task<ServiceResult<bool>> RevokeAsync(
        Device caller,
        string targetDeviceId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(targetDeviceId, out var targetGuid))
        {
            return ServiceResult<bool>.Fail(
                StatusCodes.Status404NotFound, SyncApiErrorCodes.DeviceUnknown, "No such device.");
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var target = await db.Devices.FirstOrDefaultAsync(
            d => d.DeviceId == targetGuid.ToString("D") && d.OwnerId == caller.OwnerId, cancellationToken);
        if (target is null)
        {
            return ServiceResult<bool>.Fail(
                StatusCodes.Status404NotFound, SyncApiErrorCodes.DeviceUnknown, "No such device.");
        }

        if (target.RevokedAtUtc is null)
        {
            target.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Device {TargetDeviceId} revoked by device {CallerDeviceId}.", target.DeviceId, caller.DeviceId);
        }

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<List<DeviceInfoResponse>>> ListAsync(
        Device caller,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var devices = await db.Devices.AsNoTracking()
            .Where(d => d.OwnerId == caller.OwnerId)
            .OrderBy(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return ServiceResult<List<DeviceInfoResponse>>.Ok(devices.Select(EntityMappers.ToDeviceInfo).ToList());
    }

    private static bool FixedTimeEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
