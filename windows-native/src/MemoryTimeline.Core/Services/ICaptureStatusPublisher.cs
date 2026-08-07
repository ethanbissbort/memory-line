using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Publishes a capture's Windows-side processing status toward the device that
/// recorded it (design §19 Phase 3, "processing status and review handoff").
/// The implementation lives in MemoryTimeline.Sync — it writes a
/// <c>capture_status</c> row into the local sync outbox, which the outbox
/// publisher pushes to the sync service and the originating phone pulls — but
/// the port is declared here so the queue/ingestion pipeline can call it
/// without Core taking a dependency on the sync contracts.
///
/// Windows stays the only writer and the only editing/approval surface; this is
/// a read-only projection so the user can follow a capture's whole lifecycle
/// from the phone.
/// </summary>
public interface ICaptureStatusPublisher
{
    /// <summary>
    /// Publishes the queue item's current status/stage. No-ops for captures that
    /// did not come from another device (no phone is waiting on them) and for a
    /// projection that is unchanged since the last publish, so callers can call
    /// it after every stage transition without producing outbox noise.
    /// Implementations must never log or publish transcript text beyond the
    /// bounded preview the contract defines (§14.5).
    /// </summary>
    /// <param name="queueItem">Queue row as it now stands, after its update was persisted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(RecordingQueue queueItem, CancellationToken cancellationToken = default);
}
