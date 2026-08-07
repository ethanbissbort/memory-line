namespace MemoryTimeline.SyncApi.Domain;

/// <summary>
/// Stored response for one (device, Idempotency-Key) tuple (design §11.4).
/// Replaying a mutation with the same key returns the original status code
/// and body instead of re-executing the operation. Must never hold token
/// material: the register endpoint stores only the device identity and mints
/// fresh tokens on replay. Records older than the 48h retention window are
/// swept at startup.
/// </summary>
public class IdempotencyRecord
{
    public string DeviceId { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Method and path the key was first used on (diagnostics only).</summary>
    public string Endpoint { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    /// <summary>Serialized camelCase JSON response body; null for bodyless responses (e.g. 204).</summary>
    public string? ResponseJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
