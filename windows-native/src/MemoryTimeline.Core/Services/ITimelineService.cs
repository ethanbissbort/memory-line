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
            var pixelsPerDay = ZoomConfig.GetPixelsPerDay(zoomLevel);
            var timeSpan = ZoomConfig.GetDefaultTimeSpan(zoomLevel);

            // If no events, use current date as center
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

            var startDate = centerDate.AddDays(-timeSpan.TotalDays / 2);
            var endDate = centerDate.AddDays(timeSpan.TotalDays / 2);

            return new TimelineViewport
            {
                StartDate = startDate,
                EndDate = endDate,
                ZoomLevel = zoomLevel,
                PixelsPerDay = pixelsPerDay,
                ViewportWidth = viewportWidth,
                ViewportHeight = viewportHeight
            };
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
            var newZoomLevel = currentViewport.ZoomLevel switch
            {
                ZoomLevel.Year => ZoomLevel.Month,
                ZoomLevel.Month => ZoomLevel.Week,
                ZoomLevel.Week => ZoomLevel.Day,
                ZoomLevel.Day => ZoomLevel.Day, // Max zoom
                _ => ZoomLevel.Month
            };

            var center = centerDate ?? currentViewport.StartDate.AddDays(currentViewport.TotalDays / 2.0);
            return await CreateViewportAsync(newZoomLevel, center, currentViewport.ViewportWidth, currentViewport.ViewportHeight);
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
            var newZoomLevel = currentViewport.ZoomLevel switch
            {
                ZoomLevel.Day => ZoomLevel.Week,
                ZoomLevel.Week => ZoomLevel.Month,
                ZoomLevel.Month => ZoomLevel.Year,
                ZoomLevel.Year => ZoomLevel.Year, // Min zoom
                _ => ZoomLevel.Month
            };

            var center = centerDate ?? currentViewport.StartDate.AddDays(currentViewport.TotalDays / 2.0);
            return await CreateViewportAsync(newZoomLevel, center, currentViewport.ViewportWidth, currentViewport.ViewportHeight);
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
    public async Task<TimelineViewport> PanAsync(TimelineViewport currentViewport, double pixelOffset)
    {
        try
        {
            var daysOffset = pixelOffset / currentViewport.PixelsPerDay;
            var newStartDate = currentViewport.StartDate.AddDays(-daysOffset);
            var newEndDate = currentViewport.EndDate.AddDays(-daysOffset);

            return new TimelineViewport
            {
                StartDate = newStartDate,
                EndDate = newEndDate,
                ZoomLevel = currentViewport.ZoomLevel,
                PixelsPerDay = currentViewport.PixelsPerDay,
                ViewportWidth = currentViewport.ViewportWidth,
                ViewportHeight = currentViewport.ViewportHeight
            };
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
    /// </summary>
    public void CalculateEventPositions(IEnumerable<TimelineEventDto> events, TimelineViewport viewport)
    {
        const double eventHeight = 40;
        const double eventSpacing = 10;
        var tracks = new List<List<TimelineEventDto>>();

        foreach (var evt in events.OrderBy(e => e.StartDate))
        {
            evt.PixelX = viewport.DateToPixel(evt.StartDate);
            evt.IsVisible = viewport.IsDateVisible(evt.StartDate);

            if (evt.IsDurationEvent && evt.EndDate.HasValue)
            {
                var endPixel = viewport.DateToPixel(evt.EndDate.Value);
                evt.Width = Math.Max(20, endPixel - evt.PixelX);
            }
            else
            {
                evt.Width = 20; // Point event width
            }

            evt.Height = eventHeight;

            // Find a track for this event (simple algorithm)
            var track = FindAvailableTrack(tracks, evt);
            evt.PixelY = track * (eventHeight + eventSpacing) + 100; // Offset from top
        }
    }

    /// <summary>
    /// Calculates pixel positions for eras based on viewport.
    /// </summary>
    public void CalculateEraPositions(IEnumerable<TimelineEraDto> eras, TimelineViewport viewport)
    {
        foreach (var era in eras)
        {
            era.PixelX = viewport.DateToPixel(era.StartDate);
            era.IsVisible = true;

            var endDate = era.EndDate ?? viewport.EndDate;
            var endPixel = viewport.DateToPixel(endDate);
            era.Width = Math.Max(0, endPixel - era.PixelX);
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
