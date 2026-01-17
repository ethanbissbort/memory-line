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

public class ImportServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly ImportService _importService;
    private readonly Mock<ILogger<ImportService>> _loggerMock;
    private readonly string _tempDirectory;

    public ImportServiceTests()
    {
        // Create in-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ImportTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _loggerMock = new Mock<ILogger<ImportService>>();
        _importService = new ImportService(_context, _loggerMock.Object);

        // Create temp directory for test files
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"MemoryTimelineImportTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ImportFromJsonAsync_ValidFile_ImportsEvents()
    {
        // Arrange
        var importData = new
        {
            ExportDate = DateTime.UtcNow,
            Version = "1.0",
            Events = new[]
            {
                new
                {
                    EventId = "import1",
                    Title = "Imported Event 1",
                    Description = "Test Description",
                    StartDate = new DateTime(2024, 1, 15),
                    Category = "Work",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new
                {
                    EventId = "import2",
                    Title = "Imported Event 2",
                    Description = (string?)null,
                    StartDate = new DateTime(2024, 6, 20),
                    Category = "Personal",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        };

        var filePath = Path.Combine(_tempDirectory, "import.json");
        var json = JsonSerializer.Serialize(importData, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);

        var options = new ImportOptions
        {
            ConflictResolution = ConflictResolution.Skip
        };

        // Act
        var result = await _importService.ImportFromJsonAsync(filePath, options);

        // Assert
        result.Success.Should().BeTrue();
        result.EventsImported.Should().Be(2);
        result.EventsSkipped.Should().Be(0);

        var events = await _context.Events.ToListAsync();
        events.Should().HaveCount(2);
        events.Should().Contain(e => e.Title == "Imported Event 1");
        events.Should().Contain(e => e.Title == "Imported Event 2");
    }

    [Fact]
    public async Task ImportFromJsonAsync_WithDuplicates_SkipsConflicts()
    {
        // Arrange
        // Add existing event
        var existingEvent = new Event
        {
            EventId = "existing",
            Title = "Existing Event",
            StartDate = new DateTime(2024, 1, 15),
            CreatedAt = DateTime.UtcNow
        };
        _context.Events.Add(existingEvent);
        await _context.SaveChangesAsync();

        var importData = new
        {
            ExportDate = DateTime.UtcNow,
            Events = new[]
            {
                new
                {
                    EventId = "new-id",
                    Title = "Existing Event", // Same title
                    StartDate = new DateTime(2024, 1, 15), // Same date
                    Category = "Work",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        };

        var filePath = Path.Combine(_tempDirectory, "duplicate_import.json");
        var json = JsonSerializer.Serialize(importData);
        await File.WriteAllTextAsync(filePath, json);

        var options = new ImportOptions
        {
            ConflictResolution = ConflictResolution.Skip
        };

        // Act
        var result = await _importService.ImportFromJsonAsync(filePath, options);

        // Assert
        result.Success.Should().BeTrue();
        result.EventsImported.Should().Be(0);
        result.EventsSkipped.Should().Be(1);

        var events = await _context.Events.ToListAsync();
        events.Should().HaveCount(1); // Only the original
    }

    [Fact]
    public async Task ImportFromJsonAsync_WithOverwriteResolution_OverwritesExisting()
    {
        // Arrange
        var existingEvent = new Event
        {
            EventId = "existing",
            Title = "Original Title",
            Description = "Original Description",
            StartDate = new DateTime(2024, 1, 15),
            CreatedAt = DateTime.UtcNow
        };
        _context.Events.Add(existingEvent);
        await _context.SaveChangesAsync();

        var importData = new
        {
            Events = new[]
            {
                new
                {
                    EventId = "new-id",
                    Title = "Original Title",
                    Description = "Updated Description",
                    StartDate = new DateTime(2024, 1, 15),
                    Category = "Work",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        };

        var filePath = Path.Combine(_tempDirectory, "overwrite_import.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(importData));

        var options = new ImportOptions
        {
            ConflictResolution = ConflictResolution.Overwrite
        };

        // Act
        var result = await _importService.ImportFromJsonAsync(filePath, options);

        // Assert
        result.Success.Should().BeTrue();
        result.EventsImported.Should().Be(1);

        var updatedEvent = await _context.Events.FirstAsync();
        updatedEvent.Description.Should().Be("Updated Description");
    }

    [Fact]
    public async Task ImportFromJsonAsync_WithCreateDuplicateResolution_CreatesBoth()
    {
        // Arrange
        var existingEvent = new Event
        {
            EventId = "existing",
            Title = "Duplicate Title",
            StartDate = new DateTime(2024, 1, 15),
            CreatedAt = DateTime.UtcNow
        };
        _context.Events.Add(existingEvent);
        await _context.SaveChangesAsync();

        var importData = new
        {
            Events = new[]
            {
                new
                {
                    EventId = "",
                    Title = "Duplicate Title",
                    StartDate = new DateTime(2024, 1, 15),
                    Category = "Work",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        };

        var filePath = Path.Combine(_tempDirectory, "create_duplicate_import.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(importData));

        var options = new ImportOptions
        {
            ConflictResolution = ConflictResolution.CreateDuplicate
        };

        // Act
        var result = await _importService.ImportFromJsonAsync(filePath, options);

        // Assert
        result.Success.Should().BeTrue();
        result.EventsImported.Should().Be(1);

        var events = await _context.Events.ToListAsync();
        events.Should().HaveCount(2);
    }

    [Fact]
    public async Task ImportFromJsonAsync_WithTags_ImportsTags()
    {
        // Arrange
        var importData = new
        {
            Events = new[]
            {
                new
                {
                    EventId = "event-with-tags",
                    Title = "Event With Tags",
                    StartDate = DateTime.UtcNow,
                    Tags = new[] { "tag1", "tag2", "tag3" },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            },
            Tags = new[]
            {
                new { TagId = "tag1", Name = "tag1", CreatedAt = DateTime.UtcNow },
                new { TagId = "tag2", Name = "tag2", CreatedAt = DateTime.UtcNow },
                new { TagId = "tag3", Name = "tag3", CreatedAt = DateTime.UtcNow }
            }
        };

        var filePath = Path.Combine(_tempDirectory, "tags_import.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(importData));

        var options = new ImportOptions
        {
            ImportTags = true
        };

        // Act
        var result = await _importService.ImportFromJsonAsync(filePath, options);

        // Assert
        result.Success.Should().BeTrue();
        result.EventsImported.Should().Be(1);
        result.TagsImported.Should().Be(3);

        var tags = await _context.Tags.ToListAsync();
        tags.Should().HaveCount(3);
    }

    [Fact]
    public async Task ImportFromJsonAsync_WithEras_ImportsEras()
    {
        // Arrange
        var importData = new
        {
            Events = Array.Empty<object>(),
            Eras = new[]
            {
                new
                {
                    EraId = "era1",
                    Name = "College Years",
                    StartDate = new DateTime(2015, 9, 1),
                    EndDate = new DateTime(2019, 5, 31),
                    Color = "#FF5733",
                    CreatedAt = DateTime.UtcNow
                }
            }
        };

        var filePath = Path.Combine(_tempDirectory, "eras_import.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(importData));

        var options = new ImportOptions
        {
            ImportEras = true
        };

        // Act
        var result = await _importService.ImportFromJsonAsync(filePath, options);

        // Assert
        result.Success.Should().BeTrue();
        result.ErasImported.Should().Be(1);

        var eras = await _context.Eras.ToListAsync();
        eras.Should().HaveCount(1);
        eras.First().Name.Should().Be("College Years");
    }

    [Fact]
    public async Task ValidateImportFileAsync_ValidFile_ReturnsValid()
    {
        // Arrange
        var importData = new
        {
            Events = new[]
            {
                new
                {
                    EventId = "valid1",
                    Title = "Valid Event",
                    StartDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        };

        var filePath = Path.Combine(_tempDirectory, "validate.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(importData));

        // Act
        var result = await _importService.ValidateImportFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeTrue();
        result.EventCount.Should().Be(1);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateImportFileAsync_InvalidFile_ReturnsInvalid()
    {
        // Arrange
        var filePath = Path.Combine(_tempDirectory, "invalid.json");
        await File.WriteAllTextAsync(filePath, "{ invalid json");

        // Act
        var result = await _importService.ValidateImportFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ImportFromJsonAsync_ReportsProgress_InvokesProgressCallback()
    {
        // Arrange
        var importData = new
        {
            Events = new[]
            {
                new
                {
                    EventId = "progress1",
                    Title = "Progress Test",
                    StartDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        };

        var filePath = Path.Combine(_tempDirectory, "progress_import.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(importData));

        var progressReports = new List<(int percentage, string message)>();
        var progress = new Progress<(int, string)>(p => progressReports.Add(p));

        // Act
        await _importService.ImportFromJsonAsync(filePath, progress: progress);

        // Assert
        progressReports.Should().NotBeEmpty();
        progressReports.Should().Contain(p => p.percentage == 100);
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
