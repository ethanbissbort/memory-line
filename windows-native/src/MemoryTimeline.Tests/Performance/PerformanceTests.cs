using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using Moq;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace MemoryTimeline.Tests.Performance;

/// <summary>
/// Performance tests to ensure the application meets scalability requirements.
/// Target: Handle 5000+ events with good performance.
/// </summary>
public class PerformanceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly EventRepository _eventRepository;
    private readonly IEventService _eventService;
    private readonly string _databasePath;
    private readonly ITestOutputHelper _output;

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;

        // Create a real SQLite database for performance testing
        _databasePath = Path.Combine(Path.GetTempPath(), $"PerfTest_{Guid.NewGuid()}.db");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _eventRepository = new EventRepository(_context);
        var loggerMock = new Mock<ILogger<EventService>>();
        _eventService = new EventService(_eventRepository, _context, loggerMock.Object);
    }

    [Fact]
    public async Task LoadTest_Create5000Events_CompletesWithinReasonableTime()
    {
        // Arrange
        var stopwatch = Stopwatch.StartNew();
        var events = GenerateTestEvents(5000);

        // Act
        await _eventRepository.AddRangeAsync(events);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Created 5000 events in {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000); // Should complete within 10 seconds

        var count = await _eventRepository.CountAsync();
        count.Should().Be(5000);
    }

    [Fact]
    public async Task LoadTest_Query5000Events_ReturnsQuickly()
    {
        // Arrange - Create 5000 events
        var events = GenerateTestEvents(5000);
        await _eventRepository.AddRangeAsync(events);

        // Act - Query all events
        var stopwatch = Stopwatch.StartNew();
        var results = await _eventRepository.GetAllAsync();
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Queried 5000 events in {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000); // Should complete within 2 seconds

        results.Should().HaveCount(5000);
    }

    [Fact]
    public async Task LoadTest_PaginationWith5000Events_PerformsWell()
    {
        // Arrange
        var events = GenerateTestEvents(5000);
        await _eventRepository.AddRangeAsync(events);

        // Act - Query first page
        var stopwatch = Stopwatch.StartNew();
        var (pageEvents, totalCount) = await _eventRepository.GetPagedAsync(1, 50);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Paginated query (page 1, size 50) from 5000 events in {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500); // Should be very fast with pagination

        pageEvents.Should().HaveCount(50);
        totalCount.Should().Be(5000);
    }

    [Fact]
    public async Task LoadTest_DateRangeQueryWith5000Events_PerformsWell()
    {
        // Arrange
        var events = GenerateTestEvents(5000);
        await _eventRepository.AddRangeAsync(events);

        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 12, 31);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var results = await _eventRepository.GetByDateRangeAsync(startDate, endDate);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Date range query on 5000 events in {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000); // Should complete within 1 second

        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoadTest_SearchWith5000Events_ReturnsQuickly()
    {
        // Arrange
        var events = GenerateTestEvents(5000);
        // Add some events with "important" in title
        for (int i = 0; i < 100; i++)
        {
            events[i].Title = $"Important Event {i}";
        }
        await _eventRepository.AddRangeAsync(events);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var results = await _eventRepository.SearchAsync("Important");
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Search query on 5000 events in {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1500); // Should complete within 1.5 seconds

        results.Should().HaveCount(100);
    }

    [Fact]
    public async Task LoadTest_CategoryFilterWith5000Events_PerformsWell()
    {
        // Arrange
        var events = GenerateTestEvents(5000);
        await _eventRepository.AddRangeAsync(events);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var workEvents = await _eventRepository.GetByCategoryAsync(EventCategory.Work);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Category filter query on 5000 events in {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);

        workEvents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoadTest_UpdateOperationsOnLargeDataset_PerformReasonably()
    {
        // Arrange
        var events = GenerateTestEvents(1000);
        await _eventRepository.AddRangeAsync(events);

        var allEvents = (await _eventRepository.GetAllAsync()).ToList();

        // Act - Update 100 events
        var stopwatch = Stopwatch.StartNew();
        var eventsToUpdate = allEvents.Take(100).ToList();

        foreach (var evt in eventsToUpdate)
        {
            evt.Title = $"Updated - {evt.Title}";
            evt.Description = "Updated description";
        }

        foreach (var evt in eventsToUpdate)
        {
            await _eventRepository.UpdateAsync(evt);
        }

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Updated 100 events in {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000); // Should complete within 5 seconds

        var updated = await _eventRepository.GetByIdAsync(eventsToUpdate[0].EventId);
        updated!.Title.Should().StartWith("Updated -");
    }

    [Fact]
    public async Task MemoryTest_LargeDatasetInMemory_StaysWithinBounds()
    {
        // Arrange
        var events = GenerateTestEvents(5000);
        await _eventRepository.AddRangeAsync(events);

        // Act
        var beforeMemory = GC.GetTotalMemory(true);

        // Load events using AsNoTracking (should use less memory)
        var results = await _context.Events
            .AsNoTracking()
            .ToListAsync();

        var afterMemory = GC.GetTotalMemory(false);
        var memoryUsedMB = (afterMemory - beforeMemory) / (1024.0 * 1024.0);

        // Assert
        _output.WriteLine($"Memory used for loading 5000 events: {memoryUsedMB:F2} MB");

        // With AsNoTracking, memory usage should be reasonable (target < 100 MB for 5000 events)
        memoryUsedMB.Should().BeLessThan(100);
        results.Should().HaveCount(5000);
    }

    [Fact]
    public async Task ConcurrencyTest_MultipleSimultaneousReads_HandleCorrectly()
    {
        // Arrange
        var events = GenerateTestEvents(1000);
        await _eventRepository.AddRangeAsync(events);

        // Act - Simulate 20 concurrent read operations
        var stopwatch = Stopwatch.StartNew();
        var tasks = Enumerable.Range(1, 20).Select(async _ =>
        {
            var (pageEvents, _) = await _eventRepository.GetPagedAsync(1, 50);
            return pageEvents.Count();
        });

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"20 concurrent read operations completed in {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000);

        results.Should().AllSatisfy(x => x.Should().Be(50));
    }

    [Fact]
    public async Task BulkOperationTest_DeleteManyEvents_PerformsWell()
    {
        // Arrange
        var events = GenerateTestEvents(1000);
        await _eventRepository.AddRangeAsync(events);

        var eventsToDelete = (await _eventRepository.GetAllAsync()).Take(500).ToList();

        // Act
        var stopwatch = Stopwatch.StartNew();
        await _eventRepository.DeleteRangeAsync(eventsToDelete);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Deleted 500 events in {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000);

        var remainingCount = await _eventRepository.CountAsync();
        remainingCount.Should().Be(500);
    }

    #region Helper Methods

    private List<Event> GenerateTestEvents(int count)
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var categories = EventCategory.AllCategories;
        var startDate = new DateTime(2020, 1, 1);

        return Enumerable.Range(1, count).Select(i =>
        {
            var category = categories[random.Next(categories.Length)];
            var eventDate = startDate.AddDays(random.Next(0, 1825)); // Random date within 5 years

            return new Event
            {
                EventId = Guid.NewGuid().ToString(),
                Title = $"Performance Test Event {i}",
                Description = $"This is a test event for performance testing. Event number {i}.",
                StartDate = eventDate,
                Category = category,
                Location = $"Location {i % 100}",
                Confidence = random.NextDouble(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }).ToList();
    }

    #endregion

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
