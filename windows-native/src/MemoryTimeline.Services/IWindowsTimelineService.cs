using MemoryTimeline.Data.Models;
using System;
using System.Threading.Tasks;

namespace MemoryTimeline.Services;

/// <summary>
/// Service interface for Windows Timeline integration using User Activity API.
/// </summary>
public interface IWindowsTimelineService
{
    /// <summary>
    /// Publishes an event to Windows Timeline.
    /// </summary>
    /// <param name="evt">Event to publish</param>
    Task PublishEventToTimelineAsync(Event evt);

    /// <summary>
    /// Updates an existing activity in Windows Timeline.
    /// </summary>
    /// <param name="evt">Event to update</param>
    Task UpdateTimelineActivityAsync(Event evt);

    /// <summary>
    /// Removes an event from Windows Timeline.
    /// </summary>
    /// <param name="eventId">Event ID to remove</param>
    Task RemoveFromTimelineAsync(string eventId);

    /// <summary>
    /// Creates a deep link URI for an event.
    /// </summary>
    /// <param name="eventId">Event ID</param>
    string CreateDeepLinkUri(string eventId);

    /// <summary>
    /// Handles a deep link activation.
    /// </summary>
    /// <param name="uri">Deep link URI</param>
    Task<string?> HandleDeepLinkAsync(string uri);
}
