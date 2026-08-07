namespace MemoryTimeline.SyncContracts;

/// <summary>
/// Wire contracts for the voice assistant (design §19 Phase 4).
///
/// <para><b>Where the thinking happens.</b> Windows is the brain by default:
/// the archive, the retrieval index and the extraction prompts all live in
/// <c>MemoryTimeline.Core</c>, and the Phase 3 boundary already establishes
/// Windows as the only writer. A turn asked from a phone is therefore
/// *dispatched* rather than *answered* by the service — it rides the existing
/// sync change feed to Windows, which answers and pushes the result back the
/// same way capture status already flows. The service stores and routes; it
/// does not retrieve or generate.</para>
///
/// <para>Two alternatives are first-class rather than bolted on, because a
/// roadtrip companion whose answers stop when the PC sleeps is not much of a
/// companion:</para>
/// <list type="bullet">
///   <item><description><see cref="AssistantResponder.Windows"/> — the default.
///   Grounded in the full archive. Requires Windows to be online; a turn simply
///   waits otherwise.</description></item>
///   <item><description><see cref="AssistantResponder.Provider"/> — the service
///   calls a configured LLM provider directly. Answers arrive with Windows
///   asleep, but grounding is limited to whatever context the client supplies,
///   so answers must be marked accordingly (see
///   <see cref="AssistantTurnResult.Grounding"/>).</description></item>
///   <item><description><see cref="AssistantResponder.OnDevice"/> — the phone
///   pre-processes locally (transcription, and candidate retrieval over the
///   subset it has synced) and submits that context with the turn. Cheapest and
///   works offline-ish, but the device only knows what it holds.</description></item>
/// </list>
///
/// <para>The responder is chosen per turn, not baked into the session, so a
/// client can fall back mid-conversation when Windows goes offline without
/// starting over. The service records which responder actually answered, which
/// is not always the one requested.</para>
///
/// <para><b>Privacy (§14.5).</b> Nothing here carries a full transcript or a
/// file path. Citation snippets are bounded excerpts, and the same rule that
/// governs <c>CaptureStatusChangePayload</c> applies: these payloads cross
/// devices, so they carry what a user needs to read, not the archive itself.</para>
/// </summary>
public static class AssistantResponder
{
    /// <summary>Windows answers, grounded in the full local archive. Default.</summary>
    public const string Windows = "windows";

    /// <summary>The service calls a configured LLM provider directly.</summary>
    public const string Provider = "provider";

    /// <summary>The device pre-processed and supplied its own context.</summary>
    public const string OnDevice = "on_device";
}

/// <summary>
/// Lifecycle of one turn. A turn is <see cref="Pending"/> from the moment it is
/// accepted; the service moves it to <see cref="Dispatched"/> when it has been
/// published toward a responder, and the responder moves it onward. Terminal
/// states are <see cref="Completed"/>, <see cref="Failed"/> and
/// <see cref="Cancelled"/>.
/// </summary>
public static class AssistantTurnStatus
{
    /// <summary>Accepted and durable, not yet routed.</summary>
    public const string Pending = "pending";

    /// <summary>Published toward a responder; awaiting an answer.</summary>
    public const string Dispatched = "dispatched";

    /// <summary>A responder has claimed it and is producing an answer.</summary>
    public const string Answering = "answering";

    /// <summary>Answered. <see cref="AssistantTurnResult"/> is present.</summary>
    public const string Completed = "completed";

    /// <summary>Ended without an answer; see the failure fields.</summary>
    public const string Failed = "failed";

    /// <summary>Interrupted by the user (design §19 Phase 4, "interruption and cancellation").</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>How well an answer is anchored to the user's actual archive.</summary>
public static class AssistantGrounding
{
    /// <summary>Retrieved from the full archive; citations refer to real events.</summary>
    public const string Archive = "archive";

    /// <summary>Grounded only in context the client supplied with the turn.</summary>
    public const string ClientContext = "client_context";

