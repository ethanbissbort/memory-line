using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Tests;
using Xunit;

namespace MemoryTimeline.Tests.Integration;

/// <summary>
/// Integration tests for the date-precision schema upgrade against a real
/// file-based SQLite database (following the PeopleSchemaTests pattern): an
/// old-shape database whose events/pending_events tables predate the
/// date_precision/earliest_possible/latest_possible columns is built via raw
/// DDL, upgraded with <see cref="SchemaUpgrader.EnsureSchemaAsync"/>, and
/// inspected via PRAGMA plus EF reads.
/// </summary>
public class DatePrecisionSchemaTests : IDisposable
{
    private static readonly string[] NewColumns =
    {
        "date_precision", "earliest_possible", "latest_possible"
    };

    private readonly List<(TestDbContextFactory Factory, string DatabasePath)> _databases = new();

    [Fact]
    public async Task EnsureSchemaAsync_OldShapeDatabase_AddsPrecisionColumns_AndLegacyRowsReadAsDay()
    {
        // Arrange - OLD-shape events/pending_events tables (no precision
        // columns; events also predates location/confidence) with legacy rows
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

            await ExecuteAsync(connection,
                """
                CREATE TABLE "pending_events" (
                    "pending_id" TEXT NOT NULL CONSTRAINT "PK_pending_events" PRIMARY KEY,
                    "extracted_data" TEXT NOT NULL,
                    "audio_file_path" TEXT NULL,
                    "transcript" TEXT NULL,
                    "queue_id" TEXT NULL,
                    "status" TEXT NOT NULL,
                    "created_at" TEXT NOT NULL,
                    "reviewed_at" TEXT NULL,
                    "title" TEXT NOT NULL,
                    "description" TEXT NULL,
                    "start_date" TEXT NOT NULL,
                    "end_date" TEXT NULL,
                    "category" TEXT NOT NULL,
                    "confidence_score" REAL NOT NULL,
                    "is_approved" INTEGER NOT NULL
                );
                """);
            await ExecuteAsync(connection,
                "INSERT INTO \"pending_events\" (\"pending_id\", \"extracted_data\", \"status\", \"created_at\", \"title\", \"start_date\", \"category\", \"confidence_score\", \"is_approved\") " +
                "VALUES ('legacy-pending', '{}', 'pending_review', '2024-01-01 00:00:00', 'Legacy Pending', '2012-03-14 00:00:00', 'other', 0.5, 0);");
        }

        // Act - run the upgrade (EnsureCreated is a no-op because tables exist)
        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Assert - both tables gained every new column
        await using (var verifyContext = factory.CreateDbContext())
        {
            var connection = verifyContext.Database.GetDbConnection();
            await connection.OpenAsync();

            var eventColumns = await GetColumnNamesAsync(connection, "events");
            eventColumns.Should().Contain(NewColumns);
            eventColumns.Should().Contain(new[] { "location", "confidence" });

            var pendingColumns = await GetColumnNamesAsync(connection, "pending_events");
            pendingColumns.Should().Contain(NewColumns);
        }

        // Assert - legacy rows read through EF with Day precision and no bounds
        await using (var readContext = factory.CreateDbContext())
        {
            var legacyEvent = await readContext.Events.AsNoTracking().SingleAsync();
            legacyEvent.EventId.Should().Be("legacy-event");
            legacyEvent.DatePrecision.Should().Be(DatePrecision.Day);
            legacyEvent.EarliestPossible.Should().BeNull();
            legacyEvent.LatestPossible.Should().BeNull();

            var legacyPending = await readContext.PendingEvents.AsNoTracking().SingleAsync();
            legacyPending.PendingId.Should().Be("legacy-pending");
            legacyPending.DatePrecision.Should().Be(DatePrecision.Day);
            legacyPending.EarliestPossible.Should().BeNull();
            legacyPending.LatestPossible.Should().BeNull();
        }

        // Act again - the upgrade must be an idempotent no-op the second time
        await using (var secondUpgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(secondUpgradeContext);
        }

        // Assert - schema unchanged and rows still readable
        await using (var finalContext = factory.CreateDbContext())
        {
            var connection = finalContext.Database.GetDbConnection();
            await connection.OpenAsync();
            var eventColumns = await GetColumnNamesAsync(connection, "events");
            eventColumns.Should().OnlyHaveUniqueItems();
            eventColumns.Should().Contain(NewColumns);

            var legacyEvent = await finalContext.Events.AsNoTracking().SingleAsync();
            legacyEvent.DatePrecision.Should().Be(DatePrecision.Day);
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_UpgradedDatabase_PersistsCoarsePrecisionThroughContext()
    {
        // Arrange - old-shape events table, then upgrade
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
        }

        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Act - the upgraded schema must accept a coarse-precision write
        await using (var writeContext = factory.CreateDbContext())
        {
            writeContext.Events.Add(new Event
            {
                EventId = "denver-move",
                Title = "Moved to Denver",
                StartDate = new DateTime(2011, 7, 1),
                DatePrecision = DatePrecision.Year,
                EarliestPossible = new DateTime(2011, 1, 1),
                LatestPossible = new DateTime(2011, 12, 31),
                Category = "milestone"
            });
            await writeContext.SaveChangesAsync();
        }

        // Assert
        await using var readContext = factory.CreateDbContext();
        var stored = await readContext.Events.AsNoTracking()
            .SingleAsync(e => e.EventId == "denver-move");
        stored.DatePrecision.Should().Be(DatePrecision.Year);
        stored.EarliestPossible.Should().Be(new DateTime(2011, 1, 1));
        stored.LatestPossible.Should().Be(new DateTime(2011, 12, 31));
    }

    // ---- SQLite helpers ----

    private TestDbContextFactory CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"DatePrecisionSchemaTests_{Guid.NewGuid()}.db");
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
