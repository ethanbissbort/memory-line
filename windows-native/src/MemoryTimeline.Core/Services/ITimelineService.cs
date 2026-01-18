using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Data.Repositories;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for timeline operations.
/// </summary>
public interface ITimelineService
{
    Task<IEnumerable<TimelineEventDto>> GetEventsForViewportAsync(TimelineViewport viewport);
    Task<IEnumerable<TimelineEraDto>> GetErasForViewportAsync(TimelineViewport viewport);
    Task<TimelineViewport> CreateViewportAsync(ZoomLevel zoomLevel, DateTime centerDate, int viewportWidth, int viewportHeight);
    Task<TimelineViewport> ZoomInAsync(TimelineViewport currentViewport, DateTime? centerDate = null);
    Task<TimelineViewport> ZoomOutAsync(TimelineViewport currentViewport, DateTime? centerDate = null);
    Task<TimelineViewport> PanAsync(TimelineViewport currentViewport, double pixelOffset);
    Task<DateTime> GetEarliestEventDateAsync();
    Task<DateTime> GetLatestEventDateAsync();
    void CalculateEventPositions(IEnumerable<TimelineEventDto> events, TimelineViewport viewport);
    void CalculateEraPositions(IEnumerable<TimelineEraDto> eras, TimelineViewport viewport);
}

/// <summary>
/// Timeline service implementation.
/// </summary>
public class TimelineService : ITimelineService
{
    private readonly IEventRepository _eventRepository;
    private readonly IEraRepository _eraRepository;
    private readonly ILogger<TimelineService> _logger;

    public TimelineService(
        IEventRepository eventRepository,
        IEraRepository eraRepository,
        ILogger<TimelineService> logger)
    {
        _eventRepository = eventRepository;
        _eraRepository = eraRepository;
        _logger = logger;
    }

    /// <summary>
    /// Gets events visible in the current viewport.
    /// </summary>
    public async Task<IEnumerable<TimelineEventDto>> GetEventsForViewportAsync(TimelineViewport viewport)
    {
        try
        {
            // Add buffer to load events slightly outside viewport
            var bufferDays = 30;
            var startDate = viewport.StartDate.AddDays(-bufferDays);
            var endDate = viewport.EndDate.AddDays(bufferDays);

            var events = await _eventRepository.GetByDateRangeAsync(startDate, endDate);
            var dtos = events.Select(TimelineEventDto.FromEvent).ToList();

            CalculateEventPositions(dtos, viewport);

            _logger.LogDebug("Loaded {Count} events for viewport", dtos.Count);
            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events for viewport");
            throw;
        }
    }

    /// <summary>
    /// Gets eras visible in the current viewport.
    /// </summary>
    public async Task<IEnumerable<TimelineEraDto>> GetErasForViewportAsync(TimelineViewport viewport)
    {
        try
        {
            var allEras = await _eraRepository.GetAllAsync();
            var dtos = allEras
                .Where(era => era.StartDate <= viewport.EndDate &&
                             (era.EndDate == null || era.EndDate >= viewport.StartDate))
                .Select(TimelineEraDto.FromEra)
                .ToList();

            CalculateEraPositions(dtos, viewport);

            _logger.LogDebug("Loaded {Count} eras for viewport", dtos.Count);
            return dtos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting eras for viewport");
            throw;
        }
    }

    /// <summary>
    /// Creates a new viewport with specified parameters.
    /// </summary>
    public async Task<TimelineViewport> CreateViewportAsync(
        ZoomLevel zoomLevel,
        DateTime centerDate,
        int viewportWidth,
        int viewportHeight)
    {
        try
        {
            // If no center date provided, calculate from events
            if (centerDate == DateTime.MinValue)
            {
                var earliest = await GetEarliestEventDateAsync();
                var latest = await GetLatestEventDateAsync();

                if (earliest != DateTime.MinValue && latest != DateTime.MaxValue)
                {
                    centerDate = earliest.AddDays((latest - earliest).TotalDays / 2);
                }
                else
                {
                    centerDate = DateTime.Now;
                }
            }

            // Use the new TimelineViewport.CreateCentered method
            var viewport = TimelineViewport.CreateCentered(centerDate, zoomLevel, viewportWidth);
            viewport.ViewportHeight = viewportHeight;

            return viewport;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating viewport");
            throw;
        }
    }

    /// <summary>
    /// Zooms in the viewport.
    /// </summary>
    public async Task<TimelineViewport> ZoomInAsync(TimelineViewport currentViewport, DateTime? centerDate = null)
    {
        try
        {
            if (!TimelineScale.CanZoomIn(currentViewport.ZoomLevel))
            {
                _logger.LogDebug("Already at maximum zoom level");
                return currentViewport;
            }

            var newZoomLevel = TimelineScale.ZoomIn(currentViewport.ZoomLevel);
            var center = centerDate ?? currentViewport.CenterDate;

            return await CreateViewportAsync(
                newZoomLevel,
                center,
                (int)currentViewport.ViewportWidth,
                (int)currentViewport.ViewportHeight);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error zooming in");
            throw;
        }
    }

