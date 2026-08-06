using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Tests;
using Xunit;

namespace MemoryTimeline.Tests.Integration;

/// <summary>
/// Integration tests for the 2026-08 F12 revision-history schema upgrade
/// against a real file-based SQLite database: an old-shape database (no
/// event_revisions table) is built via raw DDL, upgraded with
/// <see cref="SchemaUpgrader.EnsureSchemaAsync"/>, and inspected via PRAGMA /
/// sqlite_master. Also covers the backup/revision app_settings seed parity.
/// </summary>
public class EventRevisionSchemaTests : IDisposable
{
    private static readonly string[] ExpectedRevisionColumns =
    {
        "revision_id", "event_id", "revised_at", "revision_kind",
        "snapshot_json", "changed_fields_csv"
    };

    private readonly List<(TestDbContextFactory Factory, string DatabasePath)> _databases = new();

    [Fact]
    public async Task EnsureSchemaAsync_OldShapeDatabase_CreatesEventRevisionsTable_Idempotently()
    {
        // Arrange - build an OLD-shape database: legacy tables so
        // EnsureCreated is a no-op, and no event_revisions table at all
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
                    "created_at" TEXT NOT NULL,
                    "updated_at" TEXT NOT NULL
                );
                """);
            await ExecuteAsync(connection,
                """
                CREATE TABLE "app_settings" (
                    "setting_key" TEXT NOT NULL CONSTRAINT "PK_app_settings" PRIMARY KEY,
                    "setting_value" TEXT NOT NULL,
                    "updated_at" TEXT NOT NULL
                );
                """);
            // One user-changed row: seed parity must never overwrite it.
            await ExecuteAsync(connection,
                "INSERT INTO \"app_settings\" (\"setting_key\", \"setting_value\", \"updated_at\") " +
                "VALUES ('backup_schedule', 'daily', '2024-01-01 00:00:00');");
        }

        // Act - run the upgrade
        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Assert - the table exists with the expected columns and the
        // descending composite index
        await using (var verifyContext = factory.CreateDbContext())
        {
            var connection = verifyContext.Database.GetDbConnection();
            await connection.OpenAsync();

            var columns = await GetColumnNamesAsync(connection, "event_revisions");
            columns.Should().BeEquivalentTo(ExpectedRevisionColumns);

            var indexes = await GetNamesAsync(connection,
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='event_revisions'");
            indexes.Should().Contain("IX_event_revisions_event_id_revised_at");

            // Deliberately NO FK to events: history must outlive deletes.
            var foreignKeys = await GetNamesAsync(connection,
                "SELECT \"table\" FROM pragma_foreign_key_list('event_revisions')");
            foreignKeys.Should().BeEmpty();
        }

        // Assert - seed parity: the 4 new backup/revision keys inserted, the
        // user-changed row untouched, last_backup_at NOT seeded
        await using (var settingsContext = factory.CreateDbContext())
        {
            var settings = await settingsContext.AppSettings.AsNoTracking()
                .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue);

            settings.Should().ContainKey("backup_destination").WhoseValue.Should().Be("");
            settings.Should().ContainKey("backup_include_media").WhoseValue.Should().Be("true");
            settings.Should().ContainKey("revision_history_enabled").WhoseValue.Should().Be("true");
            settings["backup_schedule"].Should().Be("daily"); // INSERT OR IGNORE never overwrites
            settings.Should().NotContainKey("last_backup_at"); // absent = never backed up
        }

        // Act again - a second upgrade must be a safe no-op
        await using (var secondUpgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(secondUpgradeContext);
        }

        await using (var recheckContext = factory.CreateDbContext())
        {
            var connection = recheckContext.Database.GetDbConnection();
            await connection.OpenAsync();
            var columns = await GetColumnNamesAsync(connection, "event_revisions");
            columns.Should().BeEquivalentTo(ExpectedRevisionColumns);
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_UpgradedDatabase_SupportsRevisionWritesThroughContext()
    {
        // Arrange - old-shape database, then upgrade
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
                    "created_at" TEXT NOT NULL,
                    "updated_at" TEXT NOT NULL
                );
                """);
        }

        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Act - the upgraded schema must accept an EF write of a revision,
        // including one for an event id that has no events row (no FK)
        var revisedAt = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        await using (var writeContext = factory.CreateDbContext())
        {
            writeContext.EventRevisions.Add(new EventRevision
            {
                RevisionId = "rev-1",
                EventId = "event-that-never-existed",
                RevisedAt = revisedAt,
                RevisionKind = RevisionKind.Restored,
                SnapshotJson = "{\"eventId\":\"event-that-never-existed\"}",
                ChangedFieldsCsv = "title,end_date"
            });
            await writeContext.SaveChangesAsync();
        }

        // Assert - round-trips intact (enum as INTEGER, dates as TEXT)
        await using (var readContext = factory.CreateDbContext())
        {
            var stored = await readContext.EventRevisions.AsNoTracking().SingleAsync();
            stored.RevisionId.Should().Be("rev-1");
            stored.EventId.Should().Be("event-that-never-existed");
            stored.RevisedAt.Should().Be(revisedAt);
            stored.RevisionKind.Should().Be(RevisionKind.Restored);
            stored.SnapshotJson.Should().Contain("event-that-never-existed");
            stored.ChangedFieldsCsv.Should().Be("title,end_date");
        }
    }

    // ---- SQLite helpers ----

    private TestDbContextFactory CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"EventRevisionSchemaTests_{Guid.NewGuid()}.db");
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
