using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service for extracting events from transcripts using LLM.
/// Creates a short-lived DbContext per operation via IDbContextFactory,
/// so it is safe to consume from singletons and background processing.
/// </summary>
public class EventExtractionService : IEventExtractionService
{
    private readonly ILlmService _llmService;
    private readonly ISpeechToTextService _sttService;
    private readonly IEventService _eventService;
    private readonly ISettingsService _settingsService;
    private readonly IRecordingQueueRepository _queueRepository;
    private readonly IDbContextFactory<Data.AppDbContext> _contextFactory;
    private readonly ILogger<EventExtractionService> _logger;

    private const string MissingApiKeyMessage = "Anthropic API key not configured — add it in Settings";

    public EventExtractionService(
        ILlmService llmService,
        ISpeechToTextService sttService,
        IEventService eventService,
        ISettingsService settingsService,
        IRecordingQueueRepository queueRepository,
        IDbContextFactory<Data.AppDbContext> contextFactory,
        ILogger<EventExtractionService> logger)
    {
        _llmService = llmService;
        _sttService = sttService;
        _eventService = eventService;
        _settingsService = settingsService;
        _queueRepository = queueRepository;
        _contextFactory = contextFactory;
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

            // API-key pre-flight BEFORE any work (transcription is wasted effort
            // if extraction can never run). ConfigurationException is non-retryable.
            await EnsureLlmConfiguredAsync();

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

            // Step 2: Extract events using LLM (transcript + audio path are persisted
            // on every pending event so nothing lives only in memory)
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
    /// The transcript and source audio path are persisted on each pending event.
    /// </summary>
    public async Task<List<PendingEvent>> ExtractAndCreatePendingEventsAsync(string queueId, string transcript)
    {
        try
        {
            _logger.LogInformation("Extracting events from transcript for queue {QueueId}", queueId);

            // Resolve the source audio file path so it can be persisted with each pending event
            var queueItem = await _queueRepository.GetByIdAsync(queueId);
            var audioFilePath = queueItem?.AudioFilePath;

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

            await using var dbContext = await _contextFactory.CreateDbContextAsync();

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
                    Transcript = transcript,
                    AudioFilePath = audioFilePath,
                    Status = PendingStatus.PendingReview.ToStringValue(),
                    IsApproved = false,
                    CreatedAt = DateTime.UtcNow
                };

                dbContext.PendingEvents.Add(pendingEvent);
                pendingEvents.Add(pendingEvent);

                _logger.LogDebug("Created pending event: {Title} (confidence: {Confidence})",
                    pendingEvent.Title, pendingEvent.ConfidenceScore);
            }

            await dbContext.SaveChangesAsync();

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
    /// Atomic: the event row, its tag/person/location metadata and the pending-event
    /// status flip are all written in a single transaction on one context, so a
    /// failure part-way can never leave a half-approved state or duplicate events
    /// on re-approve.
    /// </summary>
    public async Task<Event> ApprovePendingEventAsync(string pendingEventId)
    {
        try
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync();

            var pendingEvent = await dbContext.PendingEvents
                .FirstOrDefaultAsync(pe => pe.PendingId == pendingEventId);

            if (pendingEvent == null)
            {
                throw new Exception($"Pending event {pendingEventId} not found");
            }

            if (pendingEvent.IsApproved)
            {
                throw new InvalidOperationException(
                    $"Pending event {pendingEventId} has already been approved");
            }

            // Minimal validation before writing anything
            if (string.IsNullOrWhiteSpace(pendingEvent.Title))
            {
                throw new InvalidOperationException("Cannot approve a pending event without a title");
            }

            if (pendingEvent.StartDate == default)
            {
                throw new InvalidOperationException("Cannot approve a pending event without a start date");
            }

            _logger.LogInformation("Approving pending event: {Title}", pendingEvent.Title);

            // Recover the full extraction payload (tags/people/locations/sourceText)
            var extracted = TryDeserializeExtractedData(pendingEvent.ExtractedData);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            var realEvent = new Event
            {
                EventId = Guid.NewGuid().ToString(),
                Title = pendingEvent.Title,
                Description = pendingEvent.Description,
                StartDate = pendingEvent.StartDate,
                EndDate = pendingEvent.EndDate,
                Category = NormalizeCategory(pendingEvent.Category),
                Confidence = pendingEvent.ConfidenceScore,
                AudioFilePath = pendingEvent.AudioFilePath,
                RawTranscript = pendingEvent.Transcript,
                // Provenance: the full extraction JSON (source text, reasoning, ...)
                ExtractionMetadata = string.IsNullOrWhiteSpace(pendingEvent.ExtractedData)
                    ? null
                    : pendingEvent.ExtractedData,
                Location = extracted?.Locations?.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            dbContext.Events.Add(realEvent);

            if (extracted != null)
            {
                await MapExtractedMetadataAsync(dbContext, realEvent, extracted);
            }

            // Flip pending-event status inside the same transaction
            pendingEvent.IsApproved = true;
            pendingEvent.Status = PendingStatus.Approved.ToStringValue();
            pendingEvent.ReviewedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Pending event approved and created: {EventId}", realEvent.EventId);

            // Notify the UI (singleton TimelineViewModel refreshes without renavigation)
            try
            {
                WeakReferenceMessenger.Default.Send(
                    new EventCreatedMessage(realEvent.EventId, realEvent.StartDate));
            }
            catch (Exception msgEx)
            {
                _logger.LogWarning(msgEx, "Failed to publish EventCreatedMessage for {EventId}", realEvent.EventId);
            }

            // Kick off embedding generation in the background; it must never
            // affect the approve flow and logs its own errors.
            var approvedEventId = realEvent.EventId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _eventService.GenerateEmbeddingAsync(approvedEventId);
                }
                catch (Exception embedEx)
                {
                    _logger.LogError(embedEx,
                        "Background embedding generation failed for approved event {EventId}", approvedEventId);
                }
            });

