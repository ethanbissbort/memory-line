namespace MemoryTimeline.SyncApi.Domain;

/// <summary>
/// The single sync-domain owner for a self-hosted deployment (design §14.1).
/// Created automatically on first run; every device and capture belongs to it.
/// </summary>
public class Owner
{
    /// <summary>Owner ID (GUID, "D" format).</summary>
    public string OwnerId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
