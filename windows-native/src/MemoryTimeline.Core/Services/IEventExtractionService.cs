using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for extracting events from transcripts.
/// </summary>
public interface IEventExtractionService
{
    /// <summary>
    /// Processes a recording queue item: transcribe and extract events.
    /// </summary>
    /// <param name="queueId">Queue item ID</param>
    /// <param name="progress">Progress reporter</param>
    /// <returns>Number of events extracted</returns>
    Task<int> ProcessRecordingAsync(string queueId, IProgress<(int percentage, string message)>? progress = null);

    /// <summary>
    /// Extracts events from a transcript and creates pending events.
    /// </summary>
    /// <param name="queueId">Queue item ID</param>
    /// <param name="transcript">Transcript text</param>
    /// <returns>List of created pending events</returns>
    Task<List<PendingEvent>> ExtractAndCreatePendingEventsAsync(string queueId, string transcript);

    /// <summary>
    /// Approves a pending event and creates it as a real event.
    /// </summary>
    /// <param name="pendingEventId">Pending event ID</param>
    /// <returns>Created event</returns>
    Task<Event> ApprovePendingEventAsync(string pendingEventId);

    /// <summary>
    /// Updates a pending event before approval.
    /// </summary>
    /// <param name="pendingEvent">Updated pending event</param>
    /// <returns>Updated pending event</returns>
    Task<PendingEvent> UpdatePendingEventAsync(PendingEvent pendingEvent);

    /// <summary>
    /// Rejects and deletes a pending event.
    /// </summary>
    /// <param name="pendingEventId">Pending event ID</param>
    Task RejectPendingEventAsync(string pendingEventId);

    /// <summary>
    /// Gets all pending events for a queue item.
    /// </summary>
    /// <param name="queueId">Queue item ID</param>
    /// <returns>List of pending events</returns>
    Task<List<PendingEvent>> GetPendingEventsForQueueAsync(string queueId);

    /// <summary>
    /// Gets all pending events awaiting review.
    /// </summary>
    /// <returns>List of pending events</returns>
    Task<List<PendingEvent>> GetAllPendingEventsAsync();

    /// <summary>
    /// Gets count of pending events by status.
    /// </summary>
    /// <param name="isApproved">Approval status</param>
    /// <returns>Count</returns>
    Task<int> GetPendingEventCountAsync(bool? isApproved = null);
}