            return realEvent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving pending event");
            throw;
        }
    }

    /// <summary>
    /// Updates a pending event. Loads the tracked row by id in a fresh context and
    /// copies the editable fields (no Update() on a detached clone).
    /// </summary>
    public async Task<PendingEvent> UpdatePendingEventAsync(PendingEvent pendingEvent)
    {
        try
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync();

            var tracked = await dbContext.PendingEvents
                .FirstOrDefaultAsync(pe => pe.PendingId == pendingEvent.PendingId);

            if (tracked == null)
            {
                throw new Exception($"Pending event {pendingEvent.PendingEventId} not found");
            }

            tracked.Title = pendingEvent.Title;
            tracked.Description = pendingEvent.Description;
            tracked.StartDate = pendingEvent.StartDate;
            tracked.EndDate = pendingEvent.EndDate;
            tracked.Category = pendingEvent.Category;
            tracked.ConfidenceScore = pendingEvent.ConfidenceScore;

            await dbContext.SaveChangesAsync();

            _logger.LogInformation("Updated pending event: {PendingEventId}", tracked.PendingEventId);
            return tracked;
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
            await using var dbContext = await _contextFactory.CreateDbContextAsync();

            var pendingEvent = await dbContext.PendingEvents
                .FirstOrDefaultAsync(pe => pe.PendingId == pendingEventId);

            if (pendingEvent != null)
            {
                dbContext.PendingEvents.Remove(pendingEvent);
                await dbContext.SaveChangesAsync();

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
        await using var dbContext = await _contextFactory.CreateDbContextAsync();

        return await dbContext.PendingEvents
            .AsNoTracking()
            .Where(pe => pe.QueueId == queueId)
            .OrderByDescending(pe => pe.ConfidenceScore)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all pending events awaiting review.
    /// </summary>
    public async Task<List<PendingEvent>> GetAllPendingEventsAsync()
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync();

        return await dbContext.PendingEvents
            .AsNoTracking()
            .Where(pe => !pe.IsApproved)
            .OrderByDescending(pe => pe.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets count of pending events by approval status.
    /// </summary>
    public async Task<int> GetPendingEventCountAsync(bool? isApproved = null)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync();

        var query = dbContext.PendingEvents.AsQueryable();

        if (isApproved.HasValue)
        {
            query = query.Where(pe => pe.IsApproved == isApproved.Value);
        }

        return await query.CountAsync();
    }

    #region Private Methods

    /// <summary>
    /// Verifies the LLM API key is configured; throws a non-retryable
    /// ConfigurationException when it is missing so the queue fails the
    /// item immediately instead of burning retries.
    /// </summary>
    private async Task EnsureLlmConfiguredAsync()
    {
        var apiKey = await _settingsService.GetSettingAsync<string>(SettingKeys.AnthropicApiKey, string.Empty);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ConfigurationException(MissingApiKeyMessage);
        }
    }

    /// <summary>
    /// Deserializes the stored extraction payload; returns null (with a logged
    /// warning) on malformed data instead of failing the approval.
    /// </summary>
    private ExtractedEvent? TryDeserializeExtractedData(string? extractedData)
    {
        if (string.IsNullOrWhiteSpace(extractedData))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ExtractedEvent>(
                extractedData,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed ExtractedData on pending event; approving without metadata");
            return null;
        }
    }

    /// <summary>
    /// Maps extracted tags/people/locations onto the real entity + junction tables.
    /// Upserts Tag/Person/Location by name (checking both the database and rows
    /// already added to this context), then adds junction rows. All work happens
    /// on the caller's context so it participates in the approve transaction.
    /// </summary>
    private async Task MapExtractedMetadataAsync(Data.AppDbContext dbContext, Event realEvent, ExtractedEvent extracted)
    {
        // Tags -> tags + event_tags
        foreach (var rawTag in DistinctNames(extracted.Tags))
        {
            var tag = dbContext.Tags.Local
                    .FirstOrDefault(t => string.Equals(t.TagName, rawTag, StringComparison.OrdinalIgnoreCase))
                ?? await dbContext.Tags
                    .FirstOrDefaultAsync(t => t.TagName.ToLower() == rawTag.ToLower());

            if (tag == null)
            {
                tag = new Tag
                {
                    TagId = Guid.NewGuid().ToString(),
                    TagName = rawTag,
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.Tags.Add(tag);
            }

            dbContext.EventTags.Add(new EventTag
            {
                EventId = realEvent.EventId,
                TagId = tag.TagId,
                Event = realEvent,
                Tag = tag,
                ConfidenceScore = extracted.Confidence,
                IsManual = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        // People -> people + event_people
        foreach (var rawPerson in DistinctNames(extracted.People))
        {
            var person = dbContext.People.Local
                    .FirstOrDefault(p => string.Equals(p.Name, rawPerson, StringComparison.OrdinalIgnoreCase))
                ?? await dbContext.People
                    .FirstOrDefaultAsync(p => p.Name.ToLower() == rawPerson.ToLower());

            if (person == null)
            {
                person = new Person
                {
                    PersonId = Guid.NewGuid().ToString(),
                    Name = rawPerson,
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.People.Add(person);
            }

            dbContext.EventPeople.Add(new EventPerson
            {
                EventId = realEvent.EventId,
                PersonId = person.PersonId,
                Event = realEvent,
                Person = person,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Locations -> locations + event_locations
        foreach (var rawLocation in DistinctNames(extracted.Locations))
        {
            var location = dbContext.Locations.Local
                    .FirstOrDefault(l => string.Equals(l.Name, rawLocation, StringComparison.OrdinalIgnoreCase))
                ?? await dbContext.Locations
                    .FirstOrDefaultAsync(l => l.Name.ToLower() == rawLocation.ToLower());

            if (location == null)
            {
                location = new Location
                {
                    LocationId = Guid.NewGuid().ToString(),
                    Name = rawLocation,
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.Locations.Add(location);
            }

            dbContext.EventLocations.Add(new EventLocation
            {
                EventId = realEvent.EventId,
                LocationId = location.LocationId,
                Event = realEvent,
                Location = location,
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    private static IEnumerable<string> DistinctNames(IEnumerable<string>? names)
    {
        return (names ?? Enumerable.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

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
    /// Normalizes an already-stored category to a valid lowercase EventCategory value.
    /// </summary>
    private string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return EventCategory.Other;
        }

        var normalized = category.Trim().ToLowerInvariant();

        return EventCategory.AllCategories.Contains(normalized)
            ? normalized
            : ParseCategory(category);
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
