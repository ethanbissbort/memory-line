using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Queue service implementation for managing recording queue and processing.
/// </summary>
public class QueueService : IQueueService
{
    private readonly IRecordingQueueRepository _queueRepository;
    private readonly ISpeechToTextService _sttService;
    private readonly ILogger<QueueService> _logger;
    private readonly SemaphoreSlim _processingSemaphore = new(1, 1);

    public event EventHandler<QueueItemStatusChangedEventArgs>? QueueItemStatusChanged;
    public event EventHandler<QueueProcessingProgressEventArgs>? ProcessingProgressChanged;

    public QueueService(
        IRecordingQueueRepository queueRepository,
        ISpeechToTextService sttService,
        ILogger<QueueService> logger)
    {
        _queueRepository = queueRepository;
        _sttService = sttService;
        _logger = logger;
    }

    /// <summary>
    /// Adds a new recording to the queue.
    /// </summary>
    public async Task<RecordingQueue> AddToQueueAsync(AudioRecordingDto recording)
    {
        try
        {
            var queueItem = new RecordingQueue
            {
                QueueId = recording.QueueId,
                AudioFilePath = recording.AudioFilePath,
                Status = QueueStatus.Pending,
                DurationSeconds = recording.DurationSeconds,
                FileSizeBytes = recording.FileSizeBytes,
                CreatedAt = recording.CreatedAt
            };

            var added = await _queueRepository.AddAsync(queueItem);

            _logger.LogInformation("Added recording to queue: {QueueId}", added.QueueId);
            RaiseStatusChanged(added.QueueId, "", QueueStatus.Pending);

            return added;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding recording to queue");
            throw;
        }
    }