    /// <summary>No retrieval — general knowledge. Must be surfaced as such.</summary>
    public const string None = "none";
}

/// <summary>Opens a conversation. Sessions scope turn ordering and shared context.</summary>
public class AssistantSessionCreateRequest
{
    /// <summary>
    /// Preferred responder for turns in this session, one of
    /// <see cref="AssistantResponder"/>. A turn may override it. Defaults to
    /// <see cref="AssistantResponder.Windows"/>.
    /// </summary>
    public string PreferredResponder { get; set; } = AssistantResponder.Windows;

    /// <summary>
    /// Optional free-text label for the surface that opened the session
    /// ("carplay", "watch", "mac"), for diagnostics only. Never used for routing.
    /// </summary>
    public string? Surface { get; set; }
}

/// <summary>A conversation.</summary>
public class AssistantSessionResponse
{
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Device that opened it; only that device may add turns.</summary>
    public string DeviceId { get; set; } = string.Empty;

    public string PreferredResponder { get; set; } = AssistantResponder.Windows;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Last activity, so idle sessions can be reaped.</summary>
    public DateTime LastTurnAtUtc { get; set; }

    public int TurnCount { get; set; }
}

/// <summary>Asks a question.</summary>
public class AssistantTurnCreateRequest
{
    /// <summary>
    /// Client-generated turn id (lowercased UUID), so a retried submission after
    /// a lost response is idempotent rather than asking twice.
    /// </summary>
    public string TurnId { get; set; } = string.Empty;

    /// <summary>
    /// The question as text. Speech-to-text happens on the device — the service
    /// never receives audio for a turn, which keeps this endpoint cheap and
    /// keeps voice data on the phone.
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Overrides the session's preferred responder for this turn only. Null
    /// means use the session's. Lets a client fall back when Windows is offline
    /// without abandoning the conversation.
    /// </summary>
    public string? Responder { get; set; }

    /// <summary>
    /// Optional context the device retrieved itself
    /// (<see cref="AssistantResponder.OnDevice"/> pre-processing). Bounded by
    /// the service; oversized context is rejected rather than truncated, so a
    /// client is never silently answered on a fraction of what it sent.
    /// </summary>
    public List<AssistantContextItem>? ClientContext { get; set; }

    /// <summary>
    /// Whether the client wants incremental chunks. A responder may ignore it
    /// and deliver one final answer; clients must handle both.
    /// </summary>
    public bool Stream { get; set; }
}

/// <summary>
/// One piece of device-supplied grounding: an event the phone believes is
/// relevant, with a bounded excerpt. Mirrors what a Windows retrieval hit looks
/// like so a responder can treat both alike.
/// </summary>
public class AssistantContextItem
{
    public string EventId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Event date as the device knows it; may be imprecise.</summary>
    public DateTime? EventDateUtc { get; set; }

    /// <summary>Bounded excerpt — never the full event text (§14.5).</summary>
    public string? Excerpt { get; set; }

    /// <summary>Device-side similarity score, if it computed one. Advisory.</summary>
    public double? Score { get; set; }
}

/// <summary>State of a turn, as any participant sees it.</summary>
public class AssistantTurnResponse
{
    public string TurnId { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    /// <summary>Monotonic within the session, assigned by the service.</summary>
    public int Sequence { get; set; }

    public string Question { get; set; } = string.Empty;

    /// <summary>One of <see cref="AssistantTurnStatus"/>.</summary>
    public string Status { get; set; } = AssistantTurnStatus.Pending;

    /// <summary>Responder the client asked for.</summary>
    public string RequestedResponder { get; set; } = AssistantResponder.Windows;

    /// <summary>
    /// Responder that actually answered. Differs from
    /// <see cref="RequestedResponder"/> when a fallback happened; null until
    /// something claims the turn.
    /// </summary>
    public string? ActualResponder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Present once <see cref="Status"/> is completed.</summary>
    public AssistantTurnResult? Result { get; set; }

    /// <summary>Display-safe failure text; no paths or credentials (§14.5).</summary>
    public string? FailureReason { get; set; }

    /// <summary>Whether retrying could plausibly succeed (§16.2 semantics).</summary>
    public bool? FailureRetryable { get; set; }
}

/// <summary>An answer.</summary>
public class AssistantTurnResult
{
    /// <summary>The answer text. Clients speak this via on-device TTS.</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// How well anchored the answer is, one of <see cref="AssistantGrounding"/>.
    /// Clients must surface anything other than
    /// <see cref="AssistantGrounding.Archive"/> rather than presenting a
    /// general-knowledge answer as if it came from the user's own history.
    /// </summary>
    public string Grounding { get; set; } = AssistantGrounding.None;

