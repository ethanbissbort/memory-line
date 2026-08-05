using System.Data.Common;
using System.Text;
using FluentAssertions;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.Integration;

/// <summary>
/// Integration tests for the event_media schema against a real file-based
/// SQLite database (following the DatePrecisionSchemaTests pattern): an
/// old-shape database that predates the event_media table is built via raw
/// DDL, upgraded with <see cref="SchemaUpgrader.EnsureSchemaAsync"/>, and
/// inspected via sqlite_master/PRAGMA plus EF reads. Also verifies the
/// event -> event_media FK cascade and the EventService delete path that
/// cleans up the managed files the cascade cannot touch.
/// </summary>
public class EventMediaSchemaTests : IDisposable
{
    private static readonly string[] EventMediaColumns =
    {
        "media_id", "event_id", "file_path", "thumbnail_path", "media_type",
        "caption", "captured_at", "latitude", "longitude", "file_size_bytes",
        "content_hash", "sort_order", "created_at"
    };

    private readonly List<(TestDbContextFactory Factory, string DatabasePath)> _databases = new();
    private readonly List<string> _tempDirectories = new();

    [Fact]
    public async Task EnsureSchemaAsync_OldShapeDatabase_CreatesEventMediaTableAndIndexesIdempotently()
    {
        // Arrange - OLD-shape database: an events table that predates
        // event_media (and location/confidence/date precision), with a row
        var factory = CreateFactory();
        await using (var setupContext = factory.CreateDbContext())
        {
            var connection = setupContext.Database.GetDbConnection();
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                """
                CREATE TABLE "events" (
                    "event_id" TEXT NOT NULL CONSTRAINT "PK_events" PRIMARY KEY,
                    "title" TEXT NOT NULL,
                    "start_date" TEXT NOT NULL,
                    "end_date" TEXT NULL,
                    "description" TEXT NULL,
                    "category" TEXT NULL,
                    "era_id" TEXT NULL,
                    "audio_file_path" TEXT NULL,
                    "raw_transcript" TEXT NULL,
                    "extraction_metadata" TEXT NULL,
                    "created_at" TEXT NOT NULL,
                    "updated_at" TEXT NOT NULL
                );
                """);
            await ExecuteAsync(connection,
                "INSERT INTO \"events\" (\"event_id\", \"title\", \"start_date\", \"created_at\", \"updated_at\") " +
                "VALUES ('legacy-event', 'Legacy Event', '2011-07-01 00:00:00', '2024-01-01 00:00:00', '2024-01-01 00:00:00');");
        }

