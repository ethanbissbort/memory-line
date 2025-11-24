using Microsoft.Extensions.Logging;
using MemoryTimeline.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// RAG (Retrieval-Augmented Generation) service implementation.
/// </summary>
public class RagService : IRagService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILlmService _llmService;
    private readonly IEventService _eventService;
    private readonly Data.AppDbContext _dbContext;
    private readonly ILogger<RagService> _logger;

    public RagService(
        IEmbeddingService embeddingService,
        ILlmService llmService,
        IEventService eventService,
        Data.AppDbContext dbContext,
        ILogger<RagService> logger)
    {
        _embeddingService = embeddingService;
        _llmService = llmService;
        _eventService = eventService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Finds similar events using embedding-based similarity search.
    /// </summary>
    public async Task<List<SimilarEvent>> FindSimilarEventsAsync(string eventId, int topK = 10, double threshold = 0.7)
    {
        try
        {
            _logger.LogInformation("Finding similar events for {EventId}", eventId);

            // Get the source event and its embedding
            var sourceEventEmbedding = await _dbContext.EventEmbeddings
                .FirstOrDefaultAsync(ee => ee.EventId == eventId);

            if (sourceEventEmbedding == null || sourceEventEmbedding.Embedding == null)
            {
                _logger.LogWarning("No embedding found for event {EventId}", eventId);
                return new List<SimilarEvent>();
            }

            var queryEmbedding = JsonSerializer.Deserialize<float[]>(sourceEventEmbedding.Embedding);
            if (queryEmbedding == null)
            {
                throw new Exception("Failed to deserialize embedding");
            }

            // Get all other event embeddings
            var allEmbeddings = await _dbContext.EventEmbeddings
                .Where(ee => ee.EventId != eventId && ee.Embedding != null)
                .ToListAsync();

            var candidateEmbeddings = allEmbeddings
                .Select(ee => (ee.EventId, JsonSerializer.Deserialize<float[]>(ee.Embedding!)!))
                .ToList();

            // Find K nearest neighbors
            var similarities = _embeddingService.FindKNearestNeighbors(
                queryEmbedding,
                candidateEmbeddings,
                topK,
                threshold);

            // Get event details
            var similarEvents = new List<SimilarEvent>();
            foreach (var similarity in similarities)
            {
                var eventEntity = await _eventService.GetEventByIdAsync(similarity.Id);
                if (eventEntity != null)
                {
                    similarEvents.Add(new SimilarEvent
                    {
                        EventId = eventEntity.EventId,
                        Title = eventEntity.Title,
                        Description = eventEntity.Description,
                        StartDate = eventEntity.StartDate,
                        Similarity = similarity.Similarity
                    });
                }
            }

            _logger.LogInformation("Found {Count} similar events for {EventId}", similarEvents.Count, eventId);
            return similarEvents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding similar events");
            throw;
        }
    }

    /// <summary>
    /// Detects cross-references using LLM analysis.
    /// </summary>
    public async Task<List<CrossReferenceResult>> DetectCrossReferencesAsync(string eventId, IEnumerable<string>? candidateEventIds = null)
    {
        try
        {
            _logger.LogInformation("Detecting cross-references for event {EventId}", eventId);

            var sourceEvent = await _eventService.GetEventWithDetailsAsync(eventId);
            if (sourceEvent == null)
            {
                throw new Exception($"Event {eventId} not found");
            }

            // Get candidate events (either specified or find similar)
            List<Event> candidates;
            if (candidateEventIds != null && candidateEventIds.Any())
            {
                candidates = new List<Event>();
                foreach (var id in candidateEventIds)
                {
                    var evt = await _eventService.GetEventWithDetailsAsync(id);
                    if (evt != null) candidates.Add(evt);
                }
            }
            else
            {
                // Find similar events as candidates
                var similarEvents = await FindSimilarEventsAsync(eventId, topK: 20, threshold: 0.6);
                candidates = new List<Event>();
                foreach (var similar in similarEvents)
                {
                    var evt = await _eventService.GetEventWithDetailsAsync(similar.EventId);
                    if (evt != null) candidates.Add(evt);
                }
            }

            if (candidates.Count == 0)
            {
                return new List<CrossReferenceResult>();
            }

            // Use LLM to analyze relationships
            var crossReferences = await AnalyzeRelationshipsWithLLMAsync(sourceEvent, candidates);

            _logger.LogInformation("Detected {Count} cross-references for event {EventId}",
                crossReferences.Count, eventId);

            return crossReferences;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting cross-references");
            throw;
        }
    }

    /// <summary>
    /// Detects patterns in events.
    /// </summary>
    public async Task<PatternAnalysisResult> DetectPatternsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            _logger.LogInformation("Detecting patterns in events");

            var result = new PatternAnalysisResult();

            // Get events in date range
            var events = startDate.HasValue && endDate.HasValue
                ? await _eventService.GetEventsByDateRangeAsync(startDate.Value, endDate.Value)
                : await _eventService.GetAllEventsAsync();

            var eventList = events.ToList();

            if (eventList.Count == 0)
            {
                return result;
            }

            // Detect category patterns
            result.CategoryPatterns = DetectCategoryPatterns(eventList);

            // Detect temporal clusters
            result.TemporalClusters = DetectTemporalClusters(eventList);

            // Detect era transitions
            result.EraTransitions = DetectEraTransitions(eventList);

            _logger.LogInformation("Detected {CategoryPatterns} category patterns, {TemporalClusters} temporal clusters, {EraTransitions} era transitions",
                result.CategoryPatterns.Count, result.TemporalClusters.Count, result.EraTransitions.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting patterns");
            throw;
        }
    }

    /// <summary>
    /// Suggests tags for an event based on similar events.
    /// </summary>
    public async Task<List<TagSuggestion>> SuggestTagsAsync(string eventId, int maxSuggestions = 5)
    {
        try
        {
            _logger.LogInformation("Suggesting tags for event {EventId}", eventId);

            // Find similar events
            var similarEvents = await FindSimilarEventsAsync(eventId, topK: 10, threshold: 0.75);

            if (similarEvents.Count == 0)
            {
                return new List<TagSuggestion>();
            }

            // Get tags from similar events with frequency counting
            var tagFrequency = new Dictionary<string, (int count, double totalSimilarity)>();

            foreach (var similar in similarEvents)
            {
                var eventTags = await _eventService.GetEventTagsAsync(similar.EventId);

                foreach (var tag in eventTags)
                {
                    if (tagFrequency.ContainsKey(tag.Name))
                    {
                        var current = tagFrequency[tag.Name];
                        tagFrequency[tag.Name] = (current.count + 1, current.totalSimilarity + similar.Similarity);
                    }
                    else
                    {
                        tagFrequency[tag.Name] = (1, similar.Similarity);
                    }
                }
            }

            // Calculate confidence scores and create suggestions
            var suggestions = tagFrequency
                .Select(kvp => new TagSuggestion
                {
                    TagName = kvp.Key,
                    Confidence = (kvp.Value.totalSimilarity / similarEvents.Count) * (kvp.Value.count / (double)similarEvents.Count),
                    Reason = $"Found in {kvp.Value.count} similar events"
                })
                .OrderByDescending(s => s.Confidence)
                .Take(maxSuggestions)
                .ToList();

            _logger.LogInformation("Generated {Count} tag suggestions for event {EventId}", suggestions.Count, eventId);
            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error suggesting tags");
            throw;
        }
    }

    /// <summary>
    /// Suggests tags for text before event creation.
    /// </summary>
    public async Task<List<TagSuggestion>> SuggestTagsForTextAsync(string title, string? description, int maxSuggestions = 5)
    {
        try
        {
            _logger.LogInformation("Suggesting tags for text: {Title}", title);

            // Generate embedding for the text
            var text = string.IsNullOrWhiteSpace(description) ? title : $"{title}. {description}";
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(text);

            // Get all event embeddings
            var allEmbeddings = await _dbContext.EventEmbeddings
                .Where(ee => ee.Embedding != null)
                .ToListAsync();

            var candidateEmbeddings = allEmbeddings
                .Select(ee => (ee.EventId, JsonSerializer.Deserialize<float[]>(ee.Embedding!)!))
                .ToList();

            // Find similar events
            var similarities = _embeddingService.FindKNearestNeighbors(
                queryEmbedding,
                candidateEmbeddings,
                10,
                0.7);

            // Get tags from similar events
            var tagFrequency = new Dictionary<string, (int count, double totalSimilarity)>();

            foreach (var similarity in similarities)
            {
                var eventTags = await _eventService.GetEventTagsAsync(similarity.Id);

                foreach (var tag in eventTags)
                {
                    if (tagFrequency.ContainsKey(tag.Name))
                    {
                        var current = tagFrequency[tag.Name];
                        tagFrequency[tag.Name] = (current.count + 1, current.totalSimilarity + similarity.Similarity);
                    }
                    else
                    {
                        tagFrequency[tag.Name] = (1, similarity.Similarity);
                    }
                }
            }

            // Calculate confidence and create suggestions
            var suggestions = tagFrequency
                .Select(kvp => new TagSuggestion
                {
                    TagName = kvp.Key,
                    Confidence = (kvp.Value.totalSimilarity / similarities.Count) * (kvp.Value.count / (double)similarities.Count),
                    Reason = $"Found in {kvp.Value.count} similar events"
                })
                .OrderByDescending(s => s.Confidence)
                .Take(maxSuggestions)
                .ToList();

            _logger.LogInformation("Generated {Count} tag suggestions for text", suggestions.Count);
            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error suggesting tags for text");
            throw;
        }
    }

    #region Private Methods

    private async Task<List<CrossReferenceResult>> AnalyzeRelationshipsWithLLMAsync(Event sourceEvent, List<Event> candidates)
    {
        // Build prompt for LLM analysis
        var prompt = BuildCrossReferencePrompt(sourceEvent, candidates);

        // Call LLM
        var extraction = await _llmService.ExtractEventsAsync(prompt, null);

        // Parse results (simplified - in production, use structured output)
        var crossReferences = new List<CrossReferenceResult>();

        // TODO: Implement proper LLM-based relationship detection
        // For now, use simple heuristics

        foreach (var candidate in candidates)
        {
            var relationship = DetermineRelationship(sourceEvent, candidate);
            if (relationship != null)
            {
                crossReferences.Add(relationship);
            }
        }

        return crossReferences;
    }

    private string BuildCrossReferencePrompt(Event sourceEvent, List<Event> candidates)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("Analyze the relationships between these events:");
        sb.AppendLine();
        sb.AppendLine($"Source Event: {sourceEvent.Title}");
        sb.AppendLine($"Description: {sourceEvent.Description}");
        sb.AppendLine($"Date: {sourceEvent.StartDate:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("Candidate Events:");

        foreach (var candidate in candidates)
        {
            sb.AppendLine($"- {candidate.Title} ({candidate.StartDate:yyyy-MM-dd})");
        }

        return sb.ToString();
    }

    private CrossReferenceResult? DetermineRelationship(Event source, Event target)
    {
        // Simple heuristic-based relationship detection
        var daysDifference = Math.Abs((target.StartDate - source.StartDate).TotalDays);

        if (daysDifference <= 7)
        {
            return new CrossReferenceResult
            {
                SourceEventId = source.EventId,
                TargetEventId = target.EventId,
                RelationshipType = CrossReferenceType.Temporal,
                Confidence = 0.8,
                Reasoning = $"Events occurred within {daysDifference} days of each other"
            };
        }

        if (source.Category == target.Category)
        {
            return new CrossReferenceResult
            {
                SourceEventId = source.EventId,
                TargetEventId = target.EventId,
                RelationshipType = CrossReferenceType.Thematic,
                Confidence = 0.7,
                Reasoning = $"Both events are in the {source.Category} category"
            };
        }

        return null;
    }

    private List<CategoryPattern> DetectCategoryPatterns(List<Event> events)
    {
        var patterns = new List<CategoryPattern>();

        var categoryGroups = events.GroupBy(e => e.Category);

        foreach (var group in categoryGroups)
        {
            var pattern = new CategoryPattern
            {
                Category = group.Key,
                EventCount = group.Count()
            };

            // Calculate frequency (events per month)
            if (events.Count > 0)
            {
                var dateRange = events.Max(e => e.StartDate) - events.Min(e => e.StartDate);
                var months = Math.Max(1, dateRange.TotalDays / 30);
                pattern.Frequency = group.Count() / months;
            }

            patterns.Add(pattern);
        }

        return patterns.OrderByDescending(p => p.EventCount).ToList();
    }

    private List<TemporalCluster> DetectTemporalClusters(List<Event> events)
    {
        var clusters = new List<TemporalCluster>();
        var sortedEvents = events.OrderBy(e => e.StartDate).ToList();

        // Simple clustering: group events within 30 days
        for (int i = 0; i < sortedEvents.Count; i++)
        {
            var clusterEvents = new List<Event> { sortedEvents[i] };
            var startDate = sortedEvents[i].StartDate;

            for (int j = i + 1; j < sortedEvents.Count; j++)
            {
                if ((sortedEvents[j].StartDate - startDate).TotalDays <= 30)
                {
                    clusterEvents.Add(sortedEvents[j]);
                    i = j;
                }
                else
                {
                    break;
                }
            }

            if (clusterEvents.Count >= 3)  // Only clusters with 3+ events
            {
                clusters.Add(new TemporalCluster
                {
                    StartDate = clusterEvents.First().StartDate,
                    EndDate = clusterEvents.Last().StartDate,
                    EventCount = clusterEvents.Count,
                    EventIds = clusterEvents.Select(e => e.EventId).ToList(),
                    Theme = DetermineClusterTheme(clusterEvents)
                });
            }
        }

        return clusters;
    }

    private List<EraTransition> DetectEraTransitions(List<Event> events)
    {
        var transitions = new List<EraTransition>();

        // Simple transition detection based on category changes
        var sortedEvents = events.OrderBy(e => e.StartDate).ToList();

        for (int i = 1; i < sortedEvents.Count - 1; i++)
        {
            var prevCategory = sortedEvents[i - 1].Category;
            var currentCategory = sortedEvents[i].Category;

            if (prevCategory != currentCategory)
            {
                // Check if this is a sustained change (next few events are also different)
                var nextEvents = sortedEvents.Skip(i).Take(3).ToList();
                if (nextEvents.Count(e => e.Category == currentCategory) >= 2)
                {
                    transitions.Add(new EraTransition
                    {
                        TransitionDate = sortedEvents[i].StartDate,
                        FromTheme = prevCategory.ToString(),
                        ToTheme = currentCategory.ToString(),
                        Description = $"Transition from {prevCategory} to {currentCategory} focus"
                    });
                }
            }
        }

        return transitions;
    }

    private string? DetermineClusterTheme(List<Event> events)
    {
        // Find most common category in cluster
        var commonCategory = events
            .GroupBy(e => e.Category)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;

        return commonCategory.ToString();
    }

    #endregion
}
