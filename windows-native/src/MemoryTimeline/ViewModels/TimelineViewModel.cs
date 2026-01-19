using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using System.Collections.ObjectModel;
using System.Linq;

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
    private ObservableCollection<TimeRulerTickDto> _timeRulerTicks = new();

    [ObservableProperty]
    private ObservableCollection<EraBarDto> _eraBars = new();

    [ObservableProperty]
    private ObservableCollection<EraFilterDto> _eraFilters = new();

    /// <summary>
    /// Gets the visible era bars (filtered by user selection).
    /// </summary>
    public IEnumerable<EraBarDto> VisibleEraBars =>
        EraBars.Where(eb => EraFilters.FirstOrDefault(f => f.EraId == eb.EraId)?.IsVisible ?? true);

    /// <summary>
    /// Gets the height needed for era bars based on number of tracks.
    /// </summary>
    public double EraBarsHeight => Math.Max(20, _eraBarTrackCount * 8);

    private int _eraBarTrackCount = 1;

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
        GenerateTimeRulerTicks();
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

            // Generate era bars and update filters
            GenerateEraBars(eras);

            _logger.LogDebug("Loaded {Count} visible eras", Eras.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading eras for viewport");
        }
    }

    /// <summary>
    /// Generates era bar data for display as thin horizontal lines.
    /// Stacks overlapping eras on different tracks.
    /// </summary>
    private void GenerateEraBars(IEnumerable<TimelineEraDto> eras)
    {
        EraBars.Clear();

        // Track assignment for non-overlapping display
        var tracks = new List<List<EraBarDto>>();
        const double barHeight = 4.0;
        const double trackSpacing = 8.0;

        foreach (var era in eras.OrderBy(e => e.StartDate))
        {
            var eraBar = EraBarDto.FromEraDto(era);

            // Find available track (non-overlapping)
            int trackIndex = FindAvailableEraTrack(tracks, eraBar);
            eraBar.TrackIndex = trackIndex;
            eraBar.TrackY = trackIndex * trackSpacing;

            EraBars.Add(eraBar);

            // Update filters if this is a new era
            if (!EraFilters.Any(f => f.EraId == era.EraId))
            {
                EraFilters.Add(new EraFilterDto
                {
                    EraId = era.EraId,
                    Name = era.Name,
                    ColorCode = era.ColorCode,
                    IsVisible = true
                });
            }
        }

        _eraBarTrackCount = tracks.Count > 0 ? tracks.Count : 1;
        OnPropertyChanged(nameof(EraBarsHeight));
        OnPropertyChanged(nameof(VisibleEraBars));
    }

    /// <summary>
    /// Finds an available track for an era bar that doesn't overlap with existing bars.
    /// </summary>
    private int FindAvailableEraTrack(List<List<EraBarDto>> tracks, EraBarDto newBar)
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            bool hasOverlap = track.Any(existing =>
            {
                // Check if date ranges overlap
                var existingEnd = existing.PixelX + existing.Width;
                var newEnd = newBar.PixelX + newBar.Width;
                return !(newBar.PixelX >= existingEnd || newEnd <= existing.PixelX);
            });

            if (!hasOverlap)
            {
                track.Add(newBar);
                return i;
            }
        }

        // Create new track
        tracks.Add(new List<EraBarDto> { newBar });
        return tracks.Count - 1;
    }

    /// <summary>
    /// Shows all eras.
    /// </summary>
    public void ShowAllEras()
    {
        foreach (var filter in EraFilters)
        {
            filter.IsVisible = true;
        }
        OnPropertyChanged(nameof(VisibleEraBars));
    }

    /// <summary>
    /// Hides all eras.
    /// </summary>
    public void HideAllEras()
    {
        foreach (var filter in EraFilters)
        {
            filter.IsVisible = false;
        }
        OnPropertyChanged(nameof(VisibleEraBars));
    }

    /// <summary>
    /// Generates time ruler ticks based on current viewport.
    /// Uses Adobe Premiere-style adaptive tick density algorithm.
    /// </summary>
    private void GenerateTimeRulerTicks()
    {
        if (Viewport == null) return;

        try
        {
            TimeRulerTicks.Clear();

            // Create coordinate converter from viewport
            var converter = TimelineCoordinateConverter.FromViewport(Viewport);

            // Get optimal ruler configuration based on zoom level
            var rulerConfig = TimeRulerConfig.Calculate(Viewport.PixelsPerDay);

            // Generate ticks
            var ticks = rulerConfig.GenerateTicks(converter);

            foreach (var tick in ticks)
            {
                TimeRulerTicks.Add(new TimeRulerTickDto
                {
                    Date = tick.Date,
                    PixelX = tick.ScreenX,
                    IsMajor = tick.IsMajor,
                    Label = tick.Label
                });
            }

            _logger.LogDebug("Generated {Count} time ruler ticks", TimeRulerTicks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating time ruler ticks");
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
    /// Performs cursor-anchored zoom following Adobe Premiere's algorithm.
    /// The timecode under the cursor stays visually fixed while the timeline expands/contracts.
    /// </summary>
    /// <param name="cursorScreenX">Cursor X position in pixels relative to viewport</param>
    /// <param name="wheelDelta">Raw mouse wheel delta (typically ±120 per tick)</param>
    public async Task CursorAnchoredZoomAsync(double cursorScreenX, double wheelDelta)
    {
        if (Viewport == null) return;

        try
        {
            // Calculate new viewport state using Premiere-style zoom
            var (newStartDate, newPixelsPerDay) = ZoomHelper.CalculateCursorAnchoredZoom(
                Viewport,
                cursorScreenX,
                wheelDelta,
                minPixelsPerDay: 0.01,  // ~100 years visible
                maxPixelsPerDay: 50.0    // ~20 days visible at most
            );

            // Calculate new end date
            var newVisibleDays = Viewport.ViewportWidth / newPixelsPerDay;
            var newEndDate = newStartDate.AddDays(newVisibleDays);
            var newCenterDate = newStartDate.AddDays(newVisibleDays / 2);

            // Update viewport
            Viewport.StartDate = newStartDate;
            Viewport.EndDate = newEndDate;
            Viewport.CenterDate = newCenterDate;
            Viewport.PixelsPerDay = newPixelsPerDay;
            Viewport.ZoomLevel = ZoomHelper.GetClosestZoomLevel(newPixelsPerDay);

            // Update current zoom level display
            CurrentZoomLevel = Viewport.ZoomLevel;

            // Reload events and ticks
            await LoadEventsForViewportAsync();
            await LoadErasForViewportAsync();
            GenerateTimeRulerTicks();

            StatusText = $"Zoom: {newPixelsPerDay:F2} px/day";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing cursor-anchored zoom");
        }
    }

    /// <summary>
    /// Performs center-anchored zoom (wheel zoom without cursor position).
    /// </summary>
    /// <param name="wheelDelta">Raw mouse wheel delta (typically ±120 per tick)</param>
    public async Task CenterAnchoredZoomAsync(double wheelDelta)
    {
        if (Viewport == null) return;
        await CursorAnchoredZoomAsync(Viewport.ViewportWidth / 2.0, wheelDelta);
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
            GenerateTimeRulerTicks();
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
    /// Sets the viewport to show a 3-year span with the specified year in the center third.
    /// Year-1 in left third, specified year in middle third, year+1 in right third.
    /// </summary>
    public async Task SetViewportToYearSpanAsync(int year)
    {
        if (IsLoading || ViewportWidth <= 0) return;

        try
        {
            IsLoading = true;
            StatusText = $"Navigating to {year}...";

            // Calculate the 3-year date range
            var startDate = new DateTime(year - 1, 1, 1);
            var endDate = new DateTime(year + 2, 1, 1); // Jan 1 of year+2 to include all of year+1
            var centerDate = new DateTime(year, 7, 1); // Middle of the target year

            // Calculate pixels per day to fit exactly 3 years in the viewport
            var totalDays = (endDate - startDate).TotalDays;
            var pixelsPerDay = ViewportWidth / totalDays;

            // Create a custom viewport for this specific date range
            Viewport = new TimelineViewport
            {
                StartDate = startDate,
                EndDate = endDate,
                CenterDate = centerDate,
                PixelsPerDay = pixelsPerDay,
                ZoomLevel = ZoomHelper.GetClosestZoomLevel(pixelsPerDay),
                ViewportWidth = ViewportWidth,
                ViewportHeight = ViewportHeight,
                ScrollPosition = 0
            };

            CurrentZoomLevel = Viewport.ZoomLevel;

            // Reload events and eras for the new viewport
            await LoadEventsForViewportAsync();
            await LoadErasForViewportAsync();
            GenerateTimeRulerTicks();

            StatusText = $"Showing {year - 1} - {year} - {year + 1}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting year span viewport");
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

            if (Viewport == null && ViewportWidth > 0 && ViewportHeight > 0)
            {
                // Create viewport if it doesn't exist but we have valid dimensions
                await CreateViewportAsync(CurrentZoomLevel, DateTime.Now);
            }
            else if (Viewport != null)
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
    /// Creates the viewport if it doesn't exist yet.
    /// </summary>
    public async Task UpdateViewportDimensionsAsync(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        var dimensionsChanged = width != ViewportWidth || height != ViewportHeight;

        ViewportWidth = width;
        ViewportHeight = height;

        if (Viewport == null)
        {
            // Viewport doesn't exist yet - create it with current dimensions
            await CreateViewportAsync(CurrentZoomLevel, DateTime.Now);
        }
        else if (dimensionsChanged)
        {
            // Update existing viewport dimensions
            Viewport.ViewportWidth = width;
            Viewport.ViewportHeight = height;

            await LoadEventsForViewportAsync();
            await LoadErasForViewportAsync();
            GenerateTimeRulerTicks();
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
