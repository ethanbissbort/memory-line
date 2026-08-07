using MemoryTimeline.SyncApi.Domain;
using MemoryTimeline.SyncContracts;

namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Assistant session and turn lifecycle (design §19 Phase 4).
///
/// <para><b>The service stores and routes; it never retrieves or generates.</b>
/// Windows is the brain: the archive, the retrieval index and the extraction
/// prompts all live there, so a turn asked from a phone is dispatched rather
/// than answered here. Submission validates, persists and publishes an
/// <c>assistant_turn</c> change, then returns — the caller polls
/// <see cref="GetTurnAsync"/> or reads the answer off its own pull feed. There
/// is no path through this interface that produces an answer, and adding one
/// would put personal memory content through the service instead of leaving it
/// on the machine that owns it.</para>
///
/// <para>Ownership is per device, not per owner: only the device that opened a
/// session may add turns to it or read them back. Answers, by contrast, may
/// arrive from any of the owner's devices over <c>/sync/push</c> — routing is
/// owner-scoped because that is how a responder finds work, while reading a
/// conversation is device-scoped because a conversation belongs to the surface
/// having it.</para>
/// </summary>
public interface IAssistantService
{
    /// <summary>
    /// Opens a conversation owned by the calling device. An unknown
    /// preferredResponder is rejected rather than defaulted, so a client built
    /// against a newer vocabulary learns that this service cannot route its
    /// turns instead of silently getting Windows.
    /// </summary>
    Task<ServiceResult<AssistantSessionResponse>> CreateSessionAsync(Device caller, AssistantSessionCreateRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches a session. A session belonging to another device is reported as
    /// missing rather than forbidden, so the response cannot be used to probe
    /// which sessions exist.
    /// </summary>
    Task<ServiceResult<AssistantSessionResponse>> GetSessionAsync(Device caller, string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Accepts a question, persists it as pending and publishes it toward a
    /// responder in the same transaction. Idempotent on the client-supplied
    /// turnId: a retry after a lost response returns the stored turn with
    /// Created=false rather than asking a second time — which for an assistant
    /// is not merely wasteful but a second answer to a question the user asked
    /// once.
    /// </summary>
    Task<ServiceResult<AssistantTurnWriteResult>> SubmitTurnAsync(Device caller, string sessionId, AssistantTurnCreateRequest request, CancellationToken cancellationToken);

    /// <summary>Fetches a turn's current state, including the answer once a responder has pushed one.</summary>
    Task<ServiceResult<AssistantTurnResponse>> GetTurnAsync(Device caller, string turnId, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a turn and publishes the cancellation so a responder still
    /// working on it stops. Terminal and irreversible: a later answer for a
    /// cancelled turn is discarded (see the assistant_turn handling in
    /// <see cref="SyncChangeService"/>), because a user who interrupted the
    /// assistant must not have it speak anyway thirty seconds later. Cancelling
    /// an already-terminal turn is a no-op that returns its current state.
    /// </summary>
    Task<ServiceResult<AssistantTurnResponse>> CancelTurnAsync(Device caller, string turnId, CancellationToken cancellationToken);
}

/// <summary>
/// A turn submission outcome: the wire response plus whether the turn was
/// accepted now (201) or replayed from an earlier identical submission (200).
/// </summary>
public sealed record AssistantTurnWriteResult(AssistantTurnResponse Response, bool Created);