    /// <summary>
    /// Gets all recordings in the queue.
    /// </summary>
    public async Task<IEnumerable<AudioRecordingDto>> GetAllQueueItemsAsync()
    {
        try
        {
            var items = await _queueRepository.GetAllAsync();
            return items.Select(AudioRecordingDto.FromRecordingQueue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting queue items");
            throw;
        }
    }

    /// <summary>
    /// Gets a specific queue item by ID.
    /// </summary>
    public async Task<RecordingQueue?> GetQueueItemAsync(string queueId)
    {
        return await _queueRepository.GetByIdAsync(queueId);
    }

    /// <summary>
    /// Updates queue item status.
    /// </summary>
    public async Task UpdateQueueItemStatusAsync(string queueId, string status, string? errorMessage = null)
    {
        try
        {
            var item = await _queueRepository.GetByIdAsync(queueId);
            if (item == null)
            {
                _logger.LogWarning("Queue item not found: {QueueId}", queueId);
                return;
            }

            var oldStatus = item.Status;
            item.Status = status;
            item.ErrorMessage = errorMessage;

            if (status == QueueStatus.Completed || status == QueueStatus.Failed)
            {
                item.ProcessedAt = DateTime.UtcNow;
            }

            await _queueRepository.UpdateAsync(item);

            _logger.LogInformation("Updated queue item {QueueId} status: {OldStatus} -> {NewStatus}",
                queueId, oldStatus, status);

            RaiseStatusChanged(queueId, oldStatus, status, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating queue item status");
            throw;
        }
    }

    /// <summary>
    /// Removes a queue item.
    /// </summary>
    public async Task RemoveQueueItemAsync(string queueId)
    {
        try
        {
            await _queueRepository.DeleteAsync(queueId);
            _logger.LogInformation("Removed queue item: {QueueId}", queueId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing queue item");
            throw;
        }
    }

    /// <summary>
    /// Gets the count of items by status.
    /// </summary>
    public async Task<int> GetQueueCountByStatusAsync(string status)
    {
        return await _queueRepository.GetCountByStatusAsync(status);
    }

    /// <summary>
    /// Processes the next pending item in the queue.
    /// </summary>
    public async Task ProcessNextItemAsync()
    {
        // Ensure only one item is processed at a time
        if (!await _processingSemaphore.WaitAsync(0))
        {
            _logger.LogDebug("Queue processing already in progress");
            return;
        }

        try
        {
            var pendingItems = await _queueRepository.GetByStatusAsync(QueueStatus.Pending);
            var nextItem = pendingItems.FirstOrDefault();

            if (nextItem == null)
            {
                _logger.LogDebug("No pending items in queue");
                return;
            }

            await ProcessQueueItemAsync(nextItem);
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }

    /// <summary>
    /// Processes all pending items in the queue.
    /// </summary>
    public async Task ProcessAllPendingAsync()
    {
        if (!await _processingSemaphore.WaitAsync(0))
        {
            _logger.LogWarning("Queue processing already in progress");
            return;
        }

        try
        {
            var pendingItems = await _queueRepository.GetByStatusAsync(QueueStatus.Pending);
            _logger.LogInformation("Processing {Count} pending items", pendingItems.Count());

            foreach (var item in pendingItems)
            {
                await ProcessQueueItemAsync(item);
            }

            _logger.LogInformation("Finished processing all pending items");
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }

    /// <summary>
    /// Retries a failed queue item.
    /// </summary>
    public async Task RetryFailedItemAsync(string queueId)
    {
        try
        {
            var item = await _queueRepository.GetByIdAsync(queueId);
            if (item == null || item.Status != QueueStatus.Failed)
            {
                _logger.LogWarning("Cannot retry item {QueueId}: not found or not failed", queueId);
                return;
            }

            item.Status = QueueStatus.Pending;
            item.ErrorMessage = null;
            item.ProcessedAt = null;

            await _queueRepository.UpdateAsync(item);
            _logger.LogInformation("Retrying failed item: {QueueId}", queueId);

            RaiseStatusChanged(queueId, QueueStatus.Failed, QueueStatus.Pending);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrying failed item");
            throw;
        }
    }

    /// <summary>
    /// Clears completed items from the queue.
    /// </summary>
    public async Task ClearCompletedItemsAsync()
    {
        try
        {
            var completedItems = await _queueRepository.GetByStatusAsync(QueueStatus.Completed);
            var count = 0;

            foreach (var item in completedItems)
            {
                await _queueRepository.DeleteAsync(item.QueueId);
                count++;
            }

            _logger.LogInformation("Cleared {Count} completed items from queue", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing completed items");
            throw;
        }
    }

    #region Private Methods

    /// <summary>
    /// Processes a single queue item with retry logic.
    /// </summary>
    private async Task ProcessQueueItemAsync(RecordingQueue item)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 1000;

        _logger.LogInformation("Processing queue item: {QueueId}", item.QueueId);

        await UpdateQueueItemStatusAsync(item.QueueId, QueueStatus.Processing);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                RaiseProgressChanged(item.QueueId, 10, "Transcribing audio...");

                // Transcribe audio using STT service
                var transcription = await _sttService.TranscribeAsync(item.AudioFilePath);

                RaiseProgressChanged(item.QueueId, 50, "Processing transcription...");

                // TODO: In Phase 4, add LLM event extraction here

                RaiseProgressChanged(item.QueueId, 100, "Completed");

                await UpdateQueueItemStatusAsync(item.QueueId, QueueStatus.Completed);

                _logger.LogInformation("Successfully processed queue item: {QueueId}", item.QueueId);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue item {QueueId}, attempt {Attempt}/{MaxRetries}",
                    item.QueueId, attempt, maxRetries);

                if (attempt < maxRetries)
                {
                    // Exponential backoff
                    var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1);
                    _logger.LogInformation("Retrying in {Delay}ms...", delay);
                    await Task.Delay(delay);
                }
                else
                {
                    // Max retries exceeded, mark as failed
                    await UpdateQueueItemStatusAsync(item.QueueId, QueueStatus.Failed, ex.Message);
                    _logger.LogError("Failed to process queue item {QueueId} after {MaxRetries} attempts",
                        item.QueueId, maxRetries);
                }
            }
        }
    }

    private void RaiseStatusChanged(string queueId, string oldStatus, string newStatus, string? errorMessage = null)
    {
        QueueItemStatusChanged?.Invoke(this, new QueueItemStatusChangedEventArgs
        {
            QueueId = queueId,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            ErrorMessage = errorMessage
        });
    }

    private void RaiseProgressChanged(string queueId, int percentage, string message)
    {
        ProcessingProgressChanged?.Invoke(this, new QueueProcessingProgressEventArgs
        {
            QueueId = queueId,
            ProgressPercentage = percentage,
            StatusMessage = message
        });
    }

    #endregion
}
