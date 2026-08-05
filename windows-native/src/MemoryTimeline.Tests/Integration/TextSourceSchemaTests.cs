using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Tests;
using Xunit;

namespace MemoryTimeline.Tests.Integration;

/// <summary>
/// Integration tests for the 2026-08 text-source schema upgrade on
/// recording_queue against a real file-based SQLite database: an old-shape
/// database (audio-only recording_queue, no source_type/source_label/transcript
/// columns) is built via raw DDL, upgraded with
/// <see cref="SchemaUpgrader.EnsureSchemaAsync"/>, and inspected via PRAGMA.
/// </summary>
public class TextSourceSchemaTests : IDisposable
{
    private static readonly string[] ExpectedRecordingQueueColumns =
    {
        "queue_id", "audio_file_path", "status", "duration_seconds",
        "file_size_bytes", "created_at", "processed_at", "error_message",
        "source_type", "source_label", "transcript"
    };

    private readonly List<(TestDbContextFactory Factory, string DatabasePath)> _databases = new();

    [Fact]
    public async Task EnsureSchemaAsync_OldShapeRecordingQueue_AddsSourceColumns_Idempotently()
    {
        // Arrange — build an OLD-shape database: recording_queue with only the
        // original audio columns and one legacy row
        var factory = CreateFactory();
        await using (var setupContext = factory.CreateDbContext())
        {
            var connection = setupContext.Database.GetDbConnection();
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                """
                CREATE TABLE "recording_queue" (
                    "queue_id" TEXT NOT NULL CONSTRAINT "PK_recording_queue" PRIMARY KEY,
                    "audio_file_path" TEXT NOT NULL,
                    "status" TEXT NOT NULL,
                    "duration_seconds" REAL NULL,
                    "file_size_bytes" INTEGER NULL,
                    "created_at" TEXT NOT NULL,
                    "processed_at" TEXT NULL,
                    "error_message" TEXT NULL
                );
                """);
            await ExecuteAsync(connection,
                "INSERT INTO \"recording_queue\" (\"queue_id\", \"audio_file_path\", \"status\", \"created_at\") " +
                "VALUES ('legacy-1', 'C:\\audio\\old.wav', 'completed', '2024-01-01 00:00:00');");
        }

        // Act — run the upgrade (EnsureCreated is a no-op because a table exists)
        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Assert — the three new columns exist
        await using (var verifyContext = factory.CreateDbContext())
        {
            var connection = verifyContext.Database.GetDbConnection();
            await connection.OpenAsync();
            var columns = await GetColumnNamesAsync(connection, "recording_queue");
            columns.Should().BeEquivalentTo(ExpectedRecordingQueueColumns);
        }

        // Act again — a second upgrade must be a safe no-op
        await using (var secondUpgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(secondUpgradeContext);
        }

        // Assert — schema unchanged; the legacy row reads through EF as an
        // Audio source (source_type DEFAULT 0) with no transcript
        await using (var readContext = factory.CreateDbContext())
        {
            var connection = readContext.Database.GetDbConnection();
            await connection.OpenAsync();
            var columns = await GetColumnNamesAsync(connection, "recording_queue");
            columns.Should().BeEquivalentTo(ExpectedRecordingQueueColumns);

            var legacy = await readContext.RecordingQueues.AsNoTracking().SingleAsync();
            legacy.QueueId.Should().Be("legacy-1");
            legacy.SourceType.Should().Be(QueueSourceType.Audio);
            legacy.SourceLabel.Should().BeNull();
            legacy.Transcript.Should().BeNull();
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_UpgradedDatabase_SupportsTextSourceWritesThroughContext()
    {
        // Arrange — old-shape database, then upgrade
        var factory = CreateFactory();
        await using (var setupContext = factory.CreateDbContext())
        {
            var connection = setupContext.Database.GetDbConnection();
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                """
                CREATE TABLE "recording_queue" (
                    "queue_id" TEXT NOT NULL CONSTRAINT "PK_recording_queue" PRIMARY KEY,
                    "audio_file_path" TEXT NOT NULL,
                    "status" TEXT NOT NULL,
                    "duration_seconds" REAL NULL,
                    "file_size_bytes" INTEGER NULL,
                    "created_at" TEXT NOT NULL,
                    "processed_at" TEXT NULL,
                    "error_message" TEXT NULL
                );
                """);
        }

        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Act — the upgraded schema must accept an EF write of a Text source
        await using (var writeContext = factory.CreateDbContext())
        {
            writeContext.RecordingQueues.Add(new RecordingQueue
            {
                QueueId = "text-1",
                SourceType = QueueSourceType.Text,
                SourceLabel = "Journal 2014-03",
                Transcript = "Pasted journal text",
                AudioFilePath = string.Empty,
                Status = QueueStatus.Pending,
                FileSizeBytes = 19,
                CreatedAt = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc)
            });
            await writeContext.SaveChangesAsync();
        }

        // Assert — round-trips intact through a fresh context
        await using (var readContext = factory.CreateDbContext())
        {
            var stored = await readContext.RecordingQueues.AsNoTracking().SingleAsync();
            stored.QueueId.Should().Be("text-1");
            stored.SourceType.Should().Be(QueueSourceType.Text);
            stored.SourceLabel.Should().Be("Journal 2014-03");
            stored.Transcript.Should().Be("Pasted journal text");
            stored.AudioFilePath.Should().Be(string.Empty);
            stored.DurationSeconds.Should().BeNull();
        }
    }

    // ---- SQLite helpers ----

    private TestDbContextFactory CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"TextSourceSchemaTests_{Guid.NewGuid()}.db");
        var factory = TestDbContextFactory.CreateSqliteFile(databasePath);
        _databases.Add((factory, databasePath));
        return factory;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
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
    }
}
