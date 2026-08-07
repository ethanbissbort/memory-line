using MemoryTimeline.SyncApi.Domain;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Device registration, token refresh, revocation, and listing (design §11.2,
/// §14.1). Registration is the only unauthenticated entry point and is gated
/// by the owner pairing code; refresh authenticates with the refresh token itself.
/// </summary>
public interface IDeviceService
{
    /// <summary>
    /// Registers a device after validating the pairing code (constant-time).
    /// When <paramref name="idempotencyKey"/> is supplied, a replay returns the
    /// original registration (same device and tokens) instead of creating a
    /// second device.
    /// </summary>
    Task<ServiceResult<DeviceRegisterResponse>> RegisterAsync(
        DeviceRegisterRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Rotates the refresh token and issues a new access token. Stale or revoked credentials fail with 401.</summary>
    Task<ServiceResult<TokenRefreshResponse>> RefreshAsync(
        string deviceId,
        TokenRefreshRequest request,
        CancellationToken cancellationToken);

    /// <summary>Revokes any device of the caller's owner (this is how Windows revokes the iPhone).</summary>
    Task<ServiceResult<bool>> RevokeAsync(Device caller, string targetDeviceId, CancellationToken cancellationToken);

    /// <summary>Lists all devices of the caller's owner.</summary>
    Task<ServiceResult<List<DeviceInfoResponse>>> ListAsync(Device caller, CancellationToken cancellationToken);
}
