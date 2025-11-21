using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using Xunit;

namespace MemoryTimeline.Tests.Integration;

/// <summary>
/// Integration tests for database operations using EF Core with SQLite.
/// </summary>
public class DatabaseIntegrationTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly EventRepository _eventRepository;
    private readonly string _databasePath;

    public DatabaseIntegrationTests()
    {
        // Create a real SQLite database for integration testing
        _databasePath = Path.Combine(Path.GetTempPath(), $"IntegrationTest_{Guid.NewGuid()}.db");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _eventRepository = new EventRepository(_context);
    }

    [Fact]
    public async Task DatabaseConnection_CanConnectAndExecuteQueries()
    {
        // Act
        var canConnect = await _context.Database.CanConnectAsync();

        // Assert
        canConnect.Should().BeTrue();
    }

    [Fact]
    public async Task Event_FullCRUDOperations_WorksCorrectly()
    {
        // Create
        var newEvent = new Event
        {
            EventId = Guid.NewGuid().ToString(),
            Title = "Integration Test Event",
            Description = "Testing full CRUD operations",
            StartDate = DateTime.UtcNow,
            Category = EventCategory.Work,
            Location = "Test Location",
            CreatedAt = DateTime.UtcNow
        };

        var created = await _eventRepository.AddAsync(newEvent);
        created.Should().NotBeNull();
        created.EventId.Should().Be(newEvent.EventId);

        // Read
        var retrieved = await _eventRepository.GetByIdAsync(created.EventId);
        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be("Integration Test Event");

        // Update
        retrieved.Title = "Updated Title";
        retrieved.Description = "Updated Description";
        await _eventRepository.UpdateAsync(retrieved);

        var updated = await _eventRepository.GetByIdAsync(retrieved.EventId);
        updated!.Title.Should().Be("Updated Title");
        updated.Description.Should().Be("Updated Description");

        // Delete
        await _eventRepository.DeleteAsync(updated);
        var deleted = await _eventRepository.GetByIdAsync(updated.EventId);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task EventWithRelationships_SavesAndLoadsCorrectly()
    {
        // Arrange
        var era = new Era
        {
            EraId = Guid.NewGuid().ToString(),
            Name = "Test Era",
            StartDate = new DateTime(2020, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            Color = "#FF0000",
            CreatedAt = DateTime.UtcNow
        };
        _context.Eras.Add(era);

        var tag1 = new Tag { TagId = Guid.NewGuid().ToString(), Name = "tag1", CreatedAt = DateTime.UtcNow };
        var tag2 = new Tag { TagId = Guid.NewGuid().ToString(), Name = "tag2", CreatedAt = DateTime.UtcNow };
        _context.Tags.AddRange(tag1, tag2);

        await _context.SaveChangesAsync();

        var eventWithRelationships = new Event
        {
            EventId = Guid.NewGuid().ToString(),
            Title = "Event with Relationships",
            StartDate = DateTime.UtcNow,
            EraId = era.EraId,
            Tags = new List<Tag> { tag1, tag2 },
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _eventRepository.AddAsync(eventWithRelationships);

        // Assert
        var loaded = await _eventRepository.GetByIdWithIncludesAsync(eventWithRelationships.EventId);
        loaded.Should().NotBeNull();
        loaded!.Era.Should().NotBeNull();
        loaded.Era!.Name.Should().Be("Test Era");
        loaded.Tags.Should().HaveCount(2);
        loaded.Tags.Should().Contain(t => t.Name == "tag1");
        loaded.Tags.Should().Contain(t => t.Name == "tag2");
    }

    [Fact]
    public async Task EventRepository_Pagination_WorksCorrectly()
    {
        // Arrange - Create 25 events
        var events = Enumerable.Range(1, 25).Select(i => new Event
        {
            EventId = Guid.NewGuid().ToString(),
            Title = $"Event {i}",
            StartDate = DateTime.UtcNow.AddDays(i),
            CreatedAt = DateTime.UtcNow
        }).ToList();

        await _eventRepository.AddRangeAsync(events);

        // Act - Get first page (10 items)
        var (page1Events, totalCount) = await _eventRepository.GetPagedAsync(
            pageNumber: 1,
            pageSize: 10);

        // Assert
        totalCount.Should().Be(25);
        page1Events.Should().HaveCount(10);

        // Act - Get second page
        var (page2Events, _) = await _eventRepository.GetPagedAsync(
            pageNumber: 2,
            pageSize: 10);

        // Assert
        page2Events.Should().HaveCount(10);

        // Act - Get third page (only 5 items remaining)
        var (page3Events, _) = await _eventRepository.GetPagedAsync(
            pageNumber: 3,
            pageSize: 10);

        // Assert
        page3Events.Should().HaveCount(5);
    }

    [Fact]
    public async Task EventRepository_DateRangeQuery_ReturnsCorrectEvents()
    {
        // Arrange
        var events = new List<Event>
        {
            new() { EventId = Guid.NewGuid().ToString(), Title = "Event 2022", StartDate = new DateTime(2022, 6, 15), CreatedAt = DateTime.UtcNow },
            new() { EventId = Guid.NewGuid().ToString(), Title = "Event 2023", StartDate = new DateTime(2023, 6, 15), CreatedAt = DateTime.UtcNow },
            new() { EventId = Guid.NewGuid().ToString(), Title = "Event 2024", StartDate = new DateTime(2024, 6, 15), CreatedAt = DateTime.UtcNow },
            new() { EventId = Guid.NewGuid().ToString(), Title = "Event 2025", StartDate = new DateTime(2025, 6, 15), CreatedAt = DateTime.UtcNow }
        };

        await _eventRepository.AddRangeAsync(events);

        // Act
        var results = await _eventRepository.GetByDateRangeAsync(
            new DateTime(2023, 1, 1),
            new DateTime(2024, 12, 31));

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(e => e.Title == "Event 2023");
        results.Should().Contain(e => e.Title == "Event 2024");
    }

    [Fact]
    public async Task EventRepository_SearchByTitle_ReturnsMatchingEvents()
    {
        // Arrange
        var events = new List<Event>
        {
            new() { EventId = Guid.NewGuid().ToString(), Title = "Important Meeting", StartDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
            new() { EventId = Guid.NewGuid().ToString(), Title = "Regular Checkup", StartDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
            new() { EventId = Guid.NewGuid().ToString(), Title = "Important Call", StartDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
            new() { EventId = Guid.NewGuid().ToString(), Title = "Lunch Break", StartDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow }
        };

        await _eventRepository.AddRangeAsync(events);

        // Act
        var results = await _eventRepository.SearchAsync("Important");

        // Assert
        results.Should().HaveCount(2);
        results.All(e => e.Title.Contains("Important")).Should().BeTrue();
    }

    [Fact]
    public async Task EventRepository_CategoryQuery_ReturnsCorrectEvents()
    {
        // Arrange
        var events = new List<Event>
        {
            new() { EventId = Guid.NewGuid().ToString(), Title = "Work Event 1", Category = EventCategory.Work, StartDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
            new() { EventId = Guid.NewGuid().ToString(), Title = "Personal Event", Category = EventCategory.Personal, StartDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
            new() { EventId = Guid.NewGuid().ToString(), Title = "Work Event 2", Category = EventCategory.Work, StartDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow },
            new() { EventId = Guid.NewGuid().ToString(), Title = "Health Event", Category = EventCategory.Health, StartDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow }
        };

        await _eventRepository.AddRangeAsync(events);

        // Act
        var workEvents = await _eventRepository.GetByCategoryAsync(EventCategory.Work);

        // Assert
        workEvents.Should().HaveCount(2);
        workEvents.All(e => e.Category == EventCategory.Work).Should().BeTrue();
    }

    [Fact]
    public async Task Transaction_RollbackOnError_MaintainsConsistency()
    {
        // Arrange
        var initialCount = await _eventRepository.CountAsync();

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Add valid event
                var event1 = new Event
                {
                    EventId = Guid.NewGuid().ToString(),
                    Title = "Valid Event",
                    StartDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                await _eventRepository.AddAsync(event1);

                // Try to add invalid event (duplicate ID)
                var event2 = new Event
                {
                    EventId = event1.EventId, // Same ID - should fail
                    Title = "Invalid Event",
                    StartDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                await _eventRepository.AddAsync(event2);

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });

        // Verify rollback - count should be unchanged
        var finalCount = await _eventRepository.CountAsync();
        finalCount.Should().Be(initialCount);
    }

    [Fact]
    public async Task ConcurrentWrites_HandleCorrectly()
    {
        // Arrange & Act
        var tasks = Enumerable.Range(1, 10).Select(async i =>
        {
            var evt = new Event
            {
                EventId = Guid.NewGuid().ToString(),
                Title = $"Concurrent Event {i}",
                StartDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            return await _eventRepository.AddAsync(evt);
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(10);
        results.Select(e => e.EventId).Should().OnlyHaveUniqueItems();

        var allEvents = await _eventRepository.GetAllAsync();
        allEvents.Count(e => e.Title.StartsWith("Concurrent Event")).Should().Be(10);
    }

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
