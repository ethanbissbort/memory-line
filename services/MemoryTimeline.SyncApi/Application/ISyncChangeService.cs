using MemoryTimeline.SyncApi.Domain;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Cursor-based synchronization over the server change log (design §12.1):
/// pull returns changes after a cursor excluding the caller's own (echo
/// suppression), push drains a client outbox idempotently per device, ack
/// records the caller's durably-applied cursor.
/// </summary>
public interface ISyncChangeService
{
    /// <summary>Pulls changes with change_id &gt; <paramref name="cursor"/>, oldest first; limit clamped to 1..500.</summary>
    Task<ServiceResult<SyncPullResponse>> PullAsync(Device caller, long cursor, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Applies pushed outbox entries with per-entry results. Entries are
    /// deduplicated by (device, clientSequence); one bad entry never fails the
    /// batch. Capture-entity payloads update the capture status and revision;
    /// capture_status entries (design §19 Phase 3) are validated against
    /// CaptureStatusChangePayload and update the capture's status projection,
    /// and a malformed one is rejected rather than repaired.
    /// </summary>
    Task<ServiceResult<SyncPushResponse>> PushAsync(Device caller, SyncPushRequest request, CancellationToken cancellationToken);

    /// <summary>Stores the caller's acknowledged cursor and last-seen timestamp.</summary>
    Task<ServiceResult<bool>> AckAsync(Device caller, SyncAckRequest request, CancellationToken cancellationToken);
}
