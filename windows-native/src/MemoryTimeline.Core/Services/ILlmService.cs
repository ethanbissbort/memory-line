namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for Large Language Model operations.
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Extracts events from a transcript text.
    /// </summary>
    /// <param name="transcript">The transcript text to analyze</param>
    /// <returns>Extraction result with detected events</returns>
    Task<EventExtractionResult> ExtractEventsAsync(string transcript);

    /// <summary>
    /// Extracts events from a transcript with additional context.
    /// </summary>
    /// <param name="transcript">The transcript text to analyze</param>
    /// <param name="context">Additional context (e.g., existing events, tags)</param>
    /// <returns>Extraction result with detected events</returns>
    Task<EventExtractionResult> ExtractEventsAsync(string transcript, ExtractionContext? context);

    /// <summary>
    /// Gets the name of the LLM provider.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets the model name being used.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Gets whether the service requires internet connectivity.
    /// </summary>
    bool RequiresInternet { get; }
}

/// <summary>
/// Context for event extraction to improve accuracy.
/// </summary>
public class ExtractionContext
{
    /// <summary>
    /// Recent events for temporal context.
    /// </summary>
    public List<string>? RecentEvents { get; set; }

    /// <summary>
    /// Available tags for consistency.
    /// </summary>
    public List<string>? AvailableTags { get; set; }

    /// <summary>
    /// People mentioned in recent events.
    /// </summary>
    public List<string>? KnownPeople { get; set; }

    /// <summary>
    /// Locations from recent events.
    /// </summary>
    public List<string>? KnownLocations { get; set; }

    /// <summary>
    /// Current date for relative date parsing.
    /// </summary>
    public DateTime? ReferenceDate { get; set; }
}

/// <summary>
/// Result of event extraction from transcript.
/// </summary>
public class EventExtractionResult
{
    /// <summary>
    /// List of extracted events.
    /// </summary>
    public List<ExtractedEvent> Events { get; set; } = new();

    /// <summary>
    /// Overall confidence score (0.0 to 1.0).
    /// </summary>
    public double OverallConfidence { get; set; }

    /// <summary>
    /// Whether the extraction was successful.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if extraction failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Processing duration in seconds.
    /// </summary>
    public double ProcessingDurationSeconds { get; set; }

    /// <summary>
    /// Token usage for cost tracking.
    /// </summary>
    public TokenUsage? TokenUsage { get; set; }
}

/// <summary>
/// An extracted event from the transcript.
/// </summary>
public class ExtractedEvent
{
    /// <summary>
    /// Event title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Event description/details.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Start date of the event.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// End date for duration events.
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Event category.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Suggested tags.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// People involved.
    /// </summary>
    public List<string> People { get; set; } = new();

    /// <summary>
    /// Locations mentioned.
    /// </summary>
    public List<string> Locations { get; set; } = new();

    /// <summary>
    /// Confidence score for this event (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Portion of transcript that mentions this event.
    /// </summary>
    public string? SourceText { get; set; }

    /// <summary>
    /// Reasoning for extraction (for debugging/review).
    /// </summary>
    public string? Reasoning { get; set; }
}

/// <summary>
/// Token usage information for API calls.
/// </summary>
public class TokenUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens => InputTokens + OutputTokens;
    public decimal? EstimatedCost { get; set; }
}
