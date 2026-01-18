using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using System.Collections.ObjectModel;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for the timeline view.
/// </summary>
public partial class TimelineViewModel : ObservableObject
{
    private readonly ITimelineService _timelineService;
    private readonly IEventService _eventService;
    private readonly ILogger<TimelineViewModel> _logger;

    [ObservableProperty]
    private TimelineViewport? _viewport;

    [ObservableProperty]
    private ObservableCollection<TimelineEventDto> _events = new();

    [ObservableProperty]
    private ObservableCollection<TimelineEraDto> _eras = new();

    [ObservableProperty]
    private ZoomLevel _currentZoomLevel = ZoomLevel.Month;

    partial void OnCurrentZoomLevelChanged(ZoomLevel value)
    {
        // Notify zoom level boolean properties when zoom level changes
        OnPropertyChanged(nameof(IsYearZoom));
        OnPropertyChanged(nameof(IsMonthZoom));
        OnPropertyChanged(nameof(IsWeekZoom));
        OnPropertyChanged(nameof(IsDayZoom));
        OnPropertyChanged(nameof(CurrentZoomLevelDisplay));
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _totalEventCount;

    [ObservableProperty]
    private TimelineEventDto? _selectedEvent;

    // Viewport dimensions
    [ObservableProperty]
    private int _viewportWidth = 1000;

    [ObservableProperty]
    private int _viewportHeight = 600;

    // Zoom level display properties
    public bool IsYearZoom => CurrentZoomLevel == ZoomLevel.Year;
    public bool IsMonthZoom => CurrentZoomLevel == ZoomLevel.Month;
    public bool IsWeekZoom => CurrentZoomLevel == ZoomLevel.Week;
    public bool IsDayZoom => CurrentZoomLevel == ZoomLevel.Day;
    public string CurrentZoomLevelDisplay => TimelineScale.GetZoomLevelName(CurrentZoomLevel);
    public bool CanZoomIn => TimelineScale.CanZoomIn(CurrentZoomLevel);
    public bool CanZoomOut => TimelineScale.CanZoomOut(CurrentZoomLevel);

    public TimelineViewModel(
        ITimelineService timelineService,
        IEventService eventService,
        ILogger<TimelineViewModel> logger)
    {
        _timelineService = timelineService;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the timeline with default viewport.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "Loading timeline...";

            TotalEventCount = await _eventService.GetTotalEventCountAsync();

            // Create initial viewport
            await CreateViewportAsync(CurrentZoomLevel, DateTime.Now);

            StatusText = $"Loaded {Events.Count} events";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing timeline");
            StatusText = "Error loading timeline";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Initializes the timeline with specified viewport dimensions.
    /// </summary>
    public async Task InitializeAsync(int width, int height)
    {
        ViewportWidth = width;
        ViewportHeight = height;
        await InitializeAsync();
    }

    /// <summary>
    /// Creates a new viewport and loads events.
    /// </summary>
    private async Task CreateViewportAsync(ZoomLevel zoomLevel, DateTime centerDate)
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0)
            return;

        Viewport = await _timelineService.CreateViewportAsync(
            zoomLevel,
            centerDate,
            ViewportWidth,
            ViewportHeight);

        await LoadEventsForViewportAsync();
        await LoadErasForViewportAsync();
    }

