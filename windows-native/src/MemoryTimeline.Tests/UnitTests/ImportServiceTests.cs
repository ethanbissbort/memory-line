using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Tests;
using Moq;
using System.Text.Json;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

public class ImportServiceTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly AppDbContext _context; // seeding context over the same in-memory store
    private readonly ImportService _importService;
    private readonly Mock<ILogger<ImportService>> _loggerMock;
    private readonly string _tempDirectory;

    public ImportServiceTests()
    {
        // Factory over a uniquely named in-memory database; ImportService creates
        // its own short-lived contexts from it. Assertions use fresh contexts so
        // stale tracked entities in the seeding context can't mask imported changes.
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _context = _contextFactory.CreateDbContext();
        _loggerMock = new Mock<ILogger<ImportService>>();
        _importService = new ImportService(_contextFactory, _loggerMock.Object);

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

        await using var verifyContext = _contextFactory.CreateDbContext();
        var events = await verifyContext.Events.ToListAsync();
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

        await using var verifyContext = _contextFactory.CreateDbContext();
        var events = await verifyContext.Events.ToListAsync();
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
        // Overwriting an existing event is counted as an update, not an import.
        result.EventsUpdated.Should().Be(1);
        result.EventsImported.Should().Be(0);

        await using var verifyContext = _contextFactory.CreateDbContext();
        var updatedEvent = await verifyContext.Events.FirstAsync();
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

        await using var verifyContext = _contextFactory.CreateDbContext();
        var events = await verifyContext.Events.ToListAsync();
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

        await using var verifyContext = _contextFactory.CreateDbContext();
        var tags = await verifyContext.Tags.ToListAsync();
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

        await using var verifyContext = _contextFactory.CreateDbContext();
        var eras = await verifyContext.Eras.ToListAsync();
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
        // Use a synchronous IProgress so reports are captured inline (deterministic).
        // System.Progress<T> reports asynchronously and can run its callbacks after the
        // assertions, making this test flaky.
        var progress = new SynchronousProgress<(int, string)>(p => progressReports.Add(p));

        // Act
        await _importService.ImportFromJsonAsync(filePath, progress: progress);

        // Assert
        progressReports.Should().NotBeEmpty();
        progressReports.Should().Contain(p => p.percentage == 100);
    }

    // ---- Sync projections (design §19 Phase 3) ----

    [Fact]
    public async Task ImportFromJsonAsync_PublishesOneProjectionPerImportedEvent()
    {
        // Arrange
        var published = new List<(string Kind, string EntityId)>();
        var service = CreateImportServiceWith(CreateRecordingPublisher(published).Object);
        var filePath = await WriteImportFileAsync("published_events.json", new
        {
            Events = new[]
            {
                new
                {
                    EventId = "sync1",
                    Title = "First Imported",
                    StartDate = new DateTime(2024, 1, 15),
                    // Tags ride along inside the event projection, so three of
                    // them must still produce exactly one publish.
                    Tags = new[] { "tag1", "tag2", "tag3" },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new
                {
                    EventId = "sync2",
                    Title = "Second Imported",
                    StartDate = new DateTime(2024, 2, 20),
                    Tags = Array.Empty<string>(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        });

        // Act
        var result = await service.ImportFromJsonAsync(filePath);

        // Assert
        result.Success.Should().BeTrue();
        published.Should().Equal(("event", "sync1"), ("event", "sync2"));
    }

    [Fact]
    public async Task ImportFromJsonAsync_OverwritesExisting_PublishesTheEventIdThatWasWritten()
    {
        // Arrange - the file carries its own id, but an overwrite updates the
        // row already in the archive; publishing the file's id would project an
        // event that does not exist and leave the real one stale on a companion.
        _context.Events.Add(new Event
        {
            EventId = "already-here",
            Title = "Shared Title",
            StartDate = new DateTime(2024, 1, 15),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var published = new List<(string Kind, string EntityId)>();
        var service = CreateImportServiceWith(CreateRecordingPublisher(published).Object);
        var filePath = await WriteImportFileAsync("published_overwrite.json", new
        {
            Events = new[]
            {
                new
                {
                    EventId = "id-from-the-file",
                    Title = "Shared Title",
                    Description = "Updated Description",
                    StartDate = new DateTime(2024, 1, 15),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        });

        // Act
        var result = await service.ImportFromJsonAsync(
            filePath, new ImportOptions { ConflictResolution = ConflictResolution.Overwrite });

        // Assert
        result.EventsUpdated.Should().Be(1);
        published.Should().Equal(("event", "already-here"));
    }

    [Fact]
    public async Task ImportFromJsonAsync_SkippedDuplicate_PublishesNothing()
    {
        // Arrange
        _context.Events.Add(new Event
        {
            EventId = "untouched",
            Title = "Shared Title",
            StartDate = new DateTime(2024, 1, 15),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var published = new List<(string Kind, string EntityId)>();
        var service = CreateImportServiceWith(CreateRecordingPublisher(published).Object);
        var filePath = await WriteImportFileAsync("published_skip.json", new
        {
            Events = new[]
            {
                new
                {
                    EventId = "ignored",
                    Title = "Shared Title",
                    StartDate = new DateTime(2024, 1, 15),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        });

        // Act
        var result = await service.ImportFromJsonAsync(
            filePath, new ImportOptions { ConflictResolution = ConflictResolution.Skip });

        // Assert - a skipped event wrote nothing, so there is nothing to project
        result.EventsSkipped.Should().Be(1);
        published.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportFromJsonAsync_PublishesOnlyTheErasItCreated()
    {
        // Arrange - an era whose name already exists is dropped by the import,
        // so it must not be projected either
        _context.Eras.Add(new Era
        {
            EraId = "existing-era",
            Name = "College Years",
            StartDate = new DateTime(2015, 9, 1),
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var published = new List<(string Kind, string EntityId)>();
        var service = CreateImportServiceWith(CreateRecordingPublisher(published).Object);
        var filePath = await WriteImportFileAsync("published_eras.json", new
        {
            Events = new[]
            {
                new
                {
                    EventId = "era-import-event",
                    Title = "An Event",
                    StartDate = new DateTime(2020, 5, 1),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            },
            Eras = new[]
            {
                new
                {
                    EraId = "era-from-file",
                    Name = "College Years",
                    StartDate = new DateTime(2015, 9, 1),
                    CreatedAt = DateTime.UtcNow
                },
                new
                {
                    EraId = "new-era",
                    Name = "First Job",
                    StartDate = new DateTime(2019, 6, 1),
                    CreatedAt = DateTime.UtcNow
                }
            }
        });

        // Act
        var result = await service.ImportFromJsonAsync(filePath);

        // Assert - and the era leads: the small set reaches the feed before the
        // long tail of events
        result.ErasImported.Should().Be(1);
        published.Should().Equal(("era", "new-era"), ("event", "era-import-event"));
    }

    [Fact]
    public async Task ImportFromJsonAsync_PublishesOnlyRowsTheSaveAlreadyCommitted()
    {
        // Arrange - the publisher reads each entity back through its own
        // connection in production, so a publish issued before SaveChangesAsync
        // would find nothing and quietly project nothing at all. This asserts
        // the pass runs after the commit, by looking the row up from a context
        // the import is not using.
        var visibleAtPublishTime = new List<(string Kind, bool Visible)>();
        var publisherMock = new Mock<ITimelineProjectionPublisher>();
        publisherMock
            .Setup(p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((eventId, _) =>
            {
                using var context = _contextFactory.CreateDbContext();
                visibleAtPublishTime.Add(("event", context.Events.Any(e => e.EventId == eventId)));
            })
            .Returns(Task.CompletedTask);
        publisherMock
            .Setup(p => p.PublishEraAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((eraId, _) =>
            {
                using var context = _contextFactory.CreateDbContext();
                visibleAtPublishTime.Add(("era", context.Eras.Any(e => e.EraId == eraId)));
            })
            .Returns(Task.CompletedTask);

        var service = CreateImportServiceWith(publisherMock.Object);
        var filePath = await WriteImportFileAsync("published_after_commit.json", new
        {
            Events = new[]
            {
                new
                {
                    EventId = "committed-event",
                    Title = "Committed",
                    StartDate = new DateTime(2024, 3, 1),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            },
            Eras = new[]
            {
                new
                {
                    EraId = "committed-era",
                    Name = "Committed Era",
                    StartDate = new DateTime(2024, 1, 1),
                    CreatedAt = DateTime.UtcNow
                }
            }
        });

        // Act
        await service.ImportFromJsonAsync(filePath);

        // Assert
        visibleAtPublishTime.Should().HaveCount(2).And.OnlyContain(v => v.Visible);
    }

    [Fact]
    public async Task ImportFromJsonAsync_ProjectionPublisherThrows_StillReportsASuccessfulImport()
    {
        // Arrange - the sync outbox is unavailable for every row
        var publisherMock = new Mock<ITimelineProjectionPublisher>();
        publisherMock
            .Setup(p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sync outbox unavailable"));
        publisherMock
            .Setup(p => p.PublishEraAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sync outbox unavailable"));
        var service = CreateImportServiceWith(publisherMock.Object);

        var filePath = await WriteImportFileAsync("publisher_throws.json", new
        {
            Events = new[]
            {
                new
                {
                    EventId = "survivor1",
                    Title = "Survivor One",
                    StartDate = new DateTime(2024, 4, 1),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new
                {
                    EventId = "survivor2",
                    Title = "Survivor Two",
                    StartDate = new DateTime(2024, 4, 2),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            },
            Eras = new[]
            {
                new
                {
                    EraId = "survivor-era",
                    Name = "Survivor Era",
                    StartDate = new DateTime(2024, 1, 1),
                    CreatedAt = DateTime.UtcNow
                }
            }
        });

        // Act
        var result = await service.ImportFromJsonAsync(filePath);

        // Assert - the import committed before the publish pass ran, and a slow,
        // hard-to-repeat operation must never be reported as failed because a
        // companion could not be told about it
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.EventsImported.Should().Be(2);
        result.ErasImported.Should().Be(1);

        await using var verifyContext = _contextFactory.CreateDbContext();
        (await verifyContext.Events.CountAsync()).Should().Be(2);
        (await verifyContext.Eras.CountAsync()).Should().Be(1);

        // And a failure that hits every row is summarised once, not logged per
        // entity: a 5,000-event import would otherwise bury its own errors.
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportFromJsonAsync_WithoutAProjectionPublisher_ImportsNormally()
    {
        // Arrange - _importService uses the three-argument constructor, i.e. the
        // default null publisher every non-sync host (and every older test) gets
        var filePath = await WriteImportFileAsync("no_publisher.json", new
        {
            Events = new[]
            {
                new
                {
                    EventId = "unsynced",
                    Title = "Unsynced Event",
                    StartDate = new DateTime(2024, 5, 1),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        });

        // Act
        var result = await _importService.ImportFromJsonAsync(filePath);

        // Assert
        result.Success.Should().BeTrue();
        result.EventsImported.Should().Be(1);
    }

    // ---- Projection helpers ----

    /// <summary>
    /// Builds an import service wired to a projection publisher. Mirrors the
    /// default fixture in every other respect, so a projection test differs from
    /// a plain one by exactly the dependency under test.
    /// </summary>
    private ImportService CreateImportServiceWith(ITimelineProjectionPublisher projectionPublisher) =>
        new(_contextFactory, _loggerMock.Object, projectionPublisher: projectionPublisher);

    /// <summary>
    /// A publisher mock that records WHAT was projected and in WHICH order.
    /// Payloads are the Sync layer's business and are covered there; what the
    /// import owes the feed is one call per row it actually wrote, after the
    /// save, for the id that ended up in the archive.
    /// </summary>
    private static Mock<ITimelineProjectionPublisher> CreateRecordingPublisher(
        List<(string Kind, string EntityId)> published)
    {
        var mock = new Mock<ITimelineProjectionPublisher>();

        mock.Setup(p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((eventId, _) => published.Add(("event", eventId)))
            .Returns(Task.CompletedTask);

        mock.Setup(p => p.PublishEraAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((eraId, _) => published.Add(("era", eraId)))
            .Returns(Task.CompletedTask);

        return mock;
    }

    private async Task<string> WriteImportFileAsync(string fileName, object importData)
    {
        var filePath = Path.Combine(_tempDirectory, fileName);
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(importData));
        return filePath;
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

    /// <summary>
    /// An <see cref="IProgress{T}"/> that invokes its handler synchronously on the
    /// calling thread, making progress-report assertions deterministic in tests.
    /// </summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SynchronousProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
