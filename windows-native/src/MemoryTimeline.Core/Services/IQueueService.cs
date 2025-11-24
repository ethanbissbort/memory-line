using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for recording queue management and processing.
/// </summary>
public interface IQueueService
{
    /// <summary>
    /// Adds a new recording to the queue.
    /// </summary>
    Task<RecordingQueue> AddToQueueAsync(AudioRecordingDto recording);

    /// <summary>
    /// Gets all recordings in the queue.
    /// </summary>
    Task<IEnumerable<AudioRecordingDto>> GetAllQueueItemsAsync();

    /// <summary>
    /// Gets a specific queue item by ID.
    /// </summary>
    Task<RecordingQueue?> GetQueueItemAsync(string queueId);

    /// <summary>
    /// Updates queue item status.
    /// </summary>
    Task UpdateQueueItemStatusAsync(string queueId, string status, string? errorMessage = null);

    /// <summary>
    /// Removes a queue item.
    /// </summary>
    Task RemoveQueueItemAsync(string queueId);

    /// <summary>
    /// Gets the count of items by status.
    /// </summary>
    Task<int> GetQueueCountByStatusAsync(string status);

    /// <summary>
    /// Processes the next pending item in the queue.
    /// </summary>
    Task ProcessNextItemAsync();

    /// <summary>
    /// Processes all pending items in the queue.
    /// </summary>
    Task ProcessAllPendingAsync();

    /// <summary>
    /// Retries a failed queue item.
    /// </summary>
    Task RetryFailedItemAsync(string queueId);

    /// <summary>
    /// Clears completed items from the queue.
    /// </summary>
    Task ClearCompletedItemsAsync();

    /// <summary>
    /// Event raised when a queue item's status changes.
    /// </summary>
    event EventHandler<QueueItemStatusChangedEventArgs>? QueueItemStatusChanged;

    /// <summary>
    /// Event raised when processing progress changes.
    /// </summary>
    event EventHandler<QueueProcessingProgressEventArgs>? ProcessingProgressChanged;
}

/// <summary>
/// Event args for queue item status change.
/// </summary>
public class QueueItemStatusChangedEventArgs : EventArgs
{
    public string QueueId { get; set; } = string.Empty;
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Event args for queue processing progress.
/// </summary>
public class QueueProcessingProgressEventArgs : EventArgs
{
    public string QueueId { get; set; } = string.Empty;
    public int ProgressPercentage { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}
