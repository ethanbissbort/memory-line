using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// LLM service implementation using Anthropic Claude API via SDK.
/// Uses Anthropic.SDK v5.8.0 for improved reliability and type safety.
/// </summary>
public class AnthropicClaudeService : ILlmService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AnthropicClaudeService> _logger;
    private AnthropicClient? _client;
    private string? _apiKey;
    private string _model;

    private const string DefaultModel = "claude-3-5-sonnet-20241022";

    public string ProviderName => "Anthropic Claude";
    public string ModelName => _model;
    public bool RequiresInternet => true;

    public AnthropicClaudeService(
        ILogger<AnthropicClaudeService> logger,
        ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
        _model = DefaultModel;
    }

    /// <summary>
    /// Extracts events from a transcript.
    /// </summary>
    public async Task<EventExtractionResult> ExtractEventsAsync(string transcript)
    {
        return await ExtractEventsAsync(transcript, null);
    }

    /// <summary>
    /// Extracts events from a transcript with context.
    /// </summary>
    public async Task<EventExtractionResult> ExtractEventsAsync(string transcript, ExtractionContext? context)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new EventExtractionResult();

        try
        {
            // Initialize client if needed
            await InitializeClientAsync();

            if (_client == null || string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogError("Anthropic API client not initialized or API key not configured");
                result.Success = false;
                result.ErrorMessage = "Anthropic API key not configured. Please add your API key in Settings.";
                return result;
            }

            _logger.LogInformation("Extracting events from transcript using {Model}", _model);

            // Build the prompts
            var systemPrompt = BuildSystemPrompt(context);
            var userPrompt = BuildUserPrompt(transcript);

            // Create messages list with system message as first user message
            // (Anthropic.SDK v5.8.0 doesn't have separate system parameter in MessageParameters)
            var messages = new List<Message>
            {
                new Message(RoleType.User, $"{systemPrompt}\n\n{userPrompt}")
            };

            // Create API request using SDK
            var parameters = new MessageParameters
            {
                Model = _model,
                MaxTokens = 4096,
                Temperature = 0.0m, // Deterministic for structured extraction
                Messages = messages,
                Stream = false
            };

            // Call API using SDK
            var response = await _client.Messages.GetClaudeMessageAsync(parameters);

            if (response?.Message?.Content == null || !response.Message.Content.Any())
            {
                throw new Exception("Empty response from Claude API");
            }

            // Extract and parse JSON from response
            var textContent = response.Message.Content
                .FirstOrDefault(c => c is TextContent) as TextContent;

            if (textContent == null)
            {
                throw new Exception("No text content in Claude API response");
            }

            var jsonContent = textContent.Text;
            result = ParseExtractionResult(jsonContent);

            // Set metadata
            result.ProcessingDurationSeconds = stopwatch.Elapsed.TotalSeconds;
            result.TokenUsage = new TokenUsage
            {
                InputTokens = response.Usage?.InputTokens ?? 0,
                OutputTokens = response.Usage?.OutputTokens ?? 0,
                EstimatedCost = CalculateCost(response.Usage)
            };

            _logger.LogInformation("Successfully extracted {Count} events in {Duration}s",
                result.Events.Count, result.ProcessingDurationSeconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting events from transcript");

            stopwatch.Stop();
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ProcessingDurationSeconds = stopwatch.Elapsed.TotalSeconds;

            return result;
        }
    }

    #region Initialization

    /// <summary>
    /// Initializes the Anthropic client with API key from settings.
    /// </summary>
    private async Task InitializeClientAsync()
    {
        if (_client != null)
            return;

        try
        {
            _apiKey = await _settingsService.GetSettingAsync<string>("AnthropicApiKey", string.Empty);

            // Fallback to generic ApiKey if AnthropicApiKey not found
            if (string.IsNullOrEmpty(_apiKey))
            {
                _apiKey = await _settingsService.GetSettingAsync<string>("ApiKey", string.Empty);
            }

            var modelSetting = await _settingsService.GetSettingAsync<string>("AnthropicModel", DefaultModel);
            if (!string.IsNullOrEmpty(modelSetting))
            {
                _model = modelSetting;
            }

            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("No API key configured in settings");
                return;
            }

            // Initialize SDK client
            var authentication = new APIAuthentication(_apiKey);
            _client = new AnthropicClient(authentication);

            _logger.LogInformation("Anthropic SDK client initialized with model: {Model}", _model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Anthropic client");
        }
    }

    #endregion

    #region Prompt Engineering

    private string BuildSystemPrompt(ExtractionContext? context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an expert at extracting life events from conversational transcripts.");
        sb.AppendLine("Your task is to identify significant events, milestones, and memorable moments.");
        sb.AppendLine();
        sb.AppendLine("Guidelines:");
        sb.AppendLine("- Extract events with clear start dates (and end dates if mentioned)");
        sb.AppendLine("- Include relevant details: people, locations, descriptions");
        sb.AppendLine("- Categorize events: Milestone, Work, Education, Health, Travel, Social, Personal, Family, Other");
        sb.AppendLine("- Suggest relevant tags (max 5 per event)");
        sb.AppendLine("- Assign confidence scores based on clarity and specificity");
        sb.AppendLine("- Skip vague or unclear mentions");
        sb.AppendLine();

        if (context != null)
        {
            if (context.ReferenceDate.HasValue)
            {
                sb.AppendLine($"Reference Date: {context.ReferenceDate.Value:yyyy-MM-dd}");
                sb.AppendLine("Use this for relative date parsing (e.g., 'yesterday', 'last week').");
                sb.AppendLine();
            }

            if (context.AvailableTags?.Count > 0)
            {
                sb.AppendLine($"Known Tags: {string.Join(", ", context.AvailableTags.Take(20))}");
                sb.AppendLine("Prefer these tags when applicable for consistency.");
                sb.AppendLine();
            }

            if (context.KnownPeople?.Count > 0)
            {
                sb.AppendLine($"Known People: {string.Join(", ", context.KnownPeople.Take(20))}");
                sb.AppendLine();
            }

            if (context.KnownLocations?.Count > 0)
            {
                sb.AppendLine($"Known Locations: {string.Join(", ", context.KnownLocations.Take(20))}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("Output Format: Respond with valid JSON only, no markdown formatting.");
        sb.AppendLine(@"{
  ""events"": [
    {
      ""title"": ""Event title (concise, 5-10 words)"",
      ""description"": ""Detailed description with context"",
      ""startDate"": ""2024-01-15T00:00:00Z"",
      ""endDate"": ""2024-01-20T00:00:00Z"" or null,
      ""category"": ""Milestone"",
      ""tags"": [""tag1"", ""tag2""],
      ""people"": [""Person Name""],
      ""locations"": [""Location Name""],
      ""confidence"": 0.95,
      ""sourceText"": ""Relevant excerpt from transcript"",
      ""reasoning"": ""Why this qualifies as an event""
    }
  ],
  ""overallConfidence"": 0.9
}");

        return sb.ToString();
    }

    private string BuildUserPrompt(string transcript)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Extract all significant events from this transcript:");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(transcript);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Return the extracted events in the specified JSON format.");

        return sb.ToString();
    }

    #endregion

    #region Response Parsing

    private EventExtractionResult ParseExtractionResult(string jsonContent)
    {
        try
        {
            // Remove markdown code blocks if present
            jsonContent = jsonContent.Trim();
            if (jsonContent.StartsWith("```json"))
            {
                jsonContent = jsonContent[7..];
            }
            else if (jsonContent.StartsWith("```"))
            {
                jsonContent = jsonContent[3..];
            }

            if (jsonContent.EndsWith("```"))
            {
                jsonContent = jsonContent[..^3];
            }

            jsonContent = jsonContent.Trim();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var parsed = JsonSerializer.Deserialize<ExtractionJsonResponse>(jsonContent, options);

            if (parsed == null)
            {
                throw new Exception("Failed to deserialize extraction result");
            }

            return new EventExtractionResult
            {
                Events = parsed.Events ?? new List<ExtractedEvent>(),
                OverallConfidence = parsed.OverallConfidence,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing extraction result: {Json}", jsonContent);

            return new EventExtractionResult
            {
                Success = false,
                ErrorMessage = $"Failed to parse extraction result: {ex.Message}"
            };
        }
    }

    private decimal CalculateCost(Anthropic.SDK.Messaging.Usage? usage)
    {
        if (usage == null) return 0m;

        // Claude 3.5 Sonnet pricing (as of 2024)
        const decimal inputCostPer1M = 3.00m;    // $3 per million tokens
        const decimal outputCostPer1M = 15.00m;  // $15 per million tokens

        var inputCost = (usage.InputTokens / 1_000_000m) * inputCostPer1M;
        var outputCost = (usage.OutputTokens / 1_000_000m) * outputCostPer1M;

        return inputCost + outputCost;
    }

    #endregion

    #region DTOs

    private class ExtractionJsonResponse
    {
        public List<ExtractedEvent>? Events { get; set; }
        public double OverallConfidence { get; set; }
    }

    #endregion
}
