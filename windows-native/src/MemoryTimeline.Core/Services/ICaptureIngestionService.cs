using MemoryTimeline.Core.DTOs;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Device-neutral capture ingestion (design §7.2). Every recording — made on this
/// machine or arriving from another device — enters the processing queue through
/// this service, which owns identity (capture/artifact IDs), integrity (SHA-256 +
/// byte length), idempotency (source_capture_id uniqueness), and the atomic
/// domain-write + sync-outbox insertion.
/// </summary>
public interface ICaptureIngestionService
{
    /// <summary>
    /// Ingests a recording produced on this machine. Mints a capture + artifact
    /// identity for it, hashes the audio file, and creates the queue item.
    /// Replaying the same recording — same queue ID or identical content bytes
    /// (§12.2: duplicate hash means reuse) — is a no-op returning the existing item.
    /// </summary>
    Task<IngestionResult> IngestLocalRecordingAsync(
        AudioRecordingDto recording,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests a capture that originated on another device. Resolves the artifact,
    /// verifies byte length and SHA-256 against the envelope, and creates the queue
    /// item exactly once per capture ID AND per content hash — retries, duplicate
    /// uploads, and re-uploads under regenerated IDs can never create duplicate
    /// queue items or pending events. A capture whose queue item was deliberately
    /// deleted is acknowledged as a duplicate, not resurrected (§12.2).
    /// </summary>
    Task<IngestionResult> IngestRemoteCaptureAsync(
        RemoteCaptureEnvelope capture,
        CancellationToken cancellationToken = default);
}
