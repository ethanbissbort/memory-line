using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using Moq;
using System.Text.Json;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

public class ExportServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly ExportService _exportService;
    private readonly Mock<ILogger<ExportService>> _loggerMock;
    private readonly string _tempDirectory;

    public ExportServiceTests()
    {
        // Create in-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ExportTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _loggerMock = new Mock<ILogger<ExportService>>();
        _exportService = new ExportService(_context, _loggerMock.Object);

        // Create temp directory for test files
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"MemoryTimelineTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);

        // Seed test data
        SeedTestData();
    }

    private void SeedTestData()
    {
        var events = new List<Event>
        {
            new()
            {
                EventId = "1",
                Title = "Event 1",
                Description = "Description 1",
                StartDate = new DateTime(2024, 1, 15),
                Category = EventCategory.Work,
                Location = "Office",
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                EventId = "2",
                Title = "Event 2",
                Description = "Description 2",
                StartDate = new DateTime(2024, 6, 20),
                Category = EventCategory.Relationship,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                EventId = "3",
                Title = "Event 3",
                StartDate = new DateTime(2023, 12, 1),
                Category = EventCategory.Milestone,
                CreatedAt = DateTime.UtcNow
            }
        };

        var eras = new List<Era>
        {
            new()
            {
                EraId = "era1",
                Name = "Era 1",
                StartDate = new DateTime(2023, 1, 1),
                EndDate = new DateTime(2024, 12, 31),
                Color = "#FF5733",
                CreatedAt = DateTime.UtcNow
            }
        };

        var tags = new List<Tag>
        {
            new()
            {
                TagId = "tag1",
                Name = "Important",
                Color = "#0000FF",
                CreatedAt = DateTime.UtcNow
            }
        };

        _context.Events.AddRange(events);
        _context.Eras.AddRange(eras);
        _context.Tags.AddRange(tags);
        _context.SaveChanges();
    }

    [Fact]
    public async Task ExportToJsonAsync_AllEvents_CreatesValidJsonFile()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "export.json");

        // Act
        await _exportService.ExportToJsonAsync(filePath);

        // Assert
        File.Exists(filePath).Should().BeTrue();

        var json = await File.ReadAllTextAsync(filePath);
        var exportData = JsonSerializer.Deserialize<JsonElement>(json);

        exportData.GetProperty("events").GetArrayLength().Should().Be(3);
        exportData.GetProperty("eventCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task ExportToJsonAsync_WithDateFilter_ExportsFilteredEvents()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "filtered_export.json");
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 12, 31);

        // Act
        await _exportService.ExportToJsonAsync(filePath, startDate, endDate);

        // Assert
        File.Exists(filePath).Should().BeTrue();

        var json = await File.ReadAllTextAsync(filePath);
        var exportData = JsonSerializer.Deserialize<JsonElement>(json);

        // Should only have events from 2024 (Event 1 and Event 2)
        exportData.GetProperty("events").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ExportToCsvAsync_AllEvents_CreatesValidCsvFile()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "export.csv");

        // Act
        await _exportService.ExportToCsvAsync(filePath);

        // Assert
        File.Exists(filePath).Should().BeTrue();

        var lines = await File.ReadAllLinesAsync(filePath);
        lines.Should().HaveCount(4); // 1 header + 3 events
        lines[0].Should().Contain("EventId,Title,Description");
    }

    [Fact]
    public async Task ExportToMarkdownAsync_AllEvents_CreatesValidMarkdownFile()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "export.md");

        // Act
        await _exportService.ExportToMarkdownAsync(filePath);

        // Assert
        File.Exists(filePath).Should().BeTrue();

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("# Memory Timeline Export");
        content.Should().Contain("## 2024");
        content.Should().Contain("## 2023");
        content.Should().Contain("### Event 1");
        content.Should().Contain("### Event 2");
        content.Should().Contain("### Event 3");
    }

    [Fact]
    public async Task ExportFullDatabaseAsync_AllData_ExportsEventsErasAndTags()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "full_export.json");

        // Act
        await _exportService.ExportFullDatabaseAsync(filePath);

        // Assert
        File.Exists(filePath).Should().BeTrue();

        var json = await File.ReadAllTextAsync(filePath);
        var exportData = JsonSerializer.Deserialize<JsonElement>(json);

        exportData.GetProperty("events").GetArrayLength().Should().Be(3);
        exportData.GetProperty("eras").GetArrayLength().Should().Be(1);
        exportData.GetProperty("tags").GetArrayLength().Should().Be(1);
        exportData.GetProperty("version").GetString().Should().Be("1.0");
    }

    [Fact]
    public async Task ExportToJsonAsync_ReportsProgress_InvokesProgressCallback()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "progress_export.json");
        var progressReports = new List<int>();
        var progress = new Progress<int>(p => progressReports.Add(p));

        // Act
        await _exportService.ExportToJsonAsync(filePath, progress: progress);

        // Assert
        progressReports.Should().Contain(50);
        progressReports.Should().Contain(100);
    }

    [Fact]
    public async Task ExportToCsvAsync_WithSpecialCharacters_EscapesCorrectly()
    {
        // Arrange
        var specialEvent = new Event
        {
            EventId = "special",
            Title = "Event with \"quotes\" and, commas",
            Description = "Description with\nnewlines",
            StartDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Events.Add(specialEvent);
        await _context.SaveChangesAsync();

        var filePath = Path.Combine(_tempDirectory, "special_export.csv");

        // Act
        await _exportService.ExportToCsvAsync(filePath);

        // Assert
        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("\"Event with \"\"quotes\"\" and, commas\"");
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
