using MemoryTimeline.SyncApi.Application;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.SyncApi.Api;

/// <summary>
/// Multipart artifact upload/download routes (design §11.2 "Artifacts"):
/// initiate → PUT raw parts → complete (integrity-validated) → download.
/// </summary>
public static class ArtifactEndpoints
{
    /// <summary>Maps the authenticated artifact routes.</summary>
    public static void MapEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/captures/{captureId}/artifacts/initiate", InitiateAsync)
            .AddEndpointFilter(IdempotencyEndpointFilter.Instance);
        // Part upload takes raw bytes and is inherently retry-safe (re-PUT
        // overwrites), so it does not use the idempotency filter.
        group.MapPut("/artifacts/{artifactId}/parts/{partNumber:int}", UploadPartAsync);
        group.MapPost("/artifacts/{artifactId}/complete", CompleteAsync)
            .AddEndpointFilter(IdempotencyEndpointFilter.Instance);
        group.MapGet("/artifacts/{artifactId}/download", DownloadAsync);
    }

    private static async Task<IResult> InitiateAsync(
        string captureId,
        ArtifactInitiateRequest request,
        HttpContext context,
        IArtifactService artifacts,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await artifacts.InitiateAsync(caller, captureId, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ApiErrorResults.ToResult(result.Error!, context);
        }

        // Re-initiating an existing artifact replays the upload parameters with 200.
        var initiate = result.Value!;
        return initiate.Created
            ? Results.Json(initiate.Response, statusCode: StatusCodes.Status201Created)
            : Results.Json(initiate.Response);
    }

    private static async Task<IResult> UploadPartAsync(
        string artifactId,
        int partNumber,
        HttpContext context,
        IArtifactService artifacts,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await artifacts.UploadPartAsync(
            caller, artifactId, partNumber, context.Request.Body, context.Request.ContentLength, cancellationToken);
        return result.IsSuccess
            ? Results.NoContent()
            : ApiErrorResults.ToResult(result.Error!, context);
    }

    private static async Task<IResult> CompleteAsync(
        string artifactId,
        ArtifactCompleteRequest request,
        HttpContext context,
        IArtifactService artifacts,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await artifacts.CompleteAsync(caller, artifactId, request, cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : ApiErrorResults.ToResult(result.Error!, context);
    }

    private static async Task<IResult> DownloadAsync(
        string artifactId,
        HttpContext context,
        IArtifactService artifacts,
        CancellationToken cancellationToken)
    {
        var caller = DeviceAuthEndpointFilter.GetDevice(context);
        var result = await artifacts.GetDownloadAsync(caller, artifactId, cancellationToken);
        return result.IsSuccess
            ? Results.File(result.Value!.FilePath, result.Value.MediaType, enableRangeProcessing: true)
            : ApiErrorResults.ToResult(result.Error!, context);
    }
}
