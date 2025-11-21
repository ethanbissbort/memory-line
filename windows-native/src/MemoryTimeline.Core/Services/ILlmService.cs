namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for LLM-based event extraction.
/// </summary>
public interface ILlmService
{
    Task<string> ExtractEventsAsync(string transcript);
}

/// <summary>
/// LLM service using Anthropic Claude API.
/// </summary>
public class LlmService : ILlmService
{
    public Task<string> ExtractEventsAsync(string transcript)
    {
        // TODO: Implement using Anthropic.SDK
        throw new NotImplementedException();
    }
}
