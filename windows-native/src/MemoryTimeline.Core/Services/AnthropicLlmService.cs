using Anthropic.SDK;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Diagnostics;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// LLM service using Anthropic Claude for event extraction.
/// </summary>
public class AnthropicLlmService : ILlmService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AnthropicLlmService> _logger;
    private AnthropicClient? _client;
    private string? _model;

    public string ProviderName => "Anthropic Claude";
    public string ModelName => _model ?? "claude-3-5-sonnet-20241022";
    public bool RequiresInternet => true;

    public AnthropicLlmService(
        ISettingsService settingsService,
        ILogger<AnthropicLlmService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Extracts events from transcript text.
    /// </summary>
    public async Task<EventExtractionResult> ExtractEventsAsync(string transcript)
    {
        return await ExtractEventsAsync(transcript, null);
    }

    /// <summary>
    /// Extracts events from transcript with context.
    /// </summary>
    public async Task<EventExtractionResult> ExtractEventsAsync(string transcript, ExtractionContext? context)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Starting event extraction from transcript ({Length} chars)", transcript.Length);

            // Initialize client if needed
            await InitializeClientAsync();

            if (_client == null)
            {
                return new EventExtractionResult
                {
                    Success = false,
                    ErrorMessage = "Anthropic API client not initialized. Check API key in settings."
                };
            }

            // Build the prompt
            var prompt = BuildExtractionPrompt(transcript, context);

            _logger.LogDebug("Sending request to Claude API");

            // Call Claude API using the SDK
            var messages = new List<Message>
            {
                new Message(RoleType.User, prompt)
            };

            var parameters = new MessageParameters
            {
                Messages = messages,
                Model = ModelName,
                MaxTokens = 4096,
                Temperature = 0.3m, // Lower temperature for more consistent structured output
                Stream = false
            };

            var response = await _client.Messages.GetClaudeMessageAsync(ModelName, parameters);

            _logger.LogInformation("Received response from Claude API");

            // Extract JSON from response
            var jsonResponse = ExtractJsonFromResponse(response);

            if (string.IsNullOrEmpty(jsonResponse))
            {
                return new EventExtractionResult
                {
                    Success = false,
                    ErrorMessage = "No valid JSON found in Claude's response"
                };
            }

            // Parse the JSON response
            var extractedData = JsonSerializer.Deserialize<EventExtractionResponse>(jsonResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (extractedData == null)
            {
                return new EventExtractionResult
                {
                    Success = false,
                    ErrorMessage = "Failed to parse extraction response"
                };
            }

            stopwatch.Stop();

            var result = new EventExtractionResult
            {
                Events = extractedData.Events ?? new List<ExtractedEvent>(),
                OverallConfidence = extractedData.OverallConfidence,
                Success = true,
                ProcessingDurationSeconds = stopwatch.Elapsed.TotalSeconds,
                TokenUsage = new TokenUsage
                {
                    InputTokens = response.Usage?.InputTokens ?? 0,
                    OutputTokens = response.Usage?.OutputTokens ?? 0,
                    EstimatedCost = CalculateEstimatedCost(
                        response.Usage?.InputTokens ?? 0,
                        response.Usage?.OutputTokens ?? 0)
                }
            };

            _logger.LogInformation("Successfully extracted {Count} events (confidence: {Confidence})",
                result.Events.Count, result.OverallConfidence);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during event extraction");

            stopwatch.Stop();

            return new EventExtractionResult
            {
                Success = false,
                ErrorMessage = $"Extraction failed: {ex.Message}",
                ProcessingDurationSeconds = stopwatch.Elapsed.TotalSeconds
            };
        }
    }

    #region Private Methods

    /// <summary>
    /// Initializes the Anthropic client with API key from settings.
    /// </summary>
    private async Task InitializeClientAsync()
    {
        if (_client != null)
            return;

        try
        {
            var apiKey = await _settingsService.GetSettingAsync<string>("ApiKey", string.Empty);
            _model = await _settingsService.GetLlmModelAsync();

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("No API key configured in settings");
                return;
            }

            _client = new AnthropicClient(new APIAuthentication(apiKey));
            _logger.LogInformation("Anthropic client initialized with model: {Model}", _model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Anthropic client");
        }
    }

    /// <summary>
    /// Builds the extraction prompt for Claude.
    /// </summary>
    private string BuildExtractionPrompt(string transcript, ExtractionContext? context)
    {
        var referenceDate = context?.ReferenceDate ?? DateTime.Now;

        var prompt = $@"You are an expert at extracting structured event information from transcribed speech. Your task is to analyze the following transcript and extract all mentioned events with their details.

# Instructions:
1. Identify ALL events mentioned in the transcript (meetings, milestones, accomplishments, significant occurrences)
2. For each event, extract:
   - Title (concise, descriptive)
   - Description (detailed information from the transcript)
   - Start date (parse relative dates like 'yesterday', 'last week', 'two months ago')
   - End date (if the event has duration)
   - Category (Milestone, Work, Education, Health, Travel, Social, Personal, Family, or Other)
   - Tags (relevant keywords)
   - People involved (names mentioned)
   - Locations mentioned
   - Confidence score (0.0 to 1.0 based on clarity of information)
   - Source text (the exact portion of transcript about this event)
   - Reasoning (brief explanation of why you extracted this as an event)

3. Parse dates relative to: {referenceDate:yyyy-MM-dd}
4. Be thorough but only extract genuine events, not hypotheticals or general discussions
5. Assign confidence scores based on:
   - 0.9-1.0: Explicit dates and clear details
   - 0.7-0.9: Clear event with approximate dates
   - 0.5-0.7: Event is clear but dates are vague
   - Below 0.5: Uncertain or ambiguous

# Context:";

        if (context?.RecentEvents != null && context.RecentEvents.Any())
        {
            prompt += "\nRecent events for reference:\n" + string.Join("\n", context.RecentEvents.Take(10).Select(e => $"- {e}"));
        }

        if (context?.AvailableTags != null && context.AvailableTags.Any())
        {
            prompt += "\n\nAvailable tags: " + string.Join(", ", context.AvailableTags.Take(20));
        }

        prompt += $@"

# Transcript:
{transcript}

# Output Format:
Return a JSON object with this exact structure (no markdown, just raw JSON):
{{
  ""events"": [
    {{
      ""title"": ""Event Title"",
      ""description"": ""Detailed description"",
      ""startDate"": ""2024-01-15T10:00:00Z"",
      ""endDate"": ""2024-01-15T12:00:00Z"",
      ""category"": ""Work"",
      ""tags"": [""tag1"", ""tag2""],
      ""people"": [""Person Name""],
      ""locations"": [""Location Name""],
      ""confidence"": 0.95,
      ""sourceText"": ""relevant portion of transcript"",
      ""reasoning"": ""why this is an event""
    }}
  ],
  ""overallConfidence"": 0.85
}}

Now analyze the transcript and extract events:";

        return prompt;
    }

    /// <summary>
    /// Extracts JSON from Claude's response (handles markdown code blocks).
    /// </summary>
    private string ExtractJsonFromResponse(dynamic response)
    {
        if (response?.Content == null)
            return string.Empty;

        // Get the text content - try to find the first content item with text
        try
        {
            var content = response.Content.FirstOrDefault();
            if (content == null)
                return string.Empty;

            var text = content?.Text?.ToString()?.Trim() ?? string.Empty;

            // Remove markdown code fences if present
            if (text.StartsWith("```json"))
            {
                text = text.Substring(7);
            }
            else if (text.StartsWith("```"))
            {
                text = text.Substring(3);
            }

            if (text.EndsWith("```"))
            {
                text = text.Substring(0, text.Length - 3);
            }

            return text.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Calculates estimated API cost based on token usage.
    /// Claude 3.5 Sonnet pricing: $3/MTok input, $15/MTok output
    /// </summary>
    private decimal CalculateEstimatedCost(int inputTokens, int outputTokens)
    {
        const decimal inputCostPerMillion = 3.00m;
        const decimal outputCostPerMillion = 15.00m;

        var inputCost = (inputTokens / 1_000_000m) * inputCostPerMillion;
        var outputCost = (outputTokens / 1_000_000m) * outputCostPerMillion;

        return inputCost + outputCost;
    }

    #endregion

    #region Response Models

    /// <summary>
    /// Response model for JSON deserialization.
    /// </summary>
    private class EventExtractionResponse
    {
        public List<ExtractedEvent>? Events { get; set; }
        public double OverallConfidence { get; set; }
    }

    #endregion
}
