using FluentAssertions;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.Services;

public class TimelineServiceTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly IEventRepository _eventRepository;
    private readonly IEraRepository _eraRepository;
    private readonly ITimelineService _timelineService;
    private readonly Mock<ILogger<TimelineService>> _loggerMock;

    public TimelineServiceTests()
    {
        // Factory over a uniquely named in-memory database; repositories create a
        // fresh context per operation against the same store.
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _eventRepository = new EventRepository(_contextFactory);
        _eraRepository = new EraRepository(_contextFactory);
        _loggerMock = new Mock<ILogger<TimelineService>>();
        _timelineService = new TimelineService(_eventRepository, _eraRepository, _loggerMock.Object);
    }

    #region Viewport Creation Tests

    [Fact]
    public async Task CreateViewportAsync_ValidParameters_ReturnsViewportWithCorrectDimensions()
    {
        // Arrange
        var zoomLevel = ZoomLevel.Month;
        var centerDate = new DateTime(2024, 6, 15);
        var width = 1920;
        var height = 1080;

        // Act
        var viewport = await _timelineService.CreateViewportAsync(zoomLevel, centerDate, width, height);

        // Assert
        viewport.Should().NotBeNull();
        viewport.ZoomLevel.Should().Be(ZoomLevel.Month);
        viewport.ViewportWidth.Should().Be(1920);
        viewport.ViewportHeight.Should().Be(1080);
        viewport.PixelsPerDay.Should().Be(TimelineScale.GetPixelsPerDay(ZoomLevel.Month));
    }

    [Fact]
    public async Task CreateViewportAsync_NoEventsInDatabase_UsesCenterDate()
    {
        // Arrange
        var centerDate = new DateTime(2024, 6, 15);

        // Act
        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, centerDate, 1920, 1080);

        // Assert
        // Viewport should be centered around the provided date
        var midpoint = viewport.StartDate.AddDays(viewport.VisibleDays / 2.0);
        midpoint.Should().BeCloseTo(centerDate, TimeSpan.FromDays(1));
    }

    [Theory]
    [InlineData(ZoomLevel.Year, 0.1)]
    [InlineData(ZoomLevel.Month, 3.0)]
    [InlineData(ZoomLevel.Week, 50.0)]
    [InlineData(ZoomLevel.Day, 800.0)]
    public async Task CreateViewportAsync_DifferentZoomLevels_HasCorrectPixelsPerDay(
        ZoomLevel zoomLevel, double expectedPixelsPerDay)
    {
        // Act
        var viewport = await _timelineService.CreateViewportAsync(
            zoomLevel, DateTime.Now, 1920, 1080);

        // Assert
        viewport.PixelsPerDay.Should().Be(expectedPixelsPerDay);
    }

    #endregion

    #region Zoom Tests

    [Fact]
    public async Task ZoomInAsync_FromMonth_ZoomsToWeek()
    {
        // Arrange
        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, DateTime.Now, 1920, 1080);

        // Act
        var zoomedViewport = await _timelineService.ZoomInAsync(viewport);

        // Assert
        zoomedViewport.ZoomLevel.Should().Be(ZoomLevel.Week);
        zoomedViewport.PixelsPerDay.Should().Be(TimelineScale.GetPixelsPerDay(ZoomLevel.Week));
    }

    [Fact]
    public async Task ZoomInAsync_FromDay_StaysAtDay()
    {
        // Arrange - already at max zoom
        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Day, DateTime.Now, 1920, 1080);

        // Act
        var zoomedViewport = await _timelineService.ZoomInAsync(viewport);

        // Assert - should stay at Day level
        zoomedViewport.ZoomLevel.Should().Be(ZoomLevel.Day);
    }

    [Fact]
    public async Task ZoomOutAsync_FromMonth_ZoomsToYear()
    {
        // Arrange
        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, DateTime.Now, 1920, 1080);

        // Act
        var zoomedViewport = await _timelineService.ZoomOutAsync(viewport);

        // Assert
        zoomedViewport.ZoomLevel.Should().Be(ZoomLevel.Year);
        zoomedViewport.PixelsPerDay.Should().Be(TimelineScale.GetPixelsPerDay(ZoomLevel.Year));
    }

    [Fact]
    public async Task ZoomOutAsync_FromYear_StaysAtYear()
    {
        // Arrange - already at min zoom
        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Year, DateTime.Now, 1920, 1080);

        // Act
        var zoomedViewport = await _timelineService.ZoomOutAsync(viewport);

        // Assert - should stay at Year level
        zoomedViewport.ZoomLevel.Should().Be(ZoomLevel.Year);
    }

    [Fact]
    public async Task ZoomInAsync_WithCustomCenterDate_CentersOnProvidedDate()
    {
        // Arrange
        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, DateTime.Now, 1920, 1080);
        var centerDate = new DateTime(2024, 12, 25);

        // Act
        var zoomedViewport = await _timelineService.ZoomInAsync(viewport, centerDate);

        // Assert
        var midpoint = zoomedViewport.StartDate.AddDays(zoomedViewport.VisibleDays / 2.0);
        midpoint.Should().BeCloseTo(centerDate, TimeSpan.FromDays(1));
    }

    #endregion

    #region Pan Tests

    [Fact]
    public async Task PanAsync_PositiveOffset_MovesViewportForward()
    {
        // Arrange
        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, new DateTime(2024, 6, 15), 1920, 1080);
        var originalStartDate = viewport.StartDate;
        var pixelOffset = 100.0; // Pan 100 pixels to the right

        // Act
        var pannedViewport = await _timelineService.PanAsync(viewport, pixelOffset);

        // Assert
        pannedViewport.StartDate.Should().BeBefore(originalStartDate); // Moving right means earlier dates
        var expectedDaysOffset = pixelOffset / viewport.PixelsPerDay;
        var actualDaysOffset = (originalStartDate - pannedViewport.StartDate).TotalDays;
        actualDaysOffset.Should().BeApproximately(expectedDaysOffset, 0.1);
    }

    [Fact]
    public async Task PanAsync_NegativeOffset_MovesViewportBackward()
    {
        // Arrange
        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, new DateTime(2024, 6, 15), 1920, 1080);
        var originalStartDate = viewport.StartDate;
        var pixelOffset = -100.0; // Pan 100 pixels to the left

        // Act
        var pannedViewport = await _timelineService.PanAsync(viewport, pixelOffset);

        // Assert
        pannedViewport.StartDate.Should().BeAfter(originalStartDate); // Moving left means later dates
    }

    [Fact]
    public async Task PanAsync_PreservesZoomLevel()
    {
        // Arrange
        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Week, DateTime.Now, 1920, 1080);

        // Act
        var pannedViewport = await _timelineService.PanAsync(viewport, 50.0);

        // Assert
        pannedViewport.ZoomLevel.Should().Be(ZoomLevel.Week);
        pannedViewport.PixelsPerDay.Should().Be(viewport.PixelsPerDay);
    }

    #endregion

    #region Event Loading Tests

    [Fact]
    public async Task GetEventsForViewportAsync_NoEvents_ReturnsEmptyCollection()
    {
        // Arrange
        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, DateTime.Now, 1920, 1080);

        // Act
        var events = await _timelineService.GetEventsForViewportAsync(viewport);

        // Assert
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsForViewportAsync_WithEventsInRange_ReturnsEvents()
    {
        // Arrange
        var centerDate = new DateTime(2024, 6, 15);
        var event1 = new Event { Title = "Event 1", StartDate = new DateTime(2024, 6, 10) };
        var event2 = new Event { Title = "Event 2", StartDate = new DateTime(2024, 6, 20) };
        var event3 = new Event { Title = "Event 3", StartDate = new DateTime(2025, 1, 1) }; // Out of range

        await _eventRepository.AddAsync(event1);
        await _eventRepository.AddAsync(event2);
        await _eventRepository.AddAsync(event3);

        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, centerDate, 1920, 1080);

        // Act
        var events = await _timelineService.GetEventsForViewportAsync(viewport);

        // Assert
        events.Should().HaveCountGreaterOrEqualTo(2);
        events.Should().Contain(e => e.Title == "Event 1");
        events.Should().Contain(e => e.Title == "Event 2");
    }

    [Fact]
    public async Task GetEventsForViewportAsync_CalculatesPixelPositions()
    {
        // Arrange
        var centerDate = new DateTime(2024, 6, 15);
        var event1 = new Event { Title = "Event 1", StartDate = centerDate };
        await _eventRepository.AddAsync(event1);

        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, centerDate, 1920, 1080);

        // Act
        var events = await _timelineService.GetEventsForViewportAsync(viewport);

        // Assert
        var eventDto = events.First();
        eventDto.PixelX.Should().BeGreaterOrEqualTo(0);
        eventDto.PixelY.Should().BeGreaterOrEqualTo(0);
        eventDto.Width.Should().BeGreaterThan(0);
        eventDto.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetEventsForViewportAsync_DurationEvent_HasCorrectWidth()
    {
        // Arrange
        var centerDate = new DateTime(2024, 6, 15);
        var event1 = new Event
        {
            Title = "Duration Event",
            StartDate = new DateTime(2024, 6, 10),
            EndDate = new DateTime(2024, 6, 20) // 10-day duration
        };
        await _eventRepository.AddAsync(event1);

        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, centerDate, 1920, 1080);

        // Act
        var events = await _timelineService.GetEventsForViewportAsync(viewport);

        // Assert
        var eventDto = events.First();
        var expectedWidth = 10 * viewport.PixelsPerDay; // 10 days
        eventDto.Width.Should().BeApproximately(expectedWidth, 1.0);
    }

    #endregion

    #region Era Loading Tests

    [Fact]
    public async Task GetErasForViewportAsync_NoEras_ReturnsEmptyCollection()
    {
        // Arrange
        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, DateTime.Now, 1920, 1080);

        // Act
        var eras = await _timelineService.GetErasForViewportAsync(viewport);

        // Assert
        eras.Should().BeEmpty();
    }

    [Fact]
    public async Task GetErasForViewportAsync_WithErasInRange_ReturnsEras()
    {
        // Arrange
        var centerDate = new DateTime(2024, 6, 15);
        var era1 = new Era
        {
            Name = "Era 1",
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            Color = "#FF0000"
        };
        await _eraRepository.AddAsync(era1);

        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, centerDate, 1920, 1080);

        // Act
        var eras = await _timelineService.GetErasForViewportAsync(viewport);

        // Assert
        eras.Should().HaveCount(1);
        eras.First().Name.Should().Be("Era 1");
    }

    [Fact]
    public async Task GetErasForViewportAsync_OngoingEra_IsIncluded()
    {
        // Arrange
        var centerDate = new DateTime(2024, 6, 15);
        var ongoingEra = new Era
        {
            Name = "Ongoing Era",
            StartDate = new DateTime(2020, 1, 1),
            EndDate = null, // Ongoing
            Color = "#00FF00"
        };
        await _eraRepository.AddAsync(ongoingEra);

        var viewport = await _timelineService.CreateViewportAsync(
            ZoomLevel.Month, centerDate, 1920, 1080);

        // Act
        var eras = await _timelineService.GetErasForViewportAsync(viewport);

        // Assert
        eras.Should().HaveCount(1);
        eras.First().Name.Should().Be("Ongoing Era");
    }

    #endregion

    #region Date Range Tests

    [Fact]
    public async Task GetEarliestEventDateAsync_NoEvents_ReturnsMinValue()
    {
        // Act
        var earliestDate = await _timelineService.GetEarliestEventDateAsync();

        // Assert
        earliestDate.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public async Task GetEarliestEventDateAsync_WithEvents_ReturnsEarliestDate()
    {
        // Arrange
        await _eventRepository.AddAsync(new Event { Title = "Event 1", StartDate = new DateTime(2024, 6, 15) });
        await _eventRepository.AddAsync(new Event { Title = "Event 2", StartDate = new DateTime(2023, 1, 1) });
        await _eventRepository.AddAsync(new Event { Title = "Event 3", StartDate = new DateTime(2025, 12, 31) });

        // Act
        var earliestDate = await _timelineService.GetEarliestEventDateAsync();

        // Assert
        earliestDate.Should().Be(new DateTime(2023, 1, 1));
    }

    [Fact]
    public async Task GetLatestEventDateAsync_NoEvents_ReturnsMaxValue()
    {
        // Act
        var latestDate = await _timelineService.GetLatestEventDateAsync();

        // Assert
        latestDate.Should().Be(DateTime.MaxValue);
    }

    [Fact]
    public async Task GetLatestEventDateAsync_WithEvents_ReturnsLatestDate()
    {
        // Arrange
        await _eventRepository.AddAsync(new Event { Title = "Event 1", StartDate = new DateTime(2024, 6, 15) });
        await _eventRepository.AddAsync(new Event { Title = "Event 2", StartDate = new DateTime(2023, 1, 1), EndDate = new DateTime(2025, 12, 31) });
        await _eventRepository.AddAsync(new Event { Title = "Event 3", StartDate = new DateTime(2022, 1, 1) });

        // Act
        var latestDate = await _timelineService.GetLatestEventDateAsync();

        // Assert
        latestDate.Should().Be(new DateTime(2025, 12, 31));
    }

    #endregion

    #region Event Position Calculation Tests

    [Fact]
    public void CalculateEventPositions_NonOverlappingEvents_AssignedToSameTrack()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            ZoomLevel = ZoomLevel.Month,
            PixelsPerDay = TimelineScale.GetPixelsPerDay(ZoomLevel.Month),
            ViewportWidth = 1920,
            ViewportHeight = 1080
        };

        var events = new List<Core.DTOs.TimelineEventDto>
        {
            new() { EventId = "1", Title = "Event 1", StartDate = new DateTime(2024, 1, 1) },
            new() { EventId = "2", Title = "Event 2", StartDate = new DateTime(2024, 6, 1) }
        };

        // Act
        _timelineService.CalculateEventPositions(events, viewport);

        // Assert
        events[0].PixelY.Should().Be(events[1].PixelY); // Same track
    }

    [Fact]
    public void CalculateEventPositions_OverlappingEvents_AssignedToDifferentTracks()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            ZoomLevel = ZoomLevel.Month,
            PixelsPerDay = TimelineScale.GetPixelsPerDay(ZoomLevel.Month),
            ViewportWidth = 1920,
            ViewportHeight = 1080
        };

        var events = new List<Core.DTOs.TimelineEventDto>
        {
            new() { EventId = "1", Title = "Event 1", StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 1, 10) },
            new() { EventId = "2", Title = "Event 2", StartDate = new DateTime(2024, 1, 5), EndDate = new DateTime(2024, 1, 15) }
        };

        // Act
        _timelineService.CalculateEventPositions(events, viewport);

        // Assert
        events[0].PixelY.Should().NotBe(events[1].PixelY); // Different tracks
    }

    #endregion

    #region Span Rendering Tests

    /// <summary>
    /// Builds a viewport at a discrete zoom level anchored at
    /// <paramref name="startDate"/> (PixelsPerDay matches the zoom exactly,
    /// like every viewport created through TimelineService).
    /// </summary>
    private static TimelineViewport CreateTestViewport(
        ZoomLevel zoom, DateTime startDate, double width = 1000, double height = 800)
    {
        var pixelsPerDay = TimelineScale.GetPixelsPerDay(zoom);
        var visibleDays = width / pixelsPerDay;
        return new TimelineViewport
        {
            StartDate = startDate,
            EndDate = startDate.AddDays(visibleDays),
            CenterDate = startDate.AddDays(visibleDays / 2),
            ZoomLevel = zoom,
            PixelsPerDay = pixelsPerDay,
            ViewportWidth = width,
            ViewportHeight = height
        };
    }

    [Fact]
    public void CalculateEventPositions_ThreeYearEventAtYearZoom_RendersProportionalSpan()
    {
        // Arrange - a 3-year job must not render like a one-hour dinner
        var viewport = CreateTestViewport(ZoomLevel.Year, new DateTime(2019, 1, 1));
        var start = new DateTime(2020, 1, 1);
        var end = new DateTime(2023, 1, 1);
        var evt = new Core.DTOs.TimelineEventDto { EventId = "1", Title = "Job", StartDate = start, EndDate = end };

        // Act
        _timelineService.CalculateEventPositions(new[] { evt }, viewport);

        // Assert - span, wider than the 30px pin, width exactly per GetEventWidth,
        // left edge anchored on the start date (not centered).
        evt.RenderMode.Should().Be(Core.DTOs.EventRenderMode.Span);
        evt.Width.Should().BeGreaterThan(30.0);
        evt.Width.Should().Be(TimelineScale.GetEventWidth(start, end, ZoomLevel.Year));
        evt.PixelX.Should().Be((start - viewport.StartDate).TotalDays * viewport.PixelsPerDay);
        evt.Height.Should().Be(24.0);
    }

    [Fact]
    public void CalculateEventPositions_ThreeYearEventAtDayZoom_WidthMatchesGetEventWidthAndClipsRight()
    {
        // Arrange - at Day zoom the same event is enormously wide; the width
        // still follows GetEventWidth (its minimum-clamp contract) and the
        // overhang flags report that the span runs past the right edge.
        var viewport = CreateTestViewport(ZoomLevel.Day, new DateTime(2020, 1, 1));
        var start = new DateTime(2020, 1, 1);
        var end = new DateTime(2023, 1, 1);
        var evt = new Core.DTOs.TimelineEventDto { EventId = "1", Title = "Job", StartDate = start, EndDate = end };

        // Act
        _timelineService.CalculateEventPositions(new[] { evt }, viewport);

        // Assert - the TRUE width still follows GetEventWidth (track-overlap
        // math), while the RENDERED geometry and the overhang (measured from
        // the clamped render edge) cap at the span render margin.
        evt.RenderMode.Should().Be(Core.DTOs.EventRenderMode.Span);
        evt.Width.Should().Be(TimelineScale.GetEventWidth(start, end, ZoomLevel.Day));
        evt.ClipsViewportLeft.Should().BeFalse();
        evt.ClipsViewportRight.Should().BeTrue();
        evt.SpanRightOverhang.Should().Be(TimelineService.SpanRenderMargin);
        evt.RenderX.Should().Be(evt.PixelX);
        evt.RenderWidth.Should().Be(
            viewport.ViewportWidth + TimelineService.SpanRenderMargin - evt.PixelX);
    }

    [Fact]
    public void CalculateEventPositions_TwentyYearEventAtDayZoom_RenderGeometryStaysClamped()
    {
        // Arrange - a 20-year event at Day zoom (800 px/day) is ~5.8M true
        // pixels wide; the rendered element must stay within the viewport
        // plus the render margin on each side.
        var viewport = CreateTestViewport(ZoomLevel.Day, new DateTime(2010, 6, 1));
        var start = new DateTime(2000, 1, 1);
        var end = new DateTime(2020, 1, 1);
        var evt = new Core.DTOs.TimelineEventDto { EventId = "1", Title = "Era-like", StartDate = start, EndDate = end };

        // Act
        _timelineService.CalculateEventPositions(new[] { evt }, viewport);

        // Assert - true width for overlap math, clamped geometry for rendering.
        evt.RenderMode.Should().Be(Core.DTOs.EventRenderMode.Span);
        evt.Width.Should().Be(TimelineScale.GetEventWidth(start, end, ZoomLevel.Day));
        evt.RenderX.Should().Be(-TimelineService.SpanRenderMargin);
        evt.RenderWidth.Should().Be(viewport.ViewportWidth + 2 * TimelineService.SpanRenderMargin);
        evt.ClipsViewportLeft.Should().BeTrue();
        evt.ClipsViewportRight.Should().BeTrue();
        evt.SpanLeftOverhang.Should().Be(TimelineService.SpanRenderMargin);
        evt.SpanRightOverhang.Should().Be(TimelineService.SpanRenderMargin);
    }

    [Theory]
    [InlineData(ZoomLevel.Year)]
    [InlineData(ZoomLevel.Month)]
    [InlineData(ZoomLevel.Week)]
    [InlineData(ZoomLevel.Day)]
    public void CalculateEventPositions_ZeroDurationEvent_AlwaysRendersPin(ZoomLevel zoom)
    {
        // Arrange - no EndDate: a point-in-time memory stays a 30px pin
        // centered on its date at EVERY zoom level.
        var viewportStart = new DateTime(2024, 6, 1);
        var viewport = CreateTestViewport(zoom, viewportStart);
        var date = viewportStart.AddDays(0.5);
        var evt = new Core.DTOs.TimelineEventDto { EventId = "1", Title = "Dinner", StartDate = date };

        // Act
        _timelineService.CalculateEventPositions(new[] { evt }, viewport);

        // Assert
        evt.RenderMode.Should().Be(Core.DTOs.EventRenderMode.Pin);
        evt.Width.Should().Be(30.0);
        evt.Height.Should().Be(40.0);
        evt.PixelX.Should().Be((date - viewport.StartDate).TotalDays * viewport.PixelsPerDay - 15.0);
    }

    [Fact]
    public void CalculateEventPositions_WidthExactlyPinWidth_RendersPin()
    {
        // Arrange - RenderMode boundary: a duration whose proportional width is
        // EXACTLY the 30px pin width stays a pin ("exceeds" means strictly
        // greater); one day longer tips it into a span.
        var viewport = CreateTestViewport(ZoomLevel.Month, new DateTime(2024, 1, 1)); // 3 px/day
        var atBoundary = new Core.DTOs.TimelineEventDto
        {
            EventId = "1",
            Title = "Ten days",
            StartDate = new DateTime(2024, 2, 1),
            EndDate = new DateTime(2024, 2, 11) // 10 days * 3 px = 30.0
        };
        var overBoundary = new Core.DTOs.TimelineEventDto
        {
            EventId = "2",
            Title = "Eleven days",
            StartDate = new DateTime(2024, 4, 1),
            EndDate = new DateTime(2024, 4, 12) // 11 days * 3 px = 33.0
        };

        // Act
        _timelineService.CalculateEventPositions(new[] { atBoundary, overBoundary }, viewport);

        // Assert
        atBoundary.RenderMode.Should().Be(Core.DTOs.EventRenderMode.Pin);
        atBoundary.Width.Should().Be(30.0);
        overBoundary.RenderMode.Should().Be(Core.DTOs.EventRenderMode.Span);
        overBoundary.Width.Should().Be(33.0);
    }

    [Fact]
    public void CalculateEventPositions_MixedPinAndSpanWidths_NoHorizontalOverlapWithinTrack()
    {
        // Arrange - a wide span, a narrower span, and pins landing on top of
        // them; track stacking must honor each event's REAL width.
        var viewport = CreateTestViewport(ZoomLevel.Month, new DateTime(2024, 1, 1));
        var events = new List<Core.DTOs.TimelineEventDto>
        {
            new() { EventId = "wide-span", Title = "A", StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 3, 1) },
            new() { EventId = "pin-1", Title = "B", StartDate = new DateTime(2024, 1, 15) },
            new() { EventId = "narrow-span", Title = "C", StartDate = new DateTime(2024, 2, 1), EndDate = new DateTime(2024, 2, 20) },
            new() { EventId = "pin-2", Title = "D", StartDate = new DateTime(2024, 1, 16) },
            new() { EventId = "far-pin", Title = "E", StartDate = new DateTime(2024, 6, 1) }
        };

        // Act
        _timelineService.CalculateEventPositions(events, viewport);

        // Assert - invariant: no two events sharing a track (same PixelY)
        // overlap horizontally over their [PixelX, PixelX + Width) extents.
        foreach (var track in events.GroupBy(e => e.PixelY))
        {
            var items = track.ToList();
            for (var i = 0; i < items.Count; i++)
            {
                for (var j = i + 1; j < items.Count; j++)
                {
                    var a = items[i];
                    var b = items[j];
                    var overlaps = !(a.PixelX >= b.PixelX + b.Width || a.PixelX + a.Width <= b.PixelX);
                    overlaps.Should().BeFalse(
                        $"events '{a.EventId}' and '{b.EventId}' share a track and must not overlap");
                }
            }
        }
    }

    #endregion

    #region Uncertainty Window Tests

    [Fact]
    public void CalculateEventPositions_YearPrecisionAtMonthZoom_WindowBoundsEqualGetWindow()
    {
        // Arrange - a Year-precision memory; the whole window fits the viewport
        var viewport = CreateTestViewport(ZoomLevel.Month, new DateTime(1998, 1, 1), width: 2000);
        var anchor = new DateTime(1998, 7, 15);
        var evt = new Core.DTOs.TimelineEventDto
        {
            EventId = "1",
            Title = "That year",
            StartDate = anchor,
            DatePrecision = DatePrecision.Year
        };

        // Act
        _timelineService.CalculateEventPositions(new[] { evt }, viewport);

        // Assert - bounds equal GetWindow exactly (converted at the viewport scale)
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(anchor, DatePrecision.Year);
        var converter = TimelineCoordinateConverter.FromViewport(viewport);
        evt.WindowStartX.Should().Be(converter.DateToScreen(earliest));
        evt.WindowEndX.Should().Be(converter.DateToScreen(latest));
        evt.HasUncertaintyWindow.Should().BeTrue();
    }

    [Fact]
    public void CalculateEventPositions_YearPrecisionAtYearZoom_WindowBoundsEqualGetWindow()
    {
        // Arrange - the same memory at Year zoom (second scale per the spec)
        var viewport = CreateTestViewport(ZoomLevel.Year, new DateTime(1990, 1, 1));
        var anchor = new DateTime(1998, 7, 15);
        var evt = new Core.DTOs.TimelineEventDto
        {
            EventId = "1",
            Title = "That year",
            StartDate = anchor,
            DatePrecision = DatePrecision.Year
        };

        // Act
        _timelineService.CalculateEventPositions(new[] { evt }, viewport);

        // Assert
        var (earliest, latest) = DatePrecisionExtensions.GetWindow(anchor, DatePrecision.Year);
        var converter = TimelineCoordinateConverter.FromViewport(viewport);
        evt.WindowStartX.Should().Be(converter.DateToScreen(earliest));
        evt.WindowEndX.Should().Be(converter.DateToScreen(latest));
        evt.WindowEndX.Should().BeGreaterThan(evt.WindowStartX);
    }

    [Fact]
    public void CalculateEventPositions_DecadeWindowLargerThanViewport_IsClampedToViewport()
    {
        // Arrange - a Decade window dwarfs a Month-zoom viewport; the exposed
        // bounds clamp to [0, ViewportWidth] so no megapixel-wide rect renders.
        var viewport = CreateTestViewport(ZoomLevel.Month, new DateTime(1995, 6, 1));
        var evt = new Core.DTOs.TimelineEventDto
        {
            EventId = "1",
            Title = "The nineties",
            StartDate = new DateTime(1995, 6, 15),
            DatePrecision = DatePrecision.Decade
        };

        // Act
        _timelineService.CalculateEventPositions(new[] { evt }, viewport);

        // Assert
        evt.WindowStartX.Should().Be(0.0);
        evt.WindowEndX.Should().Be(viewport.ViewportWidth);
        evt.HasUncertaintyWindow.Should().BeTrue();
    }

    [Fact]
    public void CalculateEventPositions_YearPrecisionAnchorOutsideViewport_BandStillShowsWhileWindowOverlaps()
    {
        // Arrange - a Week-zoom viewport over ~20 days of June 1998; the
        // Year-precision anchor (15 Jul 1998) sits OUTSIDE it, but the
        // precision window (all of 1998) covers the whole screen. The band
        // must render anyway - it must not pop off when only the anchor
        // leaves the viewport.
        var viewport = CreateTestViewport(ZoomLevel.Week, new DateTime(1998, 6, 1));
        var evt = new Core.DTOs.TimelineEventDto
        {
            EventId = "1",
            Title = "Sometime that year",
            StartDate = new DateTime(1998, 7, 15),
            DatePrecision = DatePrecision.Year
        };

        // Act
        _timelineService.CalculateEventPositions(new[] { evt }, viewport);

        // Assert - anchor invisible (pin hidden), window visible (band shown).
        evt.IsInViewport.Should().BeFalse();
        evt.IsVisible.Should().BeFalse();
        evt.IsWindowInViewport.Should().BeTrue();
        evt.WindowStartX.Should().Be(0.0);
        evt.WindowEndX.Should().Be(viewport.ViewportWidth);
        evt.HasUncertaintyWindow.Should().BeTrue();
        evt.ShowUncertaintyBand.Should().BeTrue();

        // A collapsed lane still hides the band.
        evt.IsLaneCollapsed = true;
        evt.ShowUncertaintyBand.Should().BeFalse();
    }

    [Fact]
    public void CalculateEventPositions_DayPrecision_HasNoUncertaintyWindow()
    {
        // Arrange - precision at Month or finer gets no underlay
        var viewport = CreateTestViewport(ZoomLevel.Month, new DateTime(2024, 1, 1));
        var evt = new Core.DTOs.TimelineEventDto
        {
            EventId = "1",
            Title = "Known day",
            StartDate = new DateTime(2024, 2, 1),
            DatePrecision = DatePrecision.Day
        };

        // Act
        _timelineService.CalculateEventPositions(new[] { evt }, viewport);

        // Assert
        evt.WindowStartX.Should().Be(0.0);
        evt.WindowEndX.Should().Be(0.0);
        evt.HasUncertaintyWindow.Should().BeFalse();
    }

    #endregion

    #region Auto Stacking Regression Tests

    [Fact]
    public void CalculateEventPositions_AutoStacking_MatchesLegacyPinLayout()
    {
        // Arrange - regression guard: Auto mode must keep the exact legacy
        // geometry (baseline near the bottom, tracks stacking upward by
        // pinHeight + spacing) so swimlanes change nothing until opted into.
        var viewport = CreateTestViewport(ZoomLevel.Month, new DateTime(2024, 1, 1), width: 1920, height: 1080);
        var first = new Core.DTOs.TimelineEventDto { EventId = "1", Title = "A", StartDate = new DateTime(2024, 2, 1) };
        var second = new Core.DTOs.TimelineEventDto { EventId = "2", Title = "B", StartDate = new DateTime(2024, 2, 1) };

        // Act
        _timelineService.CalculateEventPositions(new[] { first, second }, viewport);

        // Assert - legacy formula: eventsArea = max(200, height - 120),
        // baseline = eventsArea - 10, track0 = baseline - 40, step = 45.
        var eventsAreaHeight = Math.Max(200, viewport.ViewportHeight - 120);
        var baselineY = eventsAreaHeight - 10;
        first.PixelY.Should().Be(baselineY - 40.0);
        second.PixelY.Should().Be(baselineY - 40.0 - 45.0);
    }

    #endregion

    public void Dispose()
    {
        using var context = _contextFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }
}
