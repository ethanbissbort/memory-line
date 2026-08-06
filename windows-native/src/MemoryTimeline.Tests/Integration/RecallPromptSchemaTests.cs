using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Tests;
using Xunit;

namespace MemoryTimeline.Tests.Integration;

/// <summary>
/// Integration tests for the 2026-08 guided-recall schema upgrade against a
/// real file-based SQLite database: an old-shape database (no recall_prompts
/// table) is built via raw DDL, upgraded with
/// <see cref="SchemaUpgrader.EnsureSchemaAsync"/>, and inspected via PRAGMA /
/// sqlite_master.
/// </summary>
public class RecallPromptSchemaTests : IDisposable
{
    private static readonly string[] ExpectedRecallPromptColumns =
    {
        "prompt_id", "kind", "question", "anchor_start", "anchor_end",
        "entity_id", "entity_name", "status", "dismiss_reason",
        "snoozed_until", "queue_id", "created_at", "updated_at"
    };

    private readonly List<(TestDbContextFactory Factory, string DatabasePath)> _databases = new();

    [Fact]
    public async Task EnsureSchemaAsync_OldShapeDatabase_CreatesRecallPromptsTable_Idempotently()
    {
        // Arrange - build an OLD-shape database: one legacy table so
        // EnsureCreated is a no-op, and no recall_prompts table at all
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

        // Act - run the upgrade
        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Assert - the table exists with the expected columns and indexes
        await using (var verifyContext = factory.CreateDbContext())
        {
            var connection = verifyContext.Database.GetDbConnection();
            await connection.OpenAsync();

            var columns = await GetColumnNamesAsync(connection, "recall_prompts");
            columns.Should().BeEquivalentTo(ExpectedRecallPromptColumns);

            var indexes = await GetNamesAsync(connection,
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='recall_prompts'");
            indexes.Should().Contain("IX_recall_prompts_status");
            indexes.Should().Contain("IX_recall_prompts_kind_entity_id");
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
            var columns = await GetColumnNamesAsync(connection, "recall_prompts");
            columns.Should().BeEquivalentTo(ExpectedRecallPromptColumns);
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_UpgradedDatabase_SupportsRecallPromptWritesThroughContext()
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

        // Act - the upgraded schema must accept an EF write of a prompt
        var snooze = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
        await using (var writeContext = factory.CreateDbContext())
        {
            writeContext.RecallPrompts.Add(new RecallPrompt
            {
                PromptId = "prompt-1",
                Kind = RecallPromptKind.DensityGap,
                Question = "What were you doing between 2003 and 2006?",
                AnchorStart = new DateTime(2003, 1, 1),
                AnchorEnd = new DateTime(2006, 12, 31),
                EntityName = "2003-2006",
                Status = RecallPromptStatus.Active,
                DismissReason = DismissReason.Later,
                SnoozedUntil = snooze,
                QueueId = "queue-1"
            });
            await writeContext.SaveChangesAsync();
        }

        // Assert - round-trips intact (enums as INTEGER, dates as TEXT)
        await using (var readContext = factory.CreateDbContext())
        {
            var stored = await readContext.RecallPrompts.AsNoTracking().SingleAsync();
            stored.PromptId.Should().Be("prompt-1");
            stored.Kind.Should().Be(RecallPromptKind.DensityGap);
            stored.Question.Should().Be("What were you doing between 2003 and 2006?");
            stored.AnchorStart.Should().Be(new DateTime(2003, 1, 1));
            stored.AnchorEnd.Should().Be(new DateTime(2006, 12, 31));
            stored.EntityId.Should().BeNull();
            stored.EntityName.Should().Be("2003-2006");
            stored.Status.Should().Be(RecallPromptStatus.Active);
            stored.DismissReason.Should().Be(DismissReason.Later);
            stored.SnoozedUntil.Should().Be(snooze);
            stored.QueueId.Should().Be("queue-1");
        }
    }

    // ---- SQLite helpers ----

    private TestDbContextFactory CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"RecallPromptSchemaTests_{Guid.NewGuid()}.db");
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
