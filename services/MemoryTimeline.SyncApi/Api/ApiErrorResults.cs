using MemoryTimeline.SyncApi.Application;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.SyncApi.Api;

/// <summary>
/// Builds ApiError responses (the shared-contracts Error envelope) with the
/// request trace identifier as the correlation ID.
/// </summary>
public static class ApiErrorResults
{
    /// <summary>Maps a service-layer failure to an ApiError JSON response.</summary>
    public static IResult ToResult(ServiceError error, HttpContext context)
        => Error(context, error.StatusCode, error.Code, error.Message, error.Retryable);

    /// <summary>Builds an ApiError JSON response directly.</summary>
    public static IResult Error(HttpContext context, int statusCode, string code, string message, bool retryable = false)
        => Results.Json(
            new ApiError
            {
                Code = code,
                Message = message,
                Retryable = retryable,
                CorrelationId = context.TraceIdentifier,
            },
            statusCode: statusCode);
}