    /// <summary>
    /// Zooms out the viewport.
    /// </summary>
    public async Task<TimelineViewport> ZoomOutAsync(TimelineViewport currentViewport, DateTime? centerDate = null)
    {
        try
        {
            if (!TimelineScale.CanZoomOut(currentViewport.ZoomLevel))
            {
                _logger.LogDebug("Already at minimum zoom level");
                return currentViewport;
            }

            var newZoomLevel = TimelineScale.ZoomOut(currentViewport.ZoomLevel);
            var center = centerDate ?? currentViewport.CenterDate;

            return await CreateViewportAsync(
                newZoomLevel,
                center,
                (int)currentViewport.ViewportWidth,
                (int)currentViewport.ViewportHeight);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error zooming out");
            throw;
        }
    }

    /// <summary>
    /// Pans the viewport by pixel offset.
    /// </summary>
    public Task<TimelineViewport> PanAsync(TimelineViewport currentViewport, double pixelOffset)
    {
        try
        {
            // Create a copy and pan it
            var newViewport = new TimelineViewport
            {
                StartDate = currentViewport.StartDate,
                EndDate = currentViewport.EndDate,
                CenterDate = currentViewport.CenterDate,
                ZoomLevel = currentViewport.ZoomLevel,
                PixelsPerDay = currentViewport.PixelsPerDay,
                ViewportWidth = currentViewport.ViewportWidth,
                ViewportHeight = currentViewport.ViewportHeight,
                ScrollPosition = currentViewport.ScrollPosition
            };

            newViewport.Pan(pixelOffset);

            return Task.FromResult(newViewport);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error panning viewport");
            throw;
        }
    }

    /// <summary>
    /// Gets the earliest event date.
    /// </summary>
    public async Task<DateTime> GetEarliestEventDateAsync()
    {
        try
        {
            var events = await _eventRepository.GetAllAsync();
            return events.Any() ? events.Min(e => e.StartDate) : DateTime.MinValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting earliest event date");
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Gets the latest event date.
    /// </summary>
    public async Task<DateTime> GetLatestEventDateAsync()
    {
        try
        {
            var events = await _eventRepository.GetAllAsync();
            return events.Any() ? events.Max(e => e.EndDate ?? e.StartDate) : DateTime.MaxValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest event date");
            return DateTime.MaxValue;
        }
    }

    /// <summary>
    /// Calculates pixel positions for events based on viewport.
    /// Events are displayed as map pins pointing down to the timeline axis.
    /// Uses the TimelineCoordinateConverter for Premiere-style coordinate transformations.
    /// </summary>
    public void CalculateEventPositions(IEnumerable<TimelineEventDto> events, TimelineViewport viewport)
    {
        // Pin dimensions
        const double pinWidth = 30.0;
        const double pinHeight = 40.0;
        const double timelineAxisY = 50.0; // Y position of the timeline axis
        const double pinSpacing = 5.0; // Spacing between stacked pins

        // Use the coordinate converter for date-to-screen transformations
        var converter = TimelineCoordinateConverter.FromViewport(viewport);

        foreach (var evt in events.OrderBy(e => e.StartDate))
        {
            // Calculate horizontal position using coordinate converter - center the pin on the date
            var datePixelX = converter.DateToScreen(evt.StartDate);
            evt.PixelX = datePixelX - (pinWidth / 2); // Center the pin on the date

            // Fixed pin dimensions
            evt.Width = pinWidth;
            evt.Height = pinHeight;

            // Check visibility using coordinate converter
            evt.IsVisible = converter.IsRangeVisible(evt.StartDate, evt.EndDate);
        }

        // Use tracks to stack overlapping pins above each other
        var tracks = new List<List<TimelineEventDto>>();

        foreach (var evt in events.OrderBy(e => e.StartDate))
        {
            // Find a track for this event
            var trackIndex = FindAvailableTrack(tracks, evt);
            // Position so pin tip touches the timeline axis, stacking upward for overlapping events
            evt.PixelY = timelineAxisY - pinHeight - (trackIndex * (pinHeight + pinSpacing));
        }
    }

    /// <summary>
    /// Calculates pixel positions for eras based on viewport.
    /// Uses the TimelineCoordinateConverter for Premiere-style coordinate transformations.
    /// </summary>
    public void CalculateEraPositions(IEnumerable<TimelineEraDto> eras, TimelineViewport viewport)
    {
        var converter = TimelineCoordinateConverter.FromViewport(viewport);

        foreach (var era in eras)
        {
            era.PixelX = converter.DateToScreen(era.StartDate);

            var endDate = era.EndDate ?? viewport.EndDate;
            var endPixel = converter.DateToScreen(endDate);

            era.Width = Math.Max(0, endPixel - era.PixelX);
            era.IsVisible = converter.IsRangeVisible(era.StartDate, era.EndDate);
        }
    }

    private int FindAvailableTrack(List<List<TimelineEventDto>> tracks, TimelineEventDto newEvent)
    {
        for (int i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            var hasOverlap = track.Any(e =>
            {
                var eEnd = e.PixelX + e.Width;
                var newEnd = newEvent.PixelX + newEvent.Width;
                return !(newEvent.PixelX >= eEnd || newEnd <= e.PixelX);
            });

            if (!hasOverlap)
            {
                track.Add(newEvent);
                return i;
            }
        }

        // Create new track
        var newTrack = new List<TimelineEventDto> { newEvent };
        tracks.Add(newTrack);
        return tracks.Count - 1;
    }
}
