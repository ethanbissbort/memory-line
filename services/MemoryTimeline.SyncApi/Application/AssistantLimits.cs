namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Size bounds the service enforces on assistant traffic.
///
/// These live here rather than in the shared contracts because the contract
/// deliberately says the bounds are the service's ("Bounded by the service") —
/// a client cannot be trusted to have checked, and a second service could
/// reasonably choose different numbers. They are public so the tests assert
/// against the same values the service enforces instead of hard-coding a copy
/// that can drift.
///
/// <para><b>Everything here rejects; nothing truncates.</b> An over-long
/// question, an oversized client context or an over-long answer is refused with
/// a validation error rather than trimmed to fit. Truncation would mean the
/// user is answered on a fraction of what they sent — or hears a sentence that
/// stops mid-thought — with no signal that anything was dropped, and the
/// caller, unlike the service, can actually decide what to leave out.</para>
/// </summary>
public static class AssistantLimits
{
    /// <summary>Maximum characters in a question. Generous for dictated speech, far short of a pasted document.</summary>
    public const int QuestionMaxChars = 4_000;

    /// <summary>Maximum characters in the diagnostic surface label ("carplay", "watch").</summary>
    public const int SurfaceMaxChars = 64;

    /// <summary>Maximum device-supplied context items on one turn.</summary>
    public const int ClientContextMaxItems = 20;

    /// <summary>Maximum characters in one context item's excerpt.</summary>
    public const int ClientContextExcerptMaxChars = 1_000;

    /// <summary>Maximum characters in one context item's title.</summary>
    public const int ClientContextTitleMaxChars = 300;

    /// <summary>
    /// Maximum characters across the whole client context (titles plus
    /// excerpts). Enforced in addition to the per-item bounds so the total a
    /// responder has to handle is capped no matter how the client distributes
    /// it between many small items and few large ones.
    /// </summary>
    public const int ClientContextMaxTotalChars = 20_000;

    /// <summary>Maximum characters in a pushed answer.</summary>
    public const int AnswerMaxChars = 20_000;

    /// <summary>Maximum citations one answer may carry.</summary>
    public const int CitationMaxCount = 50;

    /// <summary>Maximum characters in one citation's excerpt (§14.5 — an excerpt, never the event).</summary>
    public const int CitationExcerptMaxChars = 1_000;

    /// <summary>Maximum characters in a streaming chunk's delta.</summary>
    public const int ChunkDeltaMaxChars = 4_000;

    /// <summary>Maximum characters in a display-safe failure reason.</summary>
    public const int FailureReasonMaxChars = 500;
}
