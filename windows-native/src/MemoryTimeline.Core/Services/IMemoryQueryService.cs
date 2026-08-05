namespace MemoryTimeline.Core.Services;

/// <summary>
/// Conversational retrieval over the memory archive ("Ask your timeline").
/// Runs hybrid keyword + semantic retrieval, then produces a grounded answer
/// that cites the retrieved events and admits ignorance when the archive does
/// not contain the answer.
/// </summary>
public interface IMemoryQueryService
{
    /// <summary>
    /// Answers a natural-language question from the archive.
    /// </summary>
    /// <param name="question">The user's question</param>
    /// <param name="options">Optional retrieval options; falls back to the
    /// <c>ask_top_k</c> / <c>ask_include_transcripts</c> settings when null</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The grounded answer with citations</returns>
    /// <exception cref="ConfigurationException">No Anthropic API key is configured</exception>
    Task<MemoryAnswer> AskAsync(string question, MemoryQueryOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Streaming variant of <see cref="AskAsync"/>: yields the answer text in
    /// word-sized chunks. Citations and metadata are not part of the stream —
    /// use <see cref="AskAsync"/> when they are needed.
    /// </summary>
    IAsyncEnumerable<string> AskStreamingAsync(string question, MemoryQueryOptions? options = null, CancellationToken ct = default);
}

/// <summary>
/// Options controlling retrieval for a single question.
/// </summary>
public class MemoryQueryOptions
{
    /// <summary>Number of events supplied to the answering model.</summary>
    public int TopK { get; set; } = 12;

    /// <summary>Optional lower date bound applied to retrieval.</summary>
    public DateTime? From { get; set; }

    /// <summary>Optional upper date bound applied to retrieval.</summary>
    public DateTime? To { get; set; }

    /// <summary>Whether raw transcripts are included in the answer context.</summary>
    public bool IncludeTranscripts { get; set; } = false;
}

/// <summary>
/// A grounded answer produced from the archive.
/// </summary>
public class MemoryAnswer
{
    /// <summary>The question that was asked.</summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>The display answer (citation markers replaced with [n] references).</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>Cited events, in the order they are referenced by the answer.</summary>
    public List<CitedEvent> Citations { get; set; } = new();

    /// <summary>False when the archive did not contain the answer (honest refusal).</summary>
    public bool AnsweredFromArchive { get; set; }

    /// <summary>Model-reported confidence, clamped to 0..1.</summary>
    public double Confidence { get; set; }

    /// <summary>Ids of every event supplied to the answering model.</summary>
    public List<string> RetrievedEventIds { get; set; } = new();

    /// <summary>False when semantic retrieval was unavailable (keyword-only degraded mode).</summary>
    public bool UsedSemanticRetrieval { get; set; }
}

/// <summary>
/// A single cited event supporting the answer.
/// </summary>
public sealed record CitedEvent(string EventId, string Title, DateTime StartDate, string Snippet);
