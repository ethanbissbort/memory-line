using Microsoft.Extensions.Logging;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service for extracting events from transcripts using LLM.
/// </summary>
public class EventExtractionService : IEventExtractionService
{
    private readonly ILlmService _llmService;
    private readonly ISpeechToTextService _sttService;
    private readonly IEventService _eventService;
    private readonly IRecordingQueueRepository _queueRepository;
    private readonly Data.AppDbContext _dbContext;
    private readonly ILogger<EventExtractionService> _logger;

    public EventExtractionService(
        ILlmService llmService,
        ISpeechToTextService sttService,
        IEventService eventService,
        IRecordingQueueRepository queueRepository,
        Data.AppDbContext dbContext,
        ILogger<EventExtractionService> logger)
    {
        _llmService = llmService;
        _sttService = sttService;
        _eventService = eventService;
        _queueRepository = queueRepository;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Processes a recording: transcribe audio and extract events.
    /// </summary>
    public async Task<int> ProcessRecordingAsync(string queueId, IProgress<(int, string)>? progress = null)
    {
        try
        {
            _logger.LogInformation("Processing recording {QueueId}", queueId);
            progress?.Report((10, "Loading recording..."));

            var queueItem = await _queueRepository.GetByIdAsync(queueId);
            if (queueItem == null)
            {
                throw new Exception($"Queue item {queueId} not found");
            }

            // Step 1: Transcribe audio
            progress?.Report((20, "Transcribing audio..."));
            _logger.LogInformation("Transcribing audio file: {FilePath}", queueItem.AudioFilePath);

            var transcriptionResult = await _sttService.TranscribeAsync(queueItem.AudioFilePath);

            if (!transcriptionResult.Success || string.IsNullOrWhiteSpace(transcriptionResult.Text))
            {
                throw new Exception($"Transcription failed: {transcriptionResult.ErrorMessage}");
            }

            _logger.LogInformation("Transcription completed: {Length} characters", transcriptionResult.Text.Length);

            // Step 2: Extract events using LLM
            progress?.Report((50, "Extracting events..."));
            var pendingEvents = await ExtractAndCreatePendingEventsAsync(queueId, transcriptionResult.Text);

            progress?.Report((100, $"Extracted {pendingEvents.Count} events"));
            _logger.LogInformation("Successfully extracted {Count} events from recording {QueueId}",
                pendingEvents.Count, queueId);

            return pendingEvents.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing recording {QueueId}", queueId);
            throw;
        }
    }

    /// <summary>
    /// Extracts events from transcript and creates pending events.
    /// </summary>
    public async Task<List<PendingEvent>> ExtractAndCreatePendingEventsAsync(string queueId, string transcript)
    {
        try
        {
            _logger.LogInformation("Extracting events from transcript for queue {QueueId}", queueId);

            // Build extraction context
            var context = await BuildExtractionContextAsync();

            // Extract events using LLM
            var extraction = await _llmService.ExtractEventsAsync(transcript, context);

            if (!extraction.Success)
            {
                throw new Exception($"Event extraction failed: {extraction.ErrorMessage}");
            }

            _logger.LogInformation("LLM extracted {Count} events with confidence {Confidence}",
                extraction.Events.Count, extraction.OverallConfidence);

            // Convert to pending events
            var pendingEvents = new List<PendingEvent>();

            foreach (var extracted in extraction.Events)
            {
                var pendingEvent = new PendingEvent
                {
                    PendingEventId = Guid.NewGuid().ToString(),
                    QueueId = queueId,
                    Title = extracted.Title,
                    Description = extracted.Description,
                    StartDate = extracted.StartDate,
                    EndDate = extracted.EndDate,
                    Category = ParseCategory(extracted.Category),
                    ConfidenceScore = extracted.Confidence,
                    ExtractedData = System.Text.Json.JsonSerializer.Serialize(extracted),
                    IsApproved = false,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.PendingEvents.Add(pendingEvent);
                pendingEvents.Add(pendingEvent);

                _logger.LogDebug("Created pending event: {Title} (confidence: {Confidence})",
                    pendingEvent.Title, pendingEvent.ConfidenceScore);
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Created {Count} pending events for review", pendingEvents.Count);
            return pendingEvents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting and creating pending events");
            throw;
        }
    }

    /// <summary>
    /// Approves a pending event and creates it as a real event.
    /// </summary>
    public async Task<Event> ApprovePendingEventAsync(string pendingEventId)
    {
        try
        {
            var pendingEvent = await _dbContext.PendingEvents
                .FirstOrDefaultAsync(pe => pe.PendingEventId == pendingEventId);

            if (pendingEvent == null)
            {
                throw new Exception($"Pending event {pendingEventId} not found");
            }

            _logger.LogInformation("Approving pending event: {Title}", pendingEvent.Title);

            // Create real event
            var realEvent = new Event
            {
                EventId = Guid.NewGuid().ToString(),
                Title = pendingEvent.Title,
                Description = pendingEvent.Description,
                StartDate = pendingEvent.StartDate,
                EndDate = pendingEvent.EndDate,
                Category = pendingEvent.Category,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var createdEvent = await _eventService.CreateEventAsync(realEvent);

            // Mark pending event as approved
            pendingEvent.IsApproved = true;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Pending event approved and created: {EventId}", createdEvent.EventId);
            return createdEvent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving pending event");
            throw;
        }
    }

    /// <summary>
    /// Updates a pending event.
    /// </summary>
    public async Task<PendingEvent> UpdatePendingEventAsync(PendingEvent pendingEvent)
    {
        try
        {
            _dbContext.PendingEvents.Update(pendingEvent);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Updated pending event: {PendingEventId}", pendingEvent.PendingEventId);
            return pendingEvent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating pending event");
            throw;
        }
    }

    /// <summary>
    /// Rejects and deletes a pending event.
    /// </summary>
    public async Task RejectPendingEventAsync(string pendingEventId)
    {
        try
        {
            var pendingEvent = await _dbContext.PendingEvents
                .FirstOrDefaultAsync(pe => pe.PendingEventId == pendingEventId);

            if (pendingEvent != null)
            {
                _dbContext.PendingEvents.Remove(pendingEvent);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Rejected and deleted pending event: {PendingEventId}", pendingEventId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting pending event");
            throw;
        }
    }

    /// <summary>
    /// Gets all pending events for a queue item.
    /// </summary>
    public async Task<List<PendingEvent>> GetPendingEventsForQueueAsync(string queueId)
    {
        return await _dbContext.PendingEvents
            .Where(pe => pe.QueueId == queueId)
            .OrderByDescending(pe => pe.ConfidenceScore)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all pending events awaiting review.
    /// </summary>
    public async Task<List<PendingEvent>> GetAllPendingEventsAsync()
    {
        return await _dbContext.PendingEvents
            .Where(pe => !pe.IsApproved)
            .OrderByDescending(pe => pe.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets count of pending events by approval status.
    /// </summary>
    public async Task<int> GetPendingEventCountAsync(bool? isApproved = null)
    {
        var query = _dbContext.PendingEvents.AsQueryable();

        if (isApproved.HasValue)
        {
            query = query.Where(pe => pe.IsApproved == isApproved.Value);
        }

        return await query.CountAsync();
    }

    #region Private Methods

    /// <summary>
    /// Builds extraction context from existing data.
    /// </summary>
    private async Task<ExtractionContext> BuildExtractionContextAsync()
    {
        var context = new ExtractionContext
        {
            ReferenceDate = DateTime.Now
        };

        try
        {
            // Get recent event titles for context
            var recentEvents = await _eventService.GetRecentEventsAsync(20);
            context.RecentEvents = recentEvents.Select(e => e.Title).ToList();

            // TODO: Get available tags, people, locations from database
            // This would require additional repository methods

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error building extraction context, using minimal context");
            return context;
        }
    }

    /// <summary>
    /// Parses category string to EventCategory constant.
    /// </summary>
    private string ParseCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return EventCategory.Other;
        }

        return category.ToLowerInvariant() switch
        {
            "milestone" => EventCategory.Milestone,
            "work" => EventCategory.Work,
            "education" => EventCategory.Education,
            "health" => EventCategory.Challenge,
            "travel" => EventCategory.Travel,
            "social" => EventCategory.Relationship,
            "personal" => EventCategory.Other,
            "family" => EventCategory.Relationship,
            _ => EventCategory.Other
        };
    }

    #endregion
}
