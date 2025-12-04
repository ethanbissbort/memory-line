using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for the Connections page, displaying cross-references and similar events.
/// </summary>
public partial class ConnectionsViewModel : ObservableObject
{
    private readonly IRagService _ragService;
    private readonly IEventService _eventService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<ConnectionsViewModel> _logger;

    [ObservableProperty]
    private string? _selectedEventId;

    [ObservableProperty]
    private string? _selectedEventTitle;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasSelectedEvent;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private ObservableCollection<SimilarEventDto> _similarEvents = new();

    [ObservableProperty]
    private ObservableCollection<CrossReferenceDto> _crossReferences = new();

    [ObservableProperty]
    private ObservableCollection<TagSuggestionDto> _tagSuggestions = new();

    [ObservableProperty]
    private int _similarEventsCount;

    [ObservableProperty]
    private int _crossReferencesCount;

    [ObservableProperty]
    private int _tagSuggestionsCount;

    public ConnectionsViewModel(
        IRagService ragService,
        IEventService eventService,
        INavigationService navigationService,
        ILogger<ConnectionsViewModel> logger)
    {
        _ragService = ragService;
        _eventService = eventService;
        _navigationService = navigationService;
        _logger = logger;
    }

    /// <summary>
    /// Loads connections for a specific event.
    /// </summary>
    public async Task LoadConnectionsForEventAsync(string eventId)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            SelectedEventId = eventId;

            _logger.LogInformation("Loading connections for event {EventId}", eventId);

            // Load event details
            var eventDetails = await _eventService.GetEventByIdAsync(eventId);
            if (eventDetails == null)
            {
                ErrorMessage = "Event not found";
                HasSelectedEvent = false;
                return;
            }

            SelectedEventTitle = eventDetails.Title;
            HasSelectedEvent = true;

            // Load similar events
            await LoadSimilarEventsAsync(eventId);

            // Load cross-references
            await LoadCrossReferencesAsync(eventId);

            // Load tag suggestions
            await LoadTagSuggestionsAsync(eventId);

            _logger.LogInformation("Loaded connections: {Similar} similar, {CrossRefs} cross-refs, {Tags} tag suggestions",
                SimilarEventsCount, CrossReferencesCount, TagSuggestionsCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading connections for event {EventId}", eventId);
            ErrorMessage = $"Error loading connections: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads similar events.
    /// </summary>
    private async Task LoadSimilarEventsAsync(string eventId)
    {
        try
        {
            var similar = await _ragService.FindSimilarEventsAsync(eventId, topK: 10, threshold: 0.7);

            SimilarEvents.Clear();
            foreach (var item in similar)
            {
                SimilarEvents.Add(new SimilarEventDto
                {
                    EventId = item.EventId,
                    Title = item.Title,
                    Description = item.Description,
                    StartDate = item.StartDate,
                    Similarity = item.Similarity
                });
            }

            SimilarEventsCount = SimilarEvents.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading similar events");
        }
    }

    /// <summary>
    /// Loads cross-references.
    /// </summary>
    private async Task LoadCrossReferencesAsync(string eventId)
    {
        try
        {
            var crossRefs = await _ragService.DetectCrossReferencesAsync(eventId);

            CrossReferences.Clear();
            foreach (var item in crossRefs)
            {
                // Load target event details
                var targetEvent = await _eventService.GetEventByIdAsync(item.TargetEventId);
                if (targetEvent != null)
                {
                    CrossReferences.Add(new CrossReferenceDto
                    {
                        SourceEventId = item.SourceEventId,
                        TargetEventId = item.TargetEventId,
                        TargetEventTitle = targetEvent.Title,
                        TargetEventDate = targetEvent.StartDate,
                        RelationshipType = item.RelationshipType,
                        Confidence = item.Confidence,
                        Reasoning = item.Reasoning
                    });
                }
            }

            CrossReferencesCount = CrossReferences.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading cross-references");
        }
    }

    /// <summary>
    /// Loads tag suggestions.
    /// </summary>
    private async Task LoadTagSuggestionsAsync(string eventId)
    {
        try
        {
            var suggestions = await _ragService.SuggestTagsAsync(eventId, maxSuggestions: 5);

            TagSuggestions.Clear();
            foreach (var item in suggestions)
            {
                TagSuggestions.Add(new TagSuggestionDto
                {
                    TagName = item.TagName,
                    Confidence = item.Confidence,
                    Reason = item.Reason
                });
            }

            TagSuggestionsCount = TagSuggestions.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading tag suggestions");
        }
    }

    /// <summary>
    /// Refreshes all connections data.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!string.IsNullOrEmpty(SelectedEventId))
        {
            await LoadConnectionsForEventAsync(SelectedEventId);
        }
    }

