namespace MemoryTimeline.SyncApi.Domain;

/// <summary>
/// One row of the append-only server change log (design §6.7, §12.1).
/// <see cref="ChangeId"/> is the monotonically increasing pull cursor; pulls
/// exclude rows whose <see cref="SourceDeviceId"/> is the caller (echo suppression).
/// </summary>
public class SyncChangeRow
{
    /// <summary>Autoincrement primary key — the sync cursor value.</summary>
    public long ChangeId { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Entity discriminator (SyncChangeEntityType values).</summary>
    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary>upsert | delete.</summary>
    public string Operation { get; set; } = string.Empty;

    public long Revision { get; set; }

    /// <summary>Device the change originated on; used for echo suppression on pull.</summary>
    public string? SourceDeviceId { get; set; }

    /// <summary>Entity-type-specific camelCase JSON payload (e.g. CaptureChangePayload). Never logged.</summary>
    public string? PayloadJson { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
