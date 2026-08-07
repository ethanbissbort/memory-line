using MemoryTimeline.SyncApi.Application;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.SyncApi.Api;

/// <summary>
/// Device registration, token refresh, revocation, and listing
/// (design §11.2 "Device registration").
/// </summary>
public static class DeviceEndpoints
{
    /// <summary>
    /// Maps the unauthenticated device routes. Registration is gated by the
    /// pairing code; refresh authenticates with the refresh token itself.
    /// </summary>
    public static void MapPublicEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/devices/register", RegisterAsync);
        group.MapPost("/devices/{deviceId}/refresh", RefreshAsync);
    }

    /// <summary>Maps the authenticated device routes (list, revoke).</summary>
    public static void MapAuthenticatedEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/devices", ListAsync);
        group.MapDelete("/devices/{deviceId}", RevokeAsync)
            .AddEndpointFilter(IdempotencyEndpointFilter.Instance);
    }

    private static async Task<IResult> RegisterAsync(
        DeviceRegisterRequest request,
        HttpContext context,
        IDeviceService devices,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = context.Request.Headers[IdempotencyEndpointFilter.HeaderName].ToString();
        var result = await devices.RegisterAsync(
            request,
            string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
            : ApiErrorResults.ToResult(result.Error!, context);
    }

    private static async Task<IResult> RefreshAsync(
        string deviceId,
        TokenRefreshRequest request,
        HttpContext context,
        IDeviceService devices,
        CancellationToken cancellationToken)
    {
        var result = await devices.RefreshAsync(deviceId, request, cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : ApiErrorResults.ToResult(result.Error!, context);
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IDeviceService devices,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await devices.ListAsync(caller, cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : ApiErrorResults.ToResult(result.Error!, context);
    }

    private static async Task<IResult> RevokeAsync(
        string deviceId,
        HttpContext context,
        IDeviceService devices,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await devices.RevokeAsync(caller, deviceId, cancellationToken);
        return result.IsSuccess
            ? Results.NoContent()
            : ApiErrorResults.ToResult(result.Error!, context);
    }
}
