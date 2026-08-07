namespace MemoryTimeline.SyncContracts;

/// <summary>
/// Standard error envelope returned by all 4xx/5xx responses
/// (matches the Error schema in shared-contracts/openapi/memory-line-sync-v1.yaml).
/// </summary>
public class ApiError
{
    /// <summary>Stable machine-readable code, e.g. invalid_pairing_code, artifact_hash_mismatch.</summary>
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Whether retrying the identical request can succeed.</summary>
    public bool Retryable { get; set; }

    public string? CorrelationId { get; set; }
}
