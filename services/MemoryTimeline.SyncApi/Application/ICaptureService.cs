using System.Text.Json.Nodes;
using MemoryTimeline.SyncApi.Domain;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Capture metadata lifecycle (design §6.1, §11.2). Creation is idempotent by
/// captureId; every accepted mutation appends a capture change row in the same
/// transaction so other devices see it via /sync/pull.
/// </summary>
public interface ICaptureService
{
    /// <summary>
    /// Creates a capture. The source device is always the caller from the
    /// access token — the request's sourceDeviceId is ignored. Replaying an
    /// existing captureId returns the current capture with Created=false.
    /// </summary>
    Task<ServiceResult<CaptureWriteResult>> CreateAsync(Device caller, CaptureCreateRequest request, CancellationToken cancellationToken);

    /// <summary>Fetches a capture with its artifact summaries.</summary>
    Task<ServiceResult<CaptureResponse>> GetAsync(Device caller, string captureId, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a partial update (captureType, titleHint, userNote, tripId —
    /// only keys present in <paramref name="body"/>; explicit null clears a
    /// nullable field). Bumps the revision and appends a change on any change.
    /// </summary>
    Task<ServiceResult<CaptureResponse>> UpdateAsync(Device caller, string captureId, JsonObject body, CancellationToken cancellationToken);

    /// <summary>
    /// Phase 1 minimal handler for /complete and /retry: returns the current
    /// capture and records device last-seen bookkeeping; no state change.
    /// </summary>
    Task<ServiceResult<CaptureResponse>> TouchAsync(Device caller, string captureId, CancellationToken cancellationToken);
}

/// <summary>A capture write outcome: the wire response plus whether a row was created (201) or replayed (200).</summary>
public sealed record CaptureWriteResult(CaptureResponse Response, bool Created);
