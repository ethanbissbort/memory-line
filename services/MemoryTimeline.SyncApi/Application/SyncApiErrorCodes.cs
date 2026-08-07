namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Stable machine-readable error codes carried in ApiError.Code
/// (shared-contracts OpenAPI Error schema; design §16.2 failure classes).
/// Clients branch on these — never rename an existing value.
/// </summary>
public static class SyncApiErrorCodes
{
    public const string Unauthorized = "unauthorized";
    public const string InvalidPairingCode = "invalid_pairing_code";
    public const string DeviceUnknown = "device_unknown";
    public const string DeviceRevoked = "device_revoked";
    public const string RefreshTokenInvalid = "refresh_token_invalid";
    public const string ValidationError = "validation_error";
    public const string CaptureNotFound = "capture_not_found";
    public const string CaptureConflict = "capture_conflict";
    public const string ArtifactNotFound = "artifact_not_found";
    public const string ArtifactConflict = "artifact_conflict";
    public const string ArtifactInvalidState = "artifact_invalid_state";
    public const string ArtifactPartInvalid = "artifact_part_invalid";
    public const string ArtifactPartTooLarge = "artifact_part_too_large";
    public const string ArtifactIncompleteParts = "artifact_incomplete_parts";
    public const string ArtifactPartCountMismatch = "artifact_part_count_mismatch";
    public const string ArtifactLengthMismatch = "artifact_length_mismatch";
    public const string ArtifactHashMismatch = "artifact_hash_mismatch";
    public const string ArtifactNotComplete = "artifact_not_complete";
    public const string AssistantSessionNotFound = "assistant_session_not_found";
    public const string AssistantTurnNotFound = "assistant_turn_not_found";
    public const string AssistantTurnConflict = "assistant_turn_conflict";
    public const string InternalError = "internal_error";
}