    /// <summary>
    /// Loads events for the current viewport.
    /// </summary>
    private async Task LoadEventsForViewportAsync()
    {
        if (Viewport == null) return;

        try
        {
            var events = await _timelineService.GetEventsForViewportAsync(Viewport);
            Events.Clear();
            foreach (var evt in events.Where(e => e.IsVisible))
            {
                Events.Add(evt);
            }

            _logger.LogDebug("Loaded {Count} visible events", Events.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading events for viewport");
        }
    }

    /// <summary>
    /// Loads eras for the current viewport.
    /// </summary>
    private async Task LoadErasForViewportAsync()
    {
        if (Viewport == null) return;

        try
        {
            var eras = await _timelineService.GetErasForViewportAsync(Viewport);
            Eras.Clear();
            foreach (var era in eras.Where(e => e.IsVisible))
            {
                Eras.Add(era);
            }

            _logger.LogDebug("Loaded {Count} visible eras", Eras.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading eras for viewport");
        }
    }

    /// <summary>
    /// Zooms in the timeline.
    /// </summary>
    [RelayCommand]
    public async Task ZoomInAsync()
    {
        if (Viewport == null || IsLoading) return;

        try
        {
            IsLoading = true;
            StatusText = "Zooming in...";

            var centerDate = Viewport.StartDate.AddDays(Viewport.VisibleDays / 2.0);
            Viewport = await _timelineService.ZoomInAsync(Viewport, centerDate);
            CurrentZoomLevel = Viewport.ZoomLevel;

            await LoadEventsForViewportAsync();
            await LoadErasForViewportAsync();

            StatusText = $"Zoom: {TimelineScale.GetZoomLevelName(CurrentZoomLevel)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error zooming in");
            StatusText = "Error zooming in";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Zooms out the timeline.
    /// </summary>
    [RelayCommand]
    public async Task ZoomOutAsync()
    {
        if (Viewport == null || IsLoading) return;

        try
        {
            IsLoading = true;
            StatusText = "Zooming out...";

            var centerDate = Viewport.StartDate.AddDays(Viewport.VisibleDays / 2.0);
            Viewport = await _timelineService.ZoomOutAsync(Viewport, centerDate);
            CurrentZoomLevel = Viewport.ZoomLevel;

            await LoadEventsForViewportAsync();
            await LoadErasForViewportAsync();

            StatusText = $"Zoom: {TimelineScale.GetZoomLevelName(CurrentZoomLevel)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error zooming out");
            StatusText = "Error zooming out";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Sets a specific zoom level.
    /// </summary>
    [RelayCommand]
    private async Task SetZoomLevelAsync(object? parameter)
    {
        if (Viewport == null || IsLoading || parameter == null) return;

        // Parse parameter (can be string or ZoomLevel)
        ZoomLevel zoomLevel;
        if (parameter is ZoomLevel zl)
        {
            zoomLevel = zl;
        }
        else if (parameter is string str && Enum.TryParse<ZoomLevel>(str, out var parsed))
        {
            zoomLevel = parsed;
        }
        else
        {
            _logger.LogWarning("Invalid zoom level parameter: {Parameter}", parameter);
            return;
        }

        try
        {
            IsLoading = true;
            StatusText = $"Setting zoom to {TimelineScale.GetZoomLevelName(zoomLevel)}...";

            var centerDate = Viewport.StartDate.AddDays(Viewport.VisibleDays / 2.0);
            await CreateViewportAsync(zoomLevel, centerDate);
            CurrentZoomLevel = zoomLevel;

            StatusText = $"Zoom: {TimelineScale.GetZoomLevelName(CurrentZoomLevel)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting zoom level");
            StatusText = "Error setting zoom level";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Pans the timeline by pixel offset.
    /// </summary>
    public async Task PanAsync(double pixelOffset)
    {
        if (Viewport == null || IsLoading) return;

        try
        {
            Viewport = await _timelineService.PanAsync(Viewport, pixelOffset);
            await LoadEventsForViewportAsync();
            await LoadErasForViewportAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error panning timeline");
        }
    }

    /// <summary>
    /// Navigates to today's date.
    /// </summary>
    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            StatusText = "Navigating to today...";

            await CreateViewportAsync(CurrentZoomLevel, DateTime.Now);
            StatusText = "Showing today";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to today");
            StatusText = "Error navigating";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Navigates to a specific date.
    /// </summary>
    [RelayCommand]
    private async Task GoToDateAsync(DateTime date)
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            StatusText = $"Navigating to {date:d}...";

            await CreateViewportAsync(CurrentZoomLevel, date);
            StatusText = $"Showing {date:d}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to date");
            StatusText = "Error navigating";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the timeline data.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            StatusText = "Refreshing...";

            TotalEventCount = await _eventService.GetTotalEventCountAsync();

            if (Viewport != null)
            {
                await LoadEventsForViewportAsync();
                await LoadErasForViewportAsync();
            }

            StatusText = $"Refreshed - {Events.Count} events shown";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing timeline");
            StatusText = "Error refreshing";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Selects an event.
    /// </summary>
    [RelayCommand]
    private void SelectEvent(TimelineEventDto? eventDto)
    {
        SelectedEvent = eventDto;
        _logger.LogDebug("Selected event: {EventId}", eventDto?.EventId ?? "none");
    }

    /// <summary>
    /// Updates viewport dimensions when window is resized.
    /// </summary>
    public async Task UpdateViewportDimensionsAsync(int width, int height)
    {
        if (width == ViewportWidth && height == ViewportHeight)
            return;

        ViewportWidth = width;
        ViewportHeight = height;

        if (Viewport != null)
        {
            Viewport.ViewportWidth = width;
            Viewport.ViewportHeight = height;

            await LoadEventsForViewportAsync();
            await LoadErasForViewportAsync();
        }
    }

    #region Event CRUD Operations

    /// <summary>
    /// Creates a new event on the timeline.
    /// </summary>
    public async Task CreateEventAsync(
        string title,
        DateTime date,
        string? description,
        string? category,
        string? location)
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            StatusText = "Creating event...";

            var eventData = new Event
            {
                Title = title,
                StartDate = date,
                Description = description,
                Category = category ?? EventCategory.Other,
                Location = location
            };

            await _eventService.CreateEventAsync(eventData);
            TotalEventCount = await _eventService.GetTotalEventCountAsync();

            // Refresh the timeline to show the new event
            await LoadEventsForViewportAsync();

            StatusText = $"Event '{title}' created";
            _logger.LogInformation("Created event: {Title} on {Date}", title, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event: {Title}", title);
            StatusText = $"Error creating event: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Updates an existing event on the timeline.
    /// </summary>
    public async Task UpdateEventAsync(
        string eventId,
        string title,
        DateTime date,
        string? description,
        string? category,
        string? location)
    {
        if (IsLoading || string.IsNullOrEmpty(eventId)) return;

        try
        {
            IsLoading = true;
            StatusText = "Updating event...";

            var existingEvent = await _eventService.GetEventByIdAsync(eventId);
            if (existingEvent == null)
            {
                StatusText = "Event not found";
                return;
            }

            existingEvent.Title = title;
            existingEvent.StartDate = date;
            existingEvent.Description = description;
            existingEvent.Category = category ?? EventCategory.Other;
            existingEvent.Location = location;

            await _eventService.UpdateEventAsync(existingEvent);

            // Refresh the timeline to show the updated event
            await LoadEventsForViewportAsync();

            // Update selected event if it was the one edited
            if (SelectedEvent?.EventId == eventId)
            {
                SelectedEvent = Events.FirstOrDefault(e => e.EventId == eventId);
            }

            StatusText = $"Event '{title}' updated";
            _logger.LogInformation("Updated event: {EventId} - {Title}", eventId, title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event: {EventId}", eventId);
            StatusText = "Error updating event";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Deletes an event from the timeline.
    /// </summary>
    public async Task DeleteEventAsync(string eventId)
    {
        if (IsLoading || string.IsNullOrEmpty(eventId)) return;

        try
        {
            IsLoading = true;
            StatusText = "Deleting event...";

            await _eventService.DeleteEventAsync(eventId);
            TotalEventCount = await _eventService.GetTotalEventCountAsync();

            // Refresh the timeline
            await LoadEventsForViewportAsync();

            // Clear selection if the deleted event was selected
            if (SelectedEvent?.EventId == eventId)
            {
                SelectedEvent = null;
            }

            StatusText = "Event deleted";
            _logger.LogInformation("Deleted event: {EventId}", eventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event: {EventId}", eventId);
            StatusText = "Error deleting event";
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion
}
