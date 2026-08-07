namespace MemoryTimeline.SyncApi.Domain;

/// <summary>
/// Dedupe receipt for one pushed outbox entry (design §12.1). The composite
/// key (device, client sequence) makes /sync/push idempotent per device:
/// replays return the original server change ID with Duplicate=true.
/// </summary>
public class PushReceipt
{
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Client-local outbox sequence number of the pushed entry.</summary>
    public long ClientSequence { get; set; }

    /// <summary>The change-log row the original push produced.</summary>
    public long ServerChangeId { get; set; }

    public DateTime ReceivedAtUtc { get; set; }
}