    /// <summary>
    /// Events the answer rests on. Empty is legitimate and meaningful: the
    /// Windows Ask feature refuses honestly when the archive has nothing
    /// relevant, and that refusal arrives as a completed turn with no citations.
    /// </summary>
    public List<AssistantCitation> Citations { get; set; } = new();

    /// <summary>Model that produced it, for display and diagnostics.</summary>
    public string? Model { get; set; }

    /// <summary>Wall-clock milliseconds the responder took.</summary>
    public int? ElapsedMs { get; set; }
}

/// <summary>An event an answer cites.</summary>
public class AssistantCitation
{
    public string EventId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Precision-honest display date ("Summer 2003"), already formatted by the
    /// responder. The wire carries the string rather than a date plus precision
    /// enum so every client renders exactly what Windows would.
    /// </summary>
    public string? DisplayDate { get; set; }

    /// <summary>Bounded excerpt supporting the claim — never the full event (§14.5).</summary>
    public string? Excerpt { get; set; }
}

/// <summary>
/// Payload carried by an <c>assistant_turn</c> change. Direction depends on who
/// wrote it: the service publishes a pending turn toward Windows, and Windows
/// publishes the answered turn back. Both are the same shape so a consumer can
/// apply the newest revision without caring which direction it came from.
///
/// Delivery reuses the existing change feed deliberately rather than adding a
/// second transport: Windows already runs a pull/ack loop and an outbox, so an
/// assistant turn is just another entity type riding proven plumbing.
/// </summary>
public class AssistantTurnChangePayload
{
    public string TurnId { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public int Sequence { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Status { get; set; } = AssistantTurnStatus.Pending;

    public string RequestedResponder { get; set; } = AssistantResponder.Windows;

    public string? ActualResponder { get; set; }

    /// <summary>Device that asked, so Windows can scope retrieval and reply.</summary>
    public string OriginDeviceId { get; set; } = string.Empty;

    /// <summary>Device-supplied grounding, when the turn carried any.</summary>
    public List<AssistantContextItem>? ClientContext { get; set; }

    /// <summary>Present once answered.</summary>
    public AssistantTurnResult? Result { get; set; }

    public string? FailureReason { get; set; }

    public bool? FailureRetryable { get; set; }

    /// <summary>
    /// When the publisher produced this revision. Consumers apply in cursor
    /// order; this is for display and last-write-wins tie-breaking, exactly as
    /// <c>CaptureStatusChangePayload.UpdatedAtUtc</c> is used.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// One incremental chunk of a streaming answer, carried by an
/// <c>assistant_turn_chunk</c> change.
///
/// Streaming is expressed as ordered chunks over the same feed rather than as a
/// long-lived connection, because the phone's transport is already a pull loop
/// that survives suspension and network changes. A responder that cannot stream
/// simply never publishes chunks, and the client renders the final result.
/// </summary>
public class AssistantTurnChunkPayload
{
    public string TurnId { get; set; } = string.Empty;

    /// <summary>Zero-based, contiguous per turn, so a client can detect a gap.</summary>
    public int ChunkIndex { get; set; }

    /// <summary>Text to append. Never a rewrite of what came before.</summary>
    public string Delta { get; set; } = string.Empty;

    /// <summary>True on the last chunk; the completed turn follows with citations.</summary>
    public bool IsFinal { get; set; }
}