        // Act - run the upgrade (EnsureCreated is a no-op because tables exist)
        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Assert - the table, its columns, and both indexes exist
        await using (var verifyContext = factory.CreateDbContext())
        {
            var connection = verifyContext.Database.GetDbConnection();
            await connection.OpenAsync();

            var tables = await GetNamesAsync(connection,
                "SELECT name FROM sqlite_master WHERE type='table'");
            tables.Should().Contain("event_media");

            var columns = await GetColumnNamesAsync(connection, "event_media");
            columns.Should().Contain(EventMediaColumns);

            var indexes = await GetNamesAsync(connection,
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='event_media'");
            indexes.Should().Contain(new[] { "IX_event_media_event_id", "IX_event_media_content_hash" });
        }

        // Assert - EF can write and read a media row against the upgraded table
        await using (var writeContext = factory.CreateDbContext())
        {
            writeContext.EventMedia.Add(new EventMedia
            {
                MediaId = "media-1",
                EventId = "legacy-event",
                FilePath = Path.Combine("2011", "07", "media-1.mp4"),
                MediaType = MediaType.Video,
                FileSizeBytes = 1234,
                ContentHash = new string('a', 64),
                SortOrder = 0,
                CreatedAt = new DateTime(2026, 8, 1)
            });
            await writeContext.SaveChangesAsync();
        }

        await using (var readContext = factory.CreateDbContext())
        {
            var stored = await readContext.EventMedia.AsNoTracking().SingleAsync();
            stored.EventId.Should().Be("legacy-event");
            stored.MediaType.Should().Be(MediaType.Video);
            stored.ThumbnailPath.Should().BeNull();
            stored.FileSizeBytes.Should().Be(1234);
            stored.SortOrder.Should().Be(0);
        }

        // Act again - the upgrade must be an idempotent no-op the second time
        await using (var secondUpgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(secondUpgradeContext);
        }

        // Assert - schema unchanged and the row survived
        await using (var finalContext = factory.CreateDbContext())
        {
            var connection = finalContext.Database.GetDbConnection();
            await connection.OpenAsync();
            var columns = await GetColumnNamesAsync(connection, "event_media");
            columns.Should().OnlyHaveUniqueItems();
            columns.Should().Contain(EventMediaColumns);

            (await finalContext.EventMedia.AsNoTracking().CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task DeletingEvent_CascadesEventMediaRows()
    {
        // Arrange - fresh database (full current model) with an event + 2 media
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.Database.EnsureCreated();
        }

        await using (var seedContext = factory.CreateDbContext())
        {
            seedContext.Events.Add(new Event
            {
                EventId = "evt-cascade",
                Title = "Event with media",
                StartDate = new DateTime(2021, 5, 1),
                Category = EventCategory.Other
            });
            seedContext.EventMedia.Add(new EventMedia
            {
                MediaId = "cascade-media-1",
                EventId = "evt-cascade",
                FilePath = Path.Combine("2021", "05", "cascade-media-1.jpg"),
                MediaType = MediaType.Image,
                SortOrder = 0
            });
            seedContext.EventMedia.Add(new EventMedia
            {
                MediaId = "cascade-media-2",
                EventId = "evt-cascade",
                FilePath = Path.Combine("2021", "05", "cascade-media-2.jpg"),
                MediaType = MediaType.Image,
                SortOrder = 1
            });
            await seedContext.SaveChangesAsync();
        }

        // Act - delete the event WITHOUT loading its media (the rows are
        // untracked, so removal relies on the DB-level ON DELETE CASCADE)
        await using (var deleteContext = factory.CreateDbContext())
        {
            var evt = await deleteContext.Events.SingleAsync(e => e.EventId == "evt-cascade");
            deleteContext.Events.Remove(evt);
            await deleteContext.SaveChangesAsync();
        }

        // Assert
        await using var verifyContext = factory.CreateDbContext();
        (await verifyContext.Events.AsNoTracking().CountAsync()).Should().Be(0);
        (await verifyContext.EventMedia.AsNoTracking().CountAsync()).Should().Be(0,
            "the event_media FK is ON DELETE CASCADE");
    }

    [Fact]
    public async Task EventServiceDelete_RemovesMediaRowsAndManagedFiles()
    {
        // Arrange - full stack on a real SQLite file: MediaService attaches a
        // real file, EventService deletes the event
        var factory = CreateFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.Database.EnsureCreated();
        }

        await using (var seedContext = factory.CreateDbContext())
        {
            seedContext.Events.Add(new Event
            {
                EventId = "evt-files",
                Title = "Event whose files must go",
                StartDate = new DateTime(2022, 9, 10),
                Category = EventCategory.Other
            });
            await seedContext.SaveChangesAsync();
        }

        var mediaRoot = CreateTempDirectory("EventMediaSchemaTests_Root");
        var sourceDir = CreateTempDirectory("EventMediaSchemaTests_Src");
        var sourcePath = Path.Combine(sourceDir, "photo.jpg");
        File.WriteAllBytes(sourcePath, Encoding.UTF8.GetBytes("managed file content"));

        var thumbnailMock = new Mock<IThumbnailGenerator>();
        thumbnailMock
            .Setup(t => t.TryGenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var mediaService = new MediaService(
            new EventMediaRepository(factory),
            factory,
            thumbnailMock.Object,
            new Mock<ILogger<MediaService>>().Object,
            mediaRoot);

        var media = await mediaService.AttachAsync("evt-files", sourcePath);
        var absoluteMediaPath = mediaService.GetAbsolutePath(media);
        File.Exists(absoluteMediaPath).Should().BeTrue();

        var eventService = new EventService(
            new EventRepository(factory),
            factory,
            new Mock<ILogger<EventService>>().Object,
            embeddingService: null,
            mediaService: mediaService);

        // Act
        await eventService.DeleteEventAsync("evt-files");

        // Assert - rows cascaded, managed file deleted, source untouched
        await using var verifyContext = factory.CreateDbContext();
        (await verifyContext.Events.AsNoTracking().CountAsync()).Should().Be(0);
        (await verifyContext.EventMedia.AsNoTracking().CountAsync()).Should().Be(0);
        File.Exists(absoluteMediaPath).Should().BeFalse("deleting the event must delete its managed files");
        File.Exists(sourcePath).Should().BeTrue("the original source file is never touched");
    }

    // ---- SQLite helpers ----

    private TestDbContextFactory CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"EventMediaSchemaTests_{Guid.NewGuid()}.db");
        var factory = TestDbContextFactory.CreateSqliteFile(databasePath);
        _databases.Add((factory, databasePath));
        return factory;
    }

    private string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> GetNamesAsync(DbConnection connection, string sql)
    {
        var names = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<List<string>> GetColumnNamesAsync(DbConnection connection, string tableName)
    {
        // Table names cannot be parameterized in PRAGMA statements; the names
        // used here are compile-time constants.
        var names = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        await using var reader = await command.ExecuteReaderAsync();
        var ordinal = reader.GetOrdinal("name");
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(ordinal));
        }

        return names;
    }

    public void Dispose()
    {
        foreach (var (factory, databasePath) in _databases)
        {
            using (var context = factory.CreateDbContext())
            {
                context.Database.EnsureDeleted();
            }

            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        foreach (var directory in _tempDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
