namespace MemoryTimeline.Core.Services;

/// <summary>
/// Per-session LLM usage counter (calls + estimated tokens), surfaced in the
/// Settings "AI &amp; Language Models" card so users can see how often the app
/// talks to an LLM. In-memory only — counters reset when the app restarts.
/// Recorded by <see cref="RoutingLlmService"/> for every LLM call it routes.
/// </summary>
public interface ILlmUsageTracker
{
    /// <summary>Number of LLM calls made this session.</summary>
    int CallCount { get; }

    /// <summary>
    /// Estimated total tokens this session. Real token counts are used when the
    /// provider reports them; otherwise a ~4-characters-per-token estimate.
    /// </summary>
    long EstimatedTokens { get; }

    /// <summary>Records one LLM call.</summary>
    /// <param name="providerName">Display name of the provider that served the call.</param>
    /// <param name="estimatedTokens">Real or estimated token count for the call.</param>
    void RecordCall(string providerName, long estimatedTokens);
}

/// <summary>
/// Thread-safe in-memory implementation (registered as a singleton).
/// </summary>
public class LlmUsageTracker : ILlmUsageTracker
{
    private int _callCount;
    private long _estimatedTokens;

    public int CallCount => Volatile.Read(ref _callCount);

    public long EstimatedTokens => Interlocked.Read(ref _estimatedTokens);

    public void RecordCall(string providerName, long estimatedTokens)
    {
        Interlocked.Increment(ref _callCount);
        if (estimatedTokens > 0)
        {
            Interlocked.Add(ref _estimatedTokens, estimatedTokens);
        }
    }

    /// <summary>
    /// Rough token estimate for text where the provider reported no usage
    /// (~4 characters per token for English prose).
    /// </summary>
    public static long EstimateTokens(string? text)
    {
        return string.IsNullOrEmpty(text) ? 0 : (text.Length + 3) / 4;
    }
}
