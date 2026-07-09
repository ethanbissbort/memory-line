using CommunityToolkit.Mvvm.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for event-related business logic.
/// </summary>
public interface IEventService
{
    // CRUD operations
    Task<Event> CreateEventAsync(Event eventData);
    Task<Event?> GetEventByIdAsync(string eventId);
    Task<Event?> GetEventWithDetailsAsync(string eventId);
    Task<IEnumerable<Event>> GetAllEventsAsync();
    Task<Event> UpdateEventAsync(Event eventData);
    Task DeleteEventAsync(string eventId);

    // Query operations
    Task<IEnumerable<Event>> GetEventsByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<IEnumerable<Event>> GetEventsByCategoryAsync(string category);
    Task<IEnumerable<Event>> GetEventsByEraAsync(string eraId);
    Task<IEnumerable<Event>> SearchEventsAsync(string searchTerm);
    Task<IEnumerable<Event>> GetRecentEventsAsync(int count);

    // Pagination
    Task<(IEnumerable<Event> Events, int TotalCount)> GetPagedEventsAsync(
        int pageNumber,
        int pageSize,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? category = null);

    // Tags
    Task AddTagToEventAsync(string eventId, string tagId, bool isManual = true, double confidence = 1.0);
    Task RemoveTagFromEventAsync(string eventId, string tagId);
    Task<IEnumerable<Tag>> GetEventTagsAsync(string eventId);

    // People
    Task AddPersonToEventAsync(string eventId, string personId);
    Task RemovePersonFromEventAsync(string eventId, string personId);
    Task<IEnumerable<Person>> GetEventPeopleAsync(string eventId);

    // Locations
    Task AddLocationToEventAsync(string eventId, string locationId);
    Task RemoveLocationFromEventAsync(string eventId, string locationId);
    Task<IEnumerable<Location>> GetEventLocationsAsync(string eventId);

    // Statistics
    Task<int> GetTotalEventCountAsync();
    Task<Dictionary<string, int>> GetEventCountByCategoryAsync();

    // Embeddings
    Task<bool> HasEmbeddingAsync(string eventId);
    Task GenerateEmbeddingAsync(string eventId);
}

/// <summary>
/// Event service implementation with business logic.
/// </summary>
public class EventService : IEventService
{
    private readonly IEventRepository _eventRepository;
    private readonly IEmbeddingService? _embeddingService;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<EventService> _logger;

    public EventService(
        IEventRepository eventRepository,
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<EventService> logger,
        IEmbeddingService? embeddingService = null)
    {
        _eventRepository = eventRepository;
        _contextFactory = contextFactory;
        _logger = logger;
        _embeddingService = embeddingService;
    }

    // CRUD operations

    public async Task<Event> CreateEventAsync(Event eventData)
    {
        try
        {
            eventData.Category = NormalizeCategory(eventData.Category);
            ValidateEvent(eventData);

            eventData.EventId = Guid.NewGuid().ToString();
            eventData.CreatedAt = DateTime.UtcNow;
            eventData.UpdatedAt = DateTime.UtcNow;

            var createdEvent = await _eventRepository.AddAsync(eventData);
            _logger.LogInformation("Event created: {EventId} - {Title}", createdEvent.EventId, createdEvent.Title);

            // Notify subscribers (e.g. TimelineViewModel) that a new event exists.
            // A subscriber failure must not turn a successful create into an error.
            try
            {
                WeakReferenceMessenger.Default.Send(new EventCreatedMessage(createdEvent.EventId, createdEvent.StartDate));
            }
            catch (Exception messengerEx)
            {
                _logger.LogWarning(messengerEx, "Error publishing EventCreatedMessage for event {EventId}", createdEvent.EventId);
            }

            // Generate embedding asynchronously (fire and forget).
            // The background task creates its own DbContext from the factory.
            _ = Task.Run(async () => await GenerateEmbeddingForEventInBackgroundAsync(createdEvent));

            return createdEvent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event: {Title}", eventData.Title);
            throw;
        }
    }

