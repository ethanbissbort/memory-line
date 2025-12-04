using MemoryTimeline.Data.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.UserActivities;

namespace MemoryTimeline.Services;

/// <summary>
/// Windows Timeline integration service using User Activity API.
/// </summary>
public class WindowsTimelineService : IWindowsTimelineService
{
    private readonly ILogger<WindowsTimelineService> _logger;
    private readonly UserActivityChannel _channel;

    public WindowsTimelineService(ILogger<WindowsTimelineService> logger)
    {
        _logger = logger;
        _channel = UserActivityChannel.GetDefault();
    }

    /// <summary>
    /// Publishes an event to Windows Timeline.
    /// </summary>
    public async Task PublishEventToTimelineAsync(Event evt)
    {
        try
        {
            _logger.LogInformation("Publishing event to Windows Timeline: {EventId}", evt.EventId);

            var activity = await _channel.GetOrCreateUserActivityAsync(evt.EventId);

            // Set display text
            activity.VisualElements.DisplayText = evt.Title;
            activity.VisualElements.Description = evt.Description ?? string.Empty;

            // Set activation URI for deep linking
            activity.ActivationUri = new Uri(CreateDeepLinkUri(evt.EventId));

            // Set content info for rich cards
            activity.VisualElements.Content = Windows.UI.Shell.AdaptiveCardBuilder.CreateAdaptiveCardFromJson(
                CreateAdaptiveCard(evt));

            // Save the activity
            await activity.SaveAsync();

            // Create a session (makes it show in Timeline)
            var session = activity.CreateSession();
            session.Dispose();

            _logger.LogInformation("Successfully published event to Windows Timeline");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing event to Windows Timeline");
            // Don't throw - Timeline integration is optional
        }
    }

    /// <summary>
    /// Updates an existing activity in Windows Timeline.
    /// </summary>
    public async Task UpdateTimelineActivityAsync(Event evt)
    {
        // Update is the same as publish - GetOrCreateUserActivityAsync handles both
        await PublishEventToTimelineAsync(evt);
    }

    /// <summary>
    /// Removes an event from Windows Timeline.
    /// </summary>
    public async Task RemoveFromTimelineAsync(string eventId)
    {
        try
        {
            _logger.LogInformation("Removing event from Windows Timeline: {EventId}", eventId);

            await _channel.DeleteActivityAsync(eventId);

            _logger.LogInformation("Successfully removed event from Windows Timeline");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing event from Windows Timeline");
        }
    }

    /// <summary>
    /// Creates a deep link URI for an event.
    /// </summary>
    public string CreateDeepLinkUri(string eventId)
    {
        return $"memory-timeline://event/{eventId}";
    }

    /// <summary>
    /// Handles a deep link activation.
    /// </summary>
    public async Task<string?> HandleDeepLinkAsync(string uri)
    {
        try
        {
            _logger.LogInformation("Handling deep link: {Uri}", uri);

            var parsedUri = new Uri(uri);

            if (parsedUri.Scheme == "memory-timeline" && parsedUri.Host == "event")
            {
                var eventId = parsedUri.AbsolutePath.TrimStart('/');
                _logger.LogInformation("Deep link to event: {EventId}", eventId);
                return eventId;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling deep link");
            return null;
        }

        await Task.CompletedTask;
    }

    #region Private Methods

    private string CreateAdaptiveCard(Event evt)
    {
        // Create a simple Adaptive Card for Windows Timeline
        var card = $@"{{
            ""$schema"": ""http://adaptivecards.io/schemas/adaptive-card.json"",
            ""type"": ""AdaptiveCard"",
            ""version"": ""1.0"",
            ""body"": [
                {{
                    ""type"": ""TextBlock"",
                    ""text"": ""{EscapeJson(evt.Title)}"",
                    ""size"": ""large"",
                    ""weight"": ""bolder""
                }},
                {{
                    ""type"": ""TextBlock"",
                    ""text"": ""{evt.StartDate:yyyy-MM-dd}"",
                    ""size"": ""small"",
                    ""color"": ""accent""
                }},
                {{
                    ""type"": ""TextBlock"",
                    ""text"": ""{EscapeJson(evt.Description ?? "")}"",
                    ""wrap"": true
                }},
                {{
                    ""type"": ""FactSet"",
                    ""facts"": [
                        {{
                            ""title"": ""Category"",
                            ""value"": ""{evt.Category}""
                        }}";

        if (!string.IsNullOrWhiteSpace(evt.Location))
        {
            card += $@",
                        {{
                            ""title"": ""Location"",
                            ""value"": ""{EscapeJson(evt.Location)}""
                        }}";
        }

        card += @"
                    ]
                }
            ]
        }";

        return card;
    }

    private string EscapeJson(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    #endregion
}