    /// <summary>
    /// Navigates to a specific event on the timeline.
    /// </summary>
    [RelayCommand]
    private void NavigateToEvent(string eventId)
    {
        _logger.LogInformation("Navigating to event {EventId}", eventId);
        _navigationService.NavigateTo("TimelinePage", eventId);
    }

    /// <summary>
    /// Generates embeddings for all events that don't have them.
    /// </summary>
    [RelayCommand]
    private async Task GenerateEmbeddingsAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            _logger.LogInformation("Starting embedding generation workflow");

            // Get all events without embeddings
            var allEvents = await _eventService.GetAllEventsAsync();
            var eventsNeedingEmbeddings = new List<Data.Models.Event>();

            foreach (var evt in allEvents)
            {
                var hasEmbedding = await _eventService.HasEmbeddingAsync(evt.EventId);
                if (!hasEmbedding)
                {
                    eventsNeedingEmbeddings.Add(evt);
                }
            }

            if (eventsNeedingEmbeddings.Count == 0)
            {
                _logger.LogInformation("All events already have embeddings");
                return;
            }

            _logger.LogInformation("Generating embeddings for {Count} events", eventsNeedingEmbeddings.Count);

            // Generate embeddings in batches
            int processed = 0;
            foreach (var evt in eventsNeedingEmbeddings)
            {
                try
                {
                    await _eventService.GenerateEmbeddingAsync(evt.EventId);
                    processed++;

                    if (processed % 10 == 0)
                    {
                        _logger.LogInformation("Processed {Processed} of {Total} events", processed, eventsNeedingEmbeddings.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating embedding for event {EventId}", evt.EventId);
                }
            }

            _logger.LogInformation("Embedding generation complete: {Processed} of {Total} successful",
                processed, eventsNeedingEmbeddings.Count);

            // Refresh current view
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in embedding generation workflow");
            ErrorMessage = $"Error generating embeddings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

/// <summary>
/// DTO for displaying similar events.
/// </summary>
public class SimilarEventDto : ObservableObject
{
    private string _eventId = string.Empty;
    private string _title = string.Empty;
    private string? _description;
    private DateTime _startDate;
    private double _similarity;

    public string EventId
    {
        get => _eventId;
        set => SetProperty(ref _eventId, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public DateTime StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value);
    }

    public double Similarity
    {
        get => _similarity;
        set => SetProperty(ref _similarity, value);
    }

    public string SimilarityDisplay => $"{Similarity:P0}";

    public string StartDateDisplay => StartDate.ToString("MMM d, yyyy");
}

/// <summary>
/// DTO for displaying cross-references.
/// </summary>
public class CrossReferenceDto : ObservableObject
{
    private string _sourceEventId = string.Empty;
    private string _targetEventId = string.Empty;
    private string _targetEventTitle = string.Empty;
    private DateTime _targetEventDate;
    private CrossReferenceType _relationshipType;
    private double _confidence;
    private string? _reasoning;

    public string SourceEventId
    {
        get => _sourceEventId;
        set => SetProperty(ref _sourceEventId, value);
    }

    public string TargetEventId
    {
        get => _targetEventId;
        set => SetProperty(ref _targetEventId, value);
    }

    public string TargetEventTitle
    {
        get => _targetEventTitle;
        set => SetProperty(ref _targetEventTitle, value);
    }

    public DateTime TargetEventDate
    {
        get => _targetEventDate;
        set => SetProperty(ref _targetEventDate, value);
    }

    public CrossReferenceType RelationshipType
    {
        get => _relationshipType;
        set => SetProperty(ref _relationshipType, value);
    }

    public double Confidence
    {
        get => _confidence;
        set => SetProperty(ref _confidence, value);
    }

    public string? Reasoning
    {
        get => _reasoning;
        set => SetProperty(ref _reasoning, value);
    }

    public string ConfidenceDisplay => $"{Confidence:P0}";

    public string TargetDateDisplay => TargetEventDate.ToString("MMM d, yyyy");

    public string RelationshipIcon => RelationshipType switch
    {
        CrossReferenceType.Causal => "🔗",
        CrossReferenceType.Thematic => "📌",
        CrossReferenceType.Temporal => "⏰",
        CrossReferenceType.Participatory => "👥",
        CrossReferenceType.Locational => "📍",
        CrossReferenceType.Consequential => "➡️",
        _ => "●"
    };

    public string RelationshipTypeDisplay => RelationshipType.ToString();
}

/// <summary>
/// DTO for displaying tag suggestions.
/// </summary>
public class TagSuggestionDto : ObservableObject
{
    private string _tagName = string.Empty;
    private double _confidence;
    private string? _reason;

    public string TagName
    {
        get => _tagName;
        set => SetProperty(ref _tagName, value);
    }

    public double Confidence
    {
        get => _confidence;
        set => SetProperty(ref _confidence, value);
    }

    public string? Reason
    {
        get => _reason;
        set => SetProperty(ref _reason, value);
    }

    public string ConfidenceDisplay => $"{Confidence:P0}";
}
