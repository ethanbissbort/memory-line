using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.Sync;

/// <summary>
/// Persists the last durably-applied server change ID (design §12.1). The local
/// store is authoritative; the server-side ack is diagnostic only.
/// </summary>
public interface ISyncCursorStore
{
    Task<long> GetCursorAsync();

    Task SetCursorAsync(long cursor);
}

/// <summary>
/// Applies one pulled change to the local database (design §7.4
/// RemoteChangeApplier). Capture upserts resolve to: download artifact →
/// idempotent ingestion → optional queue processing.
/// </summary>
public interface IRemoteChangeApplier
{
    Task<ChangeApplicationResult> ApplyAsync(SyncChangeDto change, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of applying one pulled change.</summary>
public enum ChangeApplicationResult
{
    /// <summary>Applied (or idempotently already applied); the cursor may advance past it.</summary>
    Applied,
    /// <summary>Nothing to do for this entity type/operation; cursor may advance.</summary>
    Skipped,
    /// <summary>Permanently unappliable (e.g. invalid artifact); logged, cursor advances so it cannot poison the queue.</summary>
    FailedPermanent,
    /// <summary>Transient failure; the cursor must NOT advance — the change is retried next cycle.</summary>
    FailedRetryable,
}

/// <summary>
/// Drains the local sync_outbox to the server (design §7.4 LocalOutboxPublisher):
/// pushes undelivered records in order and marks accepted/duplicate ones delivered.
/// </summary>
public interface ILocalOutboxPublisher
{
    /// <summary>Returns the number of records delivered this run.</summary>
    Task<int> PublishPendingAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Periodic sync loop (design §7.4 SyncBackgroundWorker): pull → apply → ack,
/// then publish outbox. Runs only while sync is enabled and the device is
/// registered; safe to start unconditionally at app startup.
/// </summary>
public interface ISyncBackgroundWorker
{
    /// <summary>Starts the periodic loop (idempotent).</summary>
    void Start();

    /// <summary>Stops the periodic loop (idempotent).</summary>
    Task StopAsync();

    /// <summary>Runs one sync cycle immediately; no-op when disabled/unregistered.</summary>
    Task<SyncCycleResult> SyncNowAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised after every cycle (and on state transitions) for UI status.</summary>
    event EventHandler<SyncStatusChangedEventArgs>? SyncStatusChanged;
}

/// <summary>Summary of one sync cycle.</summary>
public class SyncCycleResult
{
    public bool Ran { get; init; }

    public int ChangesApplied { get; init; }

    public int ChangesFailedPermanent { get; init; }

    public int OutboxPublished { get; init; }

    public long Cursor { get; init; }

    public string? Error { get; init; }
}

/// <summary>Event args for sync status transitions (consumed by the Settings UI).</summary>
public class SyncStatusChangedEventArgs : EventArgs
{
    public bool IsSyncing { get; init; }

    public DateTime? LastSuccessAtUtc { get; init; }

    public string StatusText { get; init; } = string.Empty;

    public string? LastError { get; init; }
}
