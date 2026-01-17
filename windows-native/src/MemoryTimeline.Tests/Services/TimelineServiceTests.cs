using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.Services;

public class TimelineServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly IEventRepository _eventRepository;
    private readonly IEraRepository _eraRepository;
    private readonly ITimelineService _timelineService;
    private readonly Mock<ILogger<TimelineService>> _loggerMock;

    public TimelineServiceTests()
    {
        // Create in-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _eventRepository = new EventRepository(_context);
        _eraRepository = new EraRepository(_context);
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
        var actualDaysOffset = (originalStartDate - pannedViewport.StartDate).VisibleDays;
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

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
