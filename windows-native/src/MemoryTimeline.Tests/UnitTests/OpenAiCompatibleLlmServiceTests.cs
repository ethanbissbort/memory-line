using FluentAssertions;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using Moq;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for the OpenAI-compatible chat-completions LLM service (F11): fixture
/// response parsing into the SAME EventExtractionResult shape as the Anthropic
/// path (both run through the shared LlmPromptSupport helper), configuration
/// errors, and request shape.
/// </summary>
public class OpenAiCompatibleLlmServiceTests
{
    private const string BaseUrl = "http://localhost:11434/v1";

    /// <summary>
    /// The extraction payload a model would produce — used both as the
    /// chat-completions message content and to assert shared-helper parity.
    /// </summary>
    private const string ExtractionPayloadJson = """
        {
          "events": [
            {
              "title": "Started new job",
              "description": "First day at the new company",
              "startDate": "2024-01-15T00:00:00Z",
              "datePrecision": "day",
              "category": "work",
              "tags": ["career"],
              "people": ["Sarah"],
              "locations": ["Seattle"],
              "confidence": 0.92,
              "sourceText": "I started my new job",
              "reasoning": "explicit event"
            }
          ],
          "overallConfidence": 0.9
        }
        """;

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// Builds a chat-completions fixture whose message content wraps the
    /// extraction payload in markdown fences (proving fence-stripping works).
    /// </summary>
    private static string BuildChatCompletionsFixture(string innerContent)
    {
        var fixture = new
        {
            id = "chatcmpl-fixture",
            @object = "chat.completion",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = innerContent },
                    finish_reason = "stop"
                }
            },
            usage = new { prompt_tokens = 50, completion_tokens = 20, total_tokens = 70 }
        };
        return JsonSerializer.Serialize(fixture);
    }

    private static Mock<ISettingsService> CreateSettingsMock(string baseUrl, string model = "llama3")
    {
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.GetSettingAsync<string>(SettingKeys.LlmBaseUrl, It.IsAny<string>()))
            .ReturnsAsync(baseUrl);
        settingsMock.Setup(s => s.GetLlmModelAsync()).ReturnsAsync(model);
        return settingsMock;
    }

    private static OpenAiCompatibleLlmService CreateService(
        Mock<ISettingsService> settingsMock, StubHttpMessageHandler handler)
    {
        return new OpenAiCompatibleLlmService(
            new HttpClient(handler),
            settingsMock.Object,
            new Mock<ILogger<OpenAiCompatibleLlmService>>().Object);
    }

    #region Fixture parsing

    [Fact]
    public async Task ExtractEventsAsync_ChatCompletionsFixture_ParsesIntoExtractionResult()
    {
        // Arrange - the model wraps its JSON in markdown fences
        var fencedContent = "```json\n" + ExtractionPayloadJson + "\n```";
        var handler = new StubHttpMessageHandler(_ => JsonResponse(BuildChatCompletionsFixture(fencedContent)));
        var service = CreateService(CreateSettingsMock(BaseUrl), handler);

        // Act
        var result = await service.ExtractEventsAsync("I started my new job in Seattle with Sarah");

        // Assert - same EventExtractionResult shape as the Anthropic path
        result.Success.Should().BeTrue();
        result.Events.Should().ContainSingle();
        result.Events[0].Title.Should().Be("Started new job");
        result.Events[0].Category.Should().Be("work");
        result.Events[0].DatePrecision.Should().Be("day");
        result.Events[0].People.Should().Equal("Sarah");
        result.Events[0].Confidence.Should().Be(0.92);
        result.OverallConfidence.Should().Be(0.9);
        result.TokenUsage.Should().NotBeNull();
        result.TokenUsage!.InputTokens.Should().Be(50);
        result.TokenUsage.OutputTokens.Should().Be(20);
        result.TokenUsage.EstimatedCost.Should().BeNull(); // no price list for self-hosted endpoints
    }

    [Fact]
    public async Task ExtractEventsAsync_SharedHelper_ProducesIdenticalPayloadForBothProviders()
    {
        // Arrange - run the SAME inner payload through the service (HTTP path)
        // and directly through the shared helper (the Anthropic path's parse):
        // both must yield identical extracted data, proving the parse cannot fork.
        var handler = new StubHttpMessageHandler(_ => JsonResponse(BuildChatCompletionsFixture(ExtractionPayloadJson)));
        var service = CreateService(CreateSettingsMock(BaseUrl), handler);

        // Act
        var viaService = await service.ExtractEventsAsync("transcript");
        var viaSharedHelper = JsonSerializer.Deserialize<LlmPromptSupport.EventExtractionResponse>(
            LlmPromptSupport.ExtractJsonFromText("```json\n" + ExtractionPayloadJson + "\n```"),
            LlmPromptSupport.JsonOptions);

        // Assert
        viaService.Success.Should().BeTrue();
        viaSharedHelper.Should().NotBeNull();
        viaService.Events.Should().BeEquivalentTo(viaSharedHelper!.Events);
        viaService.OverallConfidence.Should().Be(viaSharedHelper.OverallConfidence);
    }

    [Fact]
    public void BuildExtractionPrompt_SameInputs_ProducesIdenticalTextForBothProviders()
    {
        // Arrange - both services call LlmPromptSupport.BuildExtractionPrompt;
        // two calls with the same inputs must be byte-identical (no forking).
        var context = new ExtractionContext
        {
            ReferenceDate = new DateTime(2026, 8, 6),
            RecentEvents = new List<string> { "Graduated" },
            KnownPeople = new List<string> { "Sarah" }
        };

        // Act
        var promptA = LlmPromptSupport.BuildExtractionPrompt("my transcript", context);
        var promptB = LlmPromptSupport.BuildExtractionPrompt("my transcript", context);

        // Assert
        promptA.Should().Be(promptB);
        promptA.Should().Contain("my transcript");
        promptA.Should().Contain("2026-08-06");
        promptA.Should().Contain("Sarah");
        promptA.Should().StartWith("You are an expert at extracting structured event information");
    }

    [Fact]
    public async Task ExtractEventsAsync_MalformedInnerJson_ReturnsFailureResult()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ => JsonResponse(BuildChatCompletionsFixture("{ not json")));
        var service = CreateService(CreateSettingsMock(BaseUrl), handler);

        // Act
        var result = await service.ExtractEventsAsync("transcript");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("malformed JSON");
    }

    #endregion

    #region Request shape

    [Fact]
    public async Task ExtractEventsAsync_PostsToChatCompletionsWithExpectedBody()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ => JsonResponse(BuildChatCompletionsFixture(ExtractionPayloadJson)));
        var service = CreateService(CreateSettingsMock(BaseUrl + "/", "llama3"), handler);

        // Act
        await service.ExtractEventsAsync("my transcript");

        // Assert - trailing slash on the base URL is normalized away
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.ToString().Should().Be(BaseUrl + "/chat/completions");
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);

        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().Contain("\"model\":\"llama3\"");
        handler.LastRequestBody.Should().Contain("\"stream\":false");
        handler.LastRequestBody.Should().Contain("\"role\":\"user\"");
        handler.LastRequestBody.Should().Contain("You are an expert at extracting structured event information");
    }

    [Fact]
    public async Task CompleteAsync_SendsSystemPromptAsSystemRoleMessage()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ => JsonResponse(BuildChatCompletionsFixture("plain answer")));
        var service = CreateService(CreateSettingsMock(BaseUrl), handler);

        // Act
        var response = await service.CompleteAsync("be terse", "what happened?", maxTokens: 512);

        // Assert
        response.Should().Be("plain answer");
        handler.LastRequestBody.Should().Contain("\"role\":\"system\"");
        handler.LastRequestBody.Should().Contain("be terse");
        handler.LastRequestBody.Should().Contain("\"max_tokens\":512");
    }

    #endregion

    #region Configuration errors

    [Fact]
    public async Task ExtractEventsAsync_MissingBaseUrl_ThrowsConfigurationException()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        var service = CreateService(CreateSettingsMock(string.Empty), handler);

        // Act
        var act = () => service.ExtractEventsAsync("transcript");

        // Assert - non-retryable for the queue, and no HTTP call was made
        await act.Should().ThrowAsync<ConfigurationException>()
            .WithMessage("*base URL*");
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_MissingBaseUrl_ThrowsConfigurationException()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{}"));
        var service = CreateService(CreateSettingsMock("   "), handler);

        // Act
        var act = () => service.CompleteAsync("system", "user");

        // Assert
        await act.Should().ThrowAsync<ConfigurationException>()
            .WithMessage("*base URL*");
    }

    [Fact]
    public async Task ExtractEventsAsync_HttpFailure_ReturnsFailureResultInsteadOfThrowing()
    {
        // Arrange - endpoint down / wrong URL
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var service = CreateService(CreateSettingsMock(BaseUrl), handler);

        // Act
        var result = await service.ExtractEventsAsync("transcript");

        // Assert - mirrors the Anthropic path: transport failures become an
        // unsuccessful result (retryable by the queue), never a crash
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Extraction failed");
    }

    #endregion
}
