using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.Services;

public class EventServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly IEventRepository _repository;
    private readonly IEventService _eventService;
    private readonly Mock<ILogger<EventService>> _loggerMock;

    public EventServiceTests()
    {
        // Create in-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _repository = new EventRepository(_context);
        _loggerMock = new Mock<ILogger<EventService>>();
        _eventService = new EventService(_repository, _context, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateEventAsync_ValidEvent_ReturnsEventWithId()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "Test Event",
            StartDate = DateTime.UtcNow,
            Description = "Test Description",
            Category = EventCategory.Milestone
        };

        // Act
        var result = await _eventService.CreateEventAsync(newEvent);

        // Assert
        result.Should().NotBeNull();
        result.EventId.Should().NotBeNullOrEmpty();
        result.Title.Should().Be("Test Event");
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateEventAsync_NullTitle_ThrowsArgumentException()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "",
            StartDate = DateTime.UtcNow
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _eventService.CreateEventAsync(newEvent));
    }

    [Fact]
    public async Task GetEventByIdAsync_ExistingEvent_ReturnsEvent()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "Find Me",
            StartDate = DateTime.UtcNow,
            Category = EventCategory.Work
        };
        var created = await _eventService.CreateEventAsync(newEvent);

        // Act
        var result = await _eventService.GetEventByIdAsync(created.EventId);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Find Me");
    }

    [Fact]
    public async Task GetEventByIdAsync_NonExistentEvent_ReturnsNull()
    {
        // Act
        var result = await _eventService.GetEventByIdAsync("non-existent-id");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateEventAsync_ExistingEvent_UpdatesSuccessfully()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "Original Title",
            StartDate = DateTime.UtcNow,
            Category = EventCategory.Education
        };
        var created = await _eventService.CreateEventAsync(newEvent);

        // Act
        created.Title = "Updated Title";
        created.Description = "New Description";
        var updated = await _eventService.UpdateEventAsync(created);

        // Assert
        updated.Title.Should().Be("Updated Title");
        updated.Description.Should().Be("New Description");

        var fetched = await _eventService.GetEventByIdAsync(created.EventId);
        fetched!.Title.Should().Be("Updated Title");
    }

    [Fact]
    public async Task DeleteEventAsync_ExistingEvent_DeletesSuccessfully()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "Delete Me",
            StartDate = DateTime.UtcNow
        };
        var created = await _eventService.CreateEventAsync(newEvent);

        // Act
        await _eventService.DeleteEventAsync(created.EventId);

        // Assert
        var result = await _eventService.GetEventByIdAsync(created.EventId);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetEventsByDateRangeAsync_MultipleEvents_ReturnsCorrectEvents()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 12, 31);

        var event1 = new Event { Title = "Event 2024", StartDate = new DateTime(2024, 6, 15) };
        var event2 = new Event { Title = "Event 2023", StartDate = new DateTime(2023, 6, 15) };
        var event3 = new Event { Title = "Event 2025", StartDate = new DateTime(2025, 6, 15) };

        await _eventService.CreateEventAsync(event1);
        await _eventService.CreateEventAsync(event2);
        await _eventService.CreateEventAsync(event3);

        // Act
        var results = await _eventService.GetEventsByDateRangeAsync(startDate, endDate);

        // Assert
        results.Should().HaveCount(1);
        results.First().Title.Should().Be("Event 2024");
    }

    [Fact]
    public async Task GetTotalEventCountAsync_AfterCreatingEvents_ReturnsCorrectCount()
    {
        // Arrange
        await _eventService.CreateEventAsync(new Event { Title = "Event 1", StartDate = DateTime.UtcNow });
        await _eventService.CreateEventAsync(new Event { Title = "Event 2", StartDate = DateTime.UtcNow });
        await _eventService.CreateEventAsync(new Event { Title = "Event 3", StartDate = DateTime.UtcNow });

        // Act
        var count = await _eventService.GetTotalEventCountAsync();

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task SearchEventsAsync_WithMatchingTitle_ReturnsEvents()
    {
        // Arrange
        await _eventService.CreateEventAsync(new Event { Title = "Important Meeting", StartDate = DateTime.UtcNow });
        await _eventService.CreateEventAsync(new Event { Title = "Regular Checkup", StartDate = DateTime.UtcNow });
        await _eventService.CreateEventAsync(new Event { Title = "Another Important Call", StartDate = DateTime.UtcNow });

        // Act
        var results = await _eventService.SearchEventsAsync("Important");

        // Assert
        results.Should().HaveCount(2);
        results.All(e => e.Title.Contains("Important")).Should().BeTrue();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
