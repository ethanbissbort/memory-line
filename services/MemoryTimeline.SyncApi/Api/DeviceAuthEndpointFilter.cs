using MemoryTimeline.SyncApi.Application;
using MemoryTimeline.SyncApi.Domain;
using MemoryTimeline.SyncApi.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MemoryTimeline.SyncApi.Api;

/// <summary>
/// Runs after JWT bearer validation on every authenticated route: loads the
/// device row for the token's sub claim and rejects unknown or revoked devices
/// with 401 ApiError (design §14.1 — revocation takes effect immediately, not
/// at token expiry). The loaded <see cref="Device"/> is stashed in
/// HttpContext.Items for handlers via <see cref="GetDevice"/>.
/// </summary>
public sealed class DeviceAuthEndpointFilter : IEndpointFilter
{
    /// <summary>HttpContext.Items key holding the authenticated <see cref="Device"/>.</summary>
    public const string DeviceItemKey = "MemoryTimeline.SyncApi.AuthenticatedDevice";

    /// <summary>Shared stateless instance (dependencies resolve per-request from RequestServices).</summary>
    public static readonly DeviceAuthEndpointFilter Instance = new();

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var deviceId = http.User.FindFirst(SyncTokenService.DeviceClaim)?.Value
            ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(deviceId))
        {
            return ApiErrorResults.Error(
                http, StatusCodes.Status401Unauthorized, SyncApiErrorCodes.Unauthorized,
                "The access token does not identify a device.");
        }

        var factory = http.RequestServices.GetRequiredService<IDbContextFactory<SyncDbContext>>();
        Device? device;
        await using (var db = await factory.CreateDbContextAsync(http.RequestAborted))
        {
            device = await db.Devices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId, http.RequestAborted);
        }

        if (device is null)
        {
            return ApiErrorResults.Error(
                http, StatusCodes.Status401Unauthorized, SyncApiErrorCodes.DeviceUnknown,
                "The device is not registered.");
        }

        if (device.RevokedAtUtc is not null)
        {
            return ApiErrorResults.Error(
                http, StatusCodes.Status401Unauthorized, SyncApiErrorCodes.DeviceRevoked,
                "This device has been revoked.");
        }

        http.Items[DeviceItemKey] = device;
        return await next(context);
    }

    /// <summary>The device authenticated by this filter; throws when the filter did not run.</summary>
    public static Device GetDevice(HttpContext context)
        => context.Items[DeviceItemKey] as Device
            ?? throw new InvalidOperationException(
                "Authenticated device missing — the endpoint is not behind DeviceAuthEndpointFilter.");
}
