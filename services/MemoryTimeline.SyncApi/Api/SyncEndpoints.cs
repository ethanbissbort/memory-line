using MemoryTimeline.SyncApi.Application;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.SyncApi.Api;

/// <summary>Cursor-based synchronization routes (design §11.2 "Sync").</summary>
public static class SyncEndpoints
{
    /// <summary>Maps the authenticated sync routes.</summary>
    public static void MapEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/sync/pull", PullAsync);
        group.MapPost("/sync/push", PushAsync)
            .AddEndpointFilter(IdempotencyEndpointFilter.Instance);
        group.MapPost("/sync/ack", AckAsync)
            .AddEndpointFilter(IdempotencyEndpointFilter.Instance);
    }

    private static async Task<IResult> PullAsync(
        long? cursor,
        int? limit,
        HttpContext context,
        ISyncChangeService sync,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await sync.PullAsync(caller, cursor ?? 0, limit ?? 0, cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : ApiErrorResults.ToResult(result.Error!, context);
    }

    private static async Task<IResult> PushAsync(
        SyncPushRequest request,
        HttpContext context,
        ISyncChangeService sync,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await sync.PushAsync(caller, request, cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : ApiErrorResults.ToResult(result.Error!, context);
    }

    private static async Task<IResult> AckAsync(
        SyncAckRequest request,
        HttpContext context,
        ISyncChangeService sync,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await sync.AckAsync(caller, request, cancellationToken);
        return result.IsSuccess
            ? Results.NoContent()
            : ApiErrorResults.ToResult(result.Error!, context);
    }
}