    public async Task<Event?> GetEventByIdAsync(string eventId)
    {
        try
        {
            return await _eventRepository.GetByIdAsync(eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event by ID: {EventId}", eventId);
            throw;
        }
    }

    public async Task<Event?> GetEventWithDetailsAsync(string eventId)
    {
        try
        {
            return await _eventRepository.GetByIdWithIncludesAsync(eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event with details: {EventId}", eventId);
            throw;
        }
    }

    public async Task<IEnumerable<Event>> GetAllEventsAsync()
    {
        try
        {
            return await _eventRepository.GetAllAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all events");
            throw;
        }
    }

    public async Task<Event> UpdateEventAsync(Event eventData)
    {
        try
        {
            eventData.Category = NormalizeCategory(eventData.Category);
            ValidateEvent(eventData);

            var existingEvent = await _eventRepository.GetByIdAsync(eventData.EventId);
            if (existingEvent == null)
            {
                throw new InvalidOperationException($"Event not found: {eventData.EventId}");
            }

            eventData.UpdatedAt = DateTime.UtcNow;
            await _eventRepository.UpdateAsync(eventData);

            _logger.LogInformation("Event updated: {EventId} - {Title}", eventData.EventId, eventData.Title);
            return eventData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event: {EventId}", eventData.EventId);
            throw;
        }
    }

    public async Task DeleteEventAsync(string eventId)
    {
        try
        {
            var eventToDelete = await _eventRepository.GetByIdAsync(eventId);
            if (eventToDelete == null)
            {
                throw new InvalidOperationException($"Event not found: {eventId}");
            }

            await _eventRepository.DeleteAsync(eventToDelete);
            _logger.LogInformation("Event deleted: {EventId}", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event: {EventId}", eventId);
            throw;
        }
    }

    // Query operations

    public async Task<IEnumerable<Event>> GetEventsByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            return await _eventRepository.GetByDateRangeAsync(startDate, endDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events by date range");
            throw;
        }
    }

    public async Task<IEnumerable<Event>> GetEventsByCategoryAsync(string category)
    {
        try
        {
            return await _eventRepository.GetByCategoryAsync(category);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events by category: {Category}", category);
            throw;
        }
    }

    public async Task<IEnumerable<Event>> GetEventsByEraAsync(string eraId)
    {
        try
        {
            return await _eventRepository.GetByEraAsync(eraId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events by era: {EraId}", eraId);
            throw;
        }
    }

    public async Task<IEnumerable<Event>> SearchEventsAsync(string searchTerm)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return Enumerable.Empty<Event>();
            }

            return await _eventRepository.SearchAsync(searchTerm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching events: {SearchTerm}", searchTerm);
            throw;
        }
    }

    public async Task<IEnumerable<Event>> GetRecentEventsAsync(int count)
    {
        try
        {
            var allEvents = await _eventRepository.GetAllAsync();
            return allEvents
                .OrderByDescending(e => e.CreatedAt)
                .Take(count)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent events");
            throw;
        }
    }

    // Pagination

    public async Task<(IEnumerable<Event> Events, int TotalCount)> GetPagedEventsAsync(
        int pageNumber,
        int pageSize,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? category = null)
    {
        try
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 1000) pageSize = 1000;

            return await _eventRepository.GetPagedAsync(pageNumber, pageSize, startDate, endDate, category);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paged events");
            throw;
        }
    }

    // Tags

    public async Task AddTagToEventAsync(string eventId, string tagId, bool isManual = true, double confidence = 1.0)
    {
        try
        {
            var eventEntity = await _eventRepository.GetByIdWithIncludesAsync(eventId);
            if (eventEntity == null)
            {
                throw new InvalidOperationException($"Event not found: {eventId}");
            }

            var eventTag = new EventTag
            {
                EventId = eventId,
                TagId = tagId,
                IsManual = isManual,
                ConfidenceScore = confidence,
                CreatedAt = DateTime.UtcNow
            };

            eventEntity.EventTags.Add(eventTag);
            await _eventRepository.UpdateAsync(eventEntity);

            _logger.LogInformation("Tag {TagId} added to event {EventId}", tagId, eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding tag to event: {EventId}", eventId);
            throw;
        }
    }

    public async Task RemoveTagFromEventAsync(string eventId, string tagId)
    {
        try
        {
            var eventEntity = await _eventRepository.GetByIdWithIncludesAsync(eventId);
            if (eventEntity == null)
            {
                throw new InvalidOperationException($"Event not found: {eventId}");
            }

            var eventTag = eventEntity.EventTags.FirstOrDefault(et => et.TagId == tagId);
            if (eventTag != null)
            {
                eventEntity.EventTags.Remove(eventTag);
                await _eventRepository.UpdateAsync(eventEntity);
                _logger.LogInformation("Tag {TagId} removed from event {EventId}", tagId, eventId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing tag from event: {EventId}", eventId);
            throw;
        }
    }

    public async Task<IEnumerable<Tag>> GetEventTagsAsync(string eventId)
    {
        try
        {
            var eventEntity = await _eventRepository.GetByIdWithIncludesAsync(eventId);
            if (eventEntity == null)
            {
                return Enumerable.Empty<Tag>();
            }

            return eventEntity.EventTags.Select(et => et.Tag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tags for event: {EventId}", eventId);
            throw;
        }
    }

    // People

    public async Task AddPersonToEventAsync(string eventId, string personId)
    {
        try
        {
            var eventEntity = await _eventRepository.GetByIdWithIncludesAsync(eventId);
            if (eventEntity == null)
            {
                throw new InvalidOperationException($"Event not found: {eventId}");
            }

            var eventPerson = new EventPerson
            {
                EventId = eventId,
                PersonId = personId,
                CreatedAt = DateTime.UtcNow
            };

            eventEntity.EventPeople.Add(eventPerson);
            await _eventRepository.UpdateAsync(eventEntity);

            _logger.LogInformation("Person {PersonId} added to event {EventId}", personId, eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding person to event: {EventId}", eventId);
            throw;
        }
    }

    public async Task RemovePersonFromEventAsync(string eventId, string personId)
    {
        try
        {
            var eventEntity = await _eventRepository.GetByIdWithIncludesAsync(eventId);
            if (eventEntity == null)
            {
                throw new InvalidOperationException($"Event not found: {eventId}");
            }

            var eventPerson = eventEntity.EventPeople.FirstOrDefault(ep => ep.PersonId == personId);
            if (eventPerson != null)
            {
                eventEntity.EventPeople.Remove(eventPerson);
                await _eventRepository.UpdateAsync(eventEntity);
                _logger.LogInformation("Person {PersonId} removed from event {EventId}", personId, eventId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing person from event: {EventId}", eventId);
            throw;
        }
    }

    public async Task<IEnumerable<Person>> GetEventPeopleAsync(string eventId)
    {
        try
        {
            var eventEntity = await _eventRepository.GetByIdWithIncludesAsync(eventId);
            if (eventEntity == null)
            {
                return Enumerable.Empty<Person>();
            }

            return eventEntity.EventPeople.Select(ep => ep.Person);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting people for event: {EventId}", eventId);
            throw;
        }
    }

    // Locations

    public async Task AddLocationToEventAsync(string eventId, string locationId)
    {
        try
        {
            var eventEntity = await _eventRepository.GetByIdWithIncludesAsync(eventId);
            if (eventEntity == null)
            {
                throw new InvalidOperationException($"Event not found: {eventId}");
            }

            var eventLocation = new EventLocation
            {
                EventId = eventId,
                LocationId = locationId,
                CreatedAt = DateTime.UtcNow
            };

            eventEntity.EventLocations.Add(eventLocation);
            await _eventRepository.UpdateAsync(eventEntity);

            _logger.LogInformation("Location {LocationId} added to event {EventId}", locationId, eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding location to event: {EventId}", eventId);
            throw;
        }
    }

    public async Task RemoveLocationFromEventAsync(string eventId, string locationId)
    {
        try
        {
            var eventEntity = await _eventRepository.GetByIdWithIncludesAsync(eventId);
            if (eventEntity == null)
            {
                throw new InvalidOperationException($"Event not found: {eventId}");
            }

            var eventLocation = eventEntity.EventLocations.FirstOrDefault(el => el.LocationId == locationId);
            if (eventLocation != null)
            {
                eventEntity.EventLocations.Remove(eventLocation);
                await _eventRepository.UpdateAsync(eventEntity);
                _logger.LogInformation("Location {LocationId} removed from event {EventId}", locationId, eventId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing location from event: {EventId}", eventId);
            throw;
        }
    }

    public async Task<IEnumerable<Location>> GetEventLocationsAsync(string eventId)
    {
        try
        {
            var eventEntity = await _eventRepository.GetByIdWithIncludesAsync(eventId);
            if (eventEntity == null)
            {
                return Enumerable.Empty<Location>();
            }

            return eventEntity.EventLocations.Select(el => el.Location);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting locations for event: {EventId}", eventId);
            throw;
        }
    }

    // Statistics

    public async Task<int> GetTotalEventCountAsync()
    {
        try
        {
            return await _eventRepository.CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total event count");
            throw;
        }
    }

    public async Task<Dictionary<string, int>> GetEventCountByCategoryAsync()
    {
        try
        {
            var events = await _eventRepository.GetAllAsync();
            return events
                .Where(e => !string.IsNullOrEmpty(e.Category))
                .GroupBy(e => e.Category!)
                .ToDictionary(g => g.Key, g => g.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event count by category");
            throw;
        }
    }

    // Private helper methods

    private void ValidateEvent(Event eventData)
    {
        if (string.IsNullOrWhiteSpace(eventData.Title))
        {
            throw new ArgumentException("Event title is required", nameof(eventData.Title));
        }

        if (eventData.Title.Length > 500)
        {
            throw new ArgumentException("Event title cannot exceed 500 characters", nameof(eventData.Title));
        }

        if (eventData.EndDate.HasValue && eventData.EndDate < eventData.StartDate)
        {
            throw new ArgumentException("End date cannot be before start date", nameof(eventData.EndDate));
        }

        if (!string.IsNullOrEmpty(eventData.Category) &&
            !EventCategory.AllCategories.Contains(eventData.Category, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Invalid category: '{eventData.Category}'. Valid categories are: {string.Join(", ", EventCategory.AllCategories)}",
                nameof(eventData.Category));
        }
    }

    /// <summary>
    /// Maps a category to its canonical (lowercase) form from <see cref="EventCategory.AllCategories"/>,
    /// matching case-insensitively (e.g. "Other" -> "other"). Unknown values are returned
    /// unchanged so <see cref="ValidateEvent"/> can reject them with a clear message.
    /// </summary>
    private static string? NormalizeCategory(string? category)
    {
        if (string.IsNullOrEmpty(category))
        {
            return category;
        }

        return EventCategory.AllCategories.FirstOrDefault(
            c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)) ?? category;
    }

    /// <summary>
    /// Fire-and-forget wrapper around <see cref="GenerateEmbeddingForEventAsync"/>:
    /// never throws (embedding generation is non-critical for event creation),
    /// but logs failures at Warning so they are visible.
    /// </summary>
    private async Task GenerateEmbeddingForEventInBackgroundAsync(Event eventData)
    {
        if (_embeddingService == null)
        {
            _logger.LogDebug("Embedding service not available, skipping embedding generation for event {EventId}", eventData.EventId);
            return;
        }

        try
        {
            await GenerateEmbeddingForEventAsync(eventData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background embedding generation failed for event {EventId}", eventData.EventId);
        }
    }

    /// <summary>
    /// Generates and persists an embedding for the given event using its own
    /// short-lived DbContext from the factory. Throws on failure.
    /// </summary>
    private async Task GenerateEmbeddingForEventAsync(Event eventData)
    {
        if (_embeddingService == null)
        {
            throw new InvalidOperationException("Embedding service is not configured");
        }

        _logger.LogInformation("Generating embedding for event {EventId}", eventData.EventId);

        // Create text representation of event
        var text = string.IsNullOrWhiteSpace(eventData.Description)
            ? eventData.Title
            : $"{eventData.Title}. {eventData.Description}";

        // Generate embedding
        var embedding = await _embeddingService.GenerateEmbeddingAsync(text);

        // Save embedding to database using a dedicated context (safe from any thread)
        var eventEmbedding = new EventEmbedding
        {
            EmbeddingId = Guid.NewGuid().ToString(),
            EventId = eventData.EventId,
            EmbeddingVector = System.Text.Json.JsonSerializer.Serialize(embedding),
            EmbeddingProvider = "openai",
            EmbeddingModel = _embeddingService.ModelName,
            EmbeddingDimension = embedding.Length,
            CreatedAt = DateTime.UtcNow
        };

        await using var context = await _contextFactory.CreateDbContextAsync();
        context.EventEmbeddings.Add(eventEmbedding);
        await context.SaveChangesAsync();

        _logger.LogInformation("Embedding generated and saved for event {EventId}", eventData.EventId);
    }

    // Embeddings

    public async Task<bool> HasEmbeddingAsync(string eventId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.EventEmbeddings
                .AsNoTracking()
                .AnyAsync(ee => ee.EventId == eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for embedding: {EventId}", eventId);
            return false;
        }
    }

    /// <summary>
    /// Backfill entry point: generates and persists an embedding for an existing event.
    /// Throws on failure so callers (e.g. the Connections backfill UI) can report errors.
    /// </summary>
    public async Task GenerateEmbeddingAsync(string eventId)
    {
        try
        {
            var eventData = await GetEventByIdAsync(eventId);
            if (eventData == null)
            {
                throw new InvalidOperationException($"Event not found: {eventId}");
            }

            await GenerateEmbeddingForEventAsync(eventData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding for event: {EventId}", eventId);
            throw;
        }
    }
}
