using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Tests;
using Xunit;

namespace MemoryTimeline.Tests.Integration;

/// <summary>
/// Integration tests for the resurfacing schema upgrade against a real
/// file-based SQLite database (following the DatePrecisionSchemaTests
/// pattern): an old-shape database whose events table predates the
/// last_viewed_at/view_count columns is built via raw DDL, upgraded with
/// <see cref="SchemaUpgrader.EnsureSchemaAsync"/>, and inspected via PRAGMA
/// plus EF reads. Also verifies the app_settings seed parity for the new
/// home/resurfacing keys.
/// </summary>
public class ResurfacingSchemaTests : IDisposable
{
    private static readonly string[] NewColumns = { "last_viewed_at", "view_count" };

    private readonly List<(TestDbContextFactory Factory, string DatabasePath)> _databases = new();

    [Fact]
    public async Task EnsureSchemaAsync_OldShapeDatabase_AddsViewColumns_AndLegacyRowsReadAsUnviewed()
    {
        // Arrange - OLD-shape events table (no view columns) with a legacy row
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

            // Old-shape app_settings with one user-changed row: the seed-parity
            // block must add the new keys without overwriting existing values.
            await ExecuteAsync(connection,
                """
                CREATE TABLE "app_settings" (
                    "setting_key" TEXT NOT NULL CONSTRAINT "PK_app_settings" PRIMARY KEY,
                    "setting_value" TEXT NOT NULL,
                    "updated_at" TEXT NOT NULL
                );
                """);
            await ExecuteAsync(connection,
                "INSERT INTO \"app_settings\" (\"setting_key\", \"setting_value\", \"updated_at\") " +
                "VALUES ('theme', 'dark', '2024-01-01 00:00:00');");
        }

        // Act - run the upgrade (EnsureCreated is a no-op because tables exist)
        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Assert - the events table gained the view columns
        await using (var verifyContext = factory.CreateDbContext())
        {
            var connection = verifyContext.Database.GetDbConnection();
            await connection.OpenAsync();

            var eventColumns = await GetColumnNamesAsync(connection, "events");
            eventColumns.Should().Contain(NewColumns);
        }

        // Assert - the legacy row reads through EF as never viewed
        await using (var readContext = factory.CreateDbContext())
        {
            var legacyEvent = await readContext.Events.AsNoTracking().SingleAsync();
            legacyEvent.EventId.Should().Be("legacy-event");
            legacyEvent.ViewCount.Should().Be(0);
            legacyEvent.LastViewedAt.Should().BeNull();
        }

        // Assert - seed parity: new home/resurfacing keys inserted, existing
        // values untouched, last_toast_date NOT seeded
        await using (var settingsContext = factory.CreateDbContext())
        {
            var settings = await settingsContext.AppSettings.AsNoTracking()
                .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue);

            settings.Should().ContainKey("home_is_default_page").WhoseValue.Should().Be("true");
            settings.Should().ContainKey("daily_toast_enabled").WhoseValue.Should().Be("true");
            settings.Should().ContainKey("weekly_digest_enabled").WhoseValue.Should().Be("true");
            settings["theme"].Should().Be("dark"); // INSERT OR IGNORE never overwrites
            settings.Should().NotContainKey("last_toast_date"); // absent = toast never shown
        }

        // Act again - the upgrade must be an idempotent no-op the second time
        await using (var secondUpgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(secondUpgradeContext);
        }

        // Assert - schema unchanged and the row still reads as unviewed
        await using (var finalContext = factory.CreateDbContext())
        {
            var connection = finalContext.Database.GetDbConnection();
            await connection.OpenAsync();
            var eventColumns = await GetColumnNamesAsync(connection, "events");
            eventColumns.Should().OnlyHaveUniqueItems();
            eventColumns.Should().Contain(NewColumns);

            var legacyEvent = await finalContext.Events.AsNoTracking().SingleAsync();
            legacyEvent.ViewCount.Should().Be(0);
            legacyEvent.LastViewedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_UpgradedDatabase_PersistsViewTrackingThroughContext()
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

        // Act - the upgraded schema must accept view-tracking writes
        var viewedAt = new DateTime(2026, 8, 5, 12, 0, 0);
        await using (var writeContext = factory.CreateDbContext())
        {
            writeContext.Events.Add(new Event
            {
                EventId = "viewed-event",
                Title = "Viewed Event",
                StartDate = new DateTime(2015, 3, 14),
                ViewCount = 3,
                LastViewedAt = viewedAt,
                Category = "other"
            });
            await writeContext.SaveChangesAsync();
        }

        // Assert
        await using var readContext = factory.CreateDbContext();
        var stored = await readContext.Events.AsNoTracking()
            .SingleAsync(e => e.EventId == "viewed-event");
        stored.ViewCount.Should().Be(3);
        stored.LastViewedAt.Should().Be(viewedAt);
    }

    // ---- SQLite helpers ----

    private TestDbContextFactory CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ResurfacingSchemaTests_{Guid.NewGuid()}.db");
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
