using MemoryTimeline.SyncApi.Application;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.SyncApi.Api;

/// <summary>
/// Assistant session and turn routes (design §11.2 "Assistant", §19 Phase 4).
///
/// Every handler returns as soon as the turn is durable. There is no
/// long-polling or streaming route here on purpose: the answer comes from
/// Windows over the change feed, so a client either polls
/// <c>GET /assistant/turns/{turnId}</c> or reads the answer off the
/// <c>/sync/pull</c> loop it is already running — and the phone's pull loop
/// survives suspension and network changes in a way a held-open connection
/// does not.
/// </summary>
public static class AssistantEndpoints
{
    /// <summary>Maps the authenticated assistant routes.</summary>
    public static void MapEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/assistant/sessions", CreateSessionAsync)
            .AddEndpointFilter(IdempotencyEndpointFilter.Instance);
        group.MapGet("/assistant/sessions/{sessionId}", GetSessionAsync);
        group.MapPost("/assistant/sessions/{sessionId}/turns", SubmitTurnAsync)
            .AddEndpointFilter(IdempotencyEndpointFilter.Instance);
        group.MapGet("/assistant/turns/{turnId}", GetTurnAsync);
        group.MapPost("/assistant/turns/{turnId}/cancel", CancelTurnAsync)
            .AddEndpointFilter(IdempotencyEndpointFilter.Instance);
    }

    private static async Task<IResult> CreateSessionAsync(
        AssistantSessionCreateRequest request,
        HttpContext context,
        IAssistantService assistant,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await assistant.CreateSessionAsync(caller, request, cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/assistant/sessions/{result.Value!.SessionId}", result.Value)
            : ApiErrorResults.ToResult(result.Error!, context);
    }

    private static async Task<IResult> GetSessionAsync(
        string sessionId,
        HttpContext context,
        IAssistantService assistant,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await assistant.GetSessionAsync(caller, sessionId, cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : ApiErrorResults.ToResult(result.Error!, context);
    }

    private static async Task<IResult> SubmitTurnAsync(
        string sessionId,
        AssistantTurnCreateRequest request,
        HttpContext context,
        IAssistantService assistant,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await assistant.SubmitTurnAsync(caller, sessionId, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ApiErrorResults.ToResult(result.Error!, context);
        }

        // Resubmitting a turnId returns 200 with the turn as it stands — which
        // may already carry an answer — rather than asking the question twice.
        var write = result.Value!;
        return write.Created
            ? Results.Created($"/api/v1/assistant/turns/{write.Response.TurnId}", write.Response)
            : Results.Json(write.Response);
    }

    private static async Task<IResult> GetTurnAsync(
        string turnId,
        HttpContext context,
        IAssistantService assistant,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await assistant.GetTurnAsync(caller, turnId, cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : ApiErrorResults.ToResult(result.Error!, context);
    }

    private static async Task<IResult> CancelTurnAsync(
        string turnId,
        HttpContext context,
        IAssistantService assistant,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await assistant.CancelTurnAsync(caller, turnId, cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : ApiErrorResults.ToResult(result.Error!, context);
    }
}
