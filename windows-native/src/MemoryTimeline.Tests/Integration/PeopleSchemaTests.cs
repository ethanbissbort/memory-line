using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Tests;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.Integration;

/// <summary>
/// Integration tests for the 2026-08 people/drafts schema upgrade against a
/// real file-based SQLite database (see <see cref="TestDbContextFactory"/>):
/// an old-shape database (pre-contact-book people table, no drafts table) is
/// built via raw DDL, upgraded with <see cref="SchemaUpgrader.EnsureSchemaAsync"/>,
/// and inspected via PRAGMA/sqlite_master.
/// </summary>
public class PeopleSchemaTests : IDisposable
{
    private static readonly string[] ExpectedPeopleColumns =
    {
        "person_id", "name", "created_at",
        "nickname", "relationship", "email", "phone", "birthday", "company",
        "notes", "photo_path", "avatar_color", "is_favorite", "first_met_date",
        "updated_at", "merged_into_id"
    };

    private readonly List<(TestDbContextFactory Factory, string DatabasePath)> _databases = new();

    [Fact]
    public async Task EnsureSchemaAsync_OldShapeDatabase_AddsPeopleColumnsAndDraftsTable_Idempotently()
    {
        // Arrange - build an OLD-shape database: a people table with only
        // person_id/name/created_at and no drafts table at all
        var factory = CreateFactory();
        await using (var setupContext = factory.CreateDbContext())
        {
            var connection = setupContext.Database.GetDbConnection();
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                """
                CREATE TABLE "people" (
                    "person_id" TEXT NOT NULL CONSTRAINT "PK_people" PRIMARY KEY,
                    "name" TEXT NOT NULL,
                    "created_at" TEXT NOT NULL
                );
                """);
            await ExecuteAsync(connection,
                "INSERT INTO \"people\" (\"person_id\", \"name\", \"created_at\") " +
                "VALUES ('legacy-1', 'Legacy Person', '2024-01-01 00:00:00');");
        }

        // Act - run the upgrade (EnsureCreated is a no-op because tables exist)
        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Assert - every new people column exists
        await using (var verifyContext = factory.CreateDbContext())
        {
            var connection = verifyContext.Database.GetDbConnection();
            await connection.OpenAsync();

            var peopleColumns = await GetColumnNamesAsync(connection, "people");
            peopleColumns.Should().BeEquivalentTo(ExpectedPeopleColumns);

            // The drafts table and its indexes exist
            var tables = await GetNamesAsync(connection,
                "SELECT name FROM sqlite_master WHERE type='table'");
            tables.Should().Contain("drafts");

            var draftColumns = await GetColumnNamesAsync(connection, "drafts");
            draftColumns.Should().BeEquivalentTo(new[]
            {
                "draft_id", "draft_type", "title", "payload_json", "created_at", "updated_at"
            });

            var indexes = await GetNamesAsync(connection,
                "SELECT name FROM sqlite_master WHERE type='index'");
            indexes.Should().Contain("IX_drafts_draft_type");
            indexes.Should().Contain("IX_drafts_updated_at");
            indexes.Should().Contain("IX_people_is_favorite");
            indexes.Should().Contain("IX_people_merged_into_id");

            // The person_aliases table (2026-08 F9) with its NOCASE-unique alias index
            tables.Should().Contain("person_aliases");
            var aliasColumns = await GetColumnNamesAsync(connection, "person_aliases");
            aliasColumns.Should().BeEquivalentTo(new[]
            {
                "alias_id", "person_id", "alias", "created_at"
            });
            indexes.Should().Contain("IX_person_aliases_alias");
            indexes.Should().Contain("IX_person_aliases_person_id");
        }

        // Act again - running the upgrade a second time must be a safe no-op
        await using (var secondUpgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(secondUpgradeContext);
        }

        // Assert - schema unchanged and the legacy row survived with defaults
        await using (var verifyContext = factory.CreateDbContext())
        {
            var connection = verifyContext.Database.GetDbConnection();
            await connection.OpenAsync();
            var peopleColumns = await GetColumnNamesAsync(connection, "people");
            peopleColumns.Should().BeEquivalentTo(ExpectedPeopleColumns);

            var legacy = await verifyContext.People.AsNoTracking().SingleAsync();
            legacy.PersonId.Should().Be("legacy-1");
            legacy.Name.Should().Be("Legacy Person");
            legacy.IsFavorite.Should().BeFalse();
            legacy.Nickname.Should().BeNull();
            legacy.Birthday.Should().BeNull();
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_CaseVariantPeople_MergesRowsAndRebuildsIndexNocase_Idempotently()
    {
        // Arrange - an old-shape database (from the previously shipped people
        // build: BINARY unique name index with contact columns already
        // present and populated) holding case-variant duplicates: "Sarah"
        // with 2 junction links (event-1, event-2) and "sarah" with 3
        // (event-2, event-3, event-1) - two shared events (event-1 and
        // event-2). "sarah" has the most junction links, so it must win
        // keeper selection even though "Sarah" was inserted first. "Sarah"
        // (the loser) is the row the user enriched - email, notes, favorite
        // flag - so the repair must backfill those onto the keeper before
        // deleting the loser row.
        var factory = CreateFactory();
        await using (var setupContext = factory.CreateDbContext())
        {
            var connection = setupContext.Database.GetDbConnection();
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                """
                CREATE TABLE "people" (
                    "person_id" TEXT NOT NULL CONSTRAINT "PK_people" PRIMARY KEY,
                    "name" TEXT NOT NULL,
                    "created_at" TEXT NOT NULL,
                    "email" TEXT NULL,
                    "notes" TEXT NULL,
                    "is_favorite" INTEGER NOT NULL DEFAULT 0
                );
                """);
            await ExecuteAsync(connection,
                "CREATE UNIQUE INDEX \"IX_people_name\" ON \"people\" (\"name\");");
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
                CREATE TABLE "event_people" (
                    "event_id" TEXT NOT NULL,
                    "person_id" TEXT NOT NULL,
                    "created_at" TEXT NOT NULL,
                    CONSTRAINT "PK_event_people" PRIMARY KEY ("event_id", "person_id"),
                    CONSTRAINT "FK_event_people_events_event_id" FOREIGN KEY ("event_id") REFERENCES "events" ("event_id") ON DELETE CASCADE,
                    CONSTRAINT "FK_event_people_people_person_id" FOREIGN KEY ("person_id") REFERENCES "people" ("person_id") ON DELETE CASCADE
                );
                """);
            await ExecuteAsync(connection,
                """
                INSERT INTO "people" ("person_id", "name", "created_at", "email", "notes", "is_favorite") VALUES
                    ('person-upper', 'Sarah', '2024-01-01 00:00:00', 'sarah@example.com', 'Met on the coast trail', 1),
                    ('person-lower', 'sarah', '2024-02-01 00:00:00', NULL, NULL, 0);
                INSERT INTO "events" ("event_id", "title", "start_date", "created_at", "updated_at") VALUES
                    ('event-1', 'Hike',   '2024-03-01 00:00:00', '2024-03-01 00:00:00', '2024-03-01 00:00:00'),
                    ('event-2', 'Dinner', '2024-04-01 00:00:00', '2024-04-01 00:00:00', '2024-04-01 00:00:00'),
                    ('event-3', 'Movie',  '2024-05-01 00:00:00', '2024-05-01 00:00:00', '2024-05-01 00:00:00');
                INSERT INTO "event_people" ("event_id", "person_id", "created_at") VALUES
                    ('event-1', 'person-upper', '2024-03-01 00:00:00'),
                    ('event-2', 'person-upper', '2024-04-01 00:00:00'),
                    ('event-2', 'person-lower', '2024-04-01 00:00:00'),
                    ('event-3', 'person-lower', '2024-05-01 00:00:00'),
                    ('event-1', 'person-lower', '2024-05-01 00:00:00');
                """);
        }

        var loggerMock = new Mock<ILogger>();

        // Act - run the upgrade (EnsureCreated is a no-op because tables exist)
        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext, loggerMock.Object);
        }

        // Assert - one survivor holding BOTH people's event associations
        await using (var verifyContext = factory.CreateDbContext())
        {
            var connection = verifyContext.Database.GetDbConnection();
            await connection.OpenAsync();

            // "sarah" (person-lower) has the most junction links, so it is the
            // keeper - even though "Sarah" was inserted first. The loser row
            // is gone.
            var names = await GetNamesAsync(connection,
                "SELECT name FROM \"people\" ORDER BY name");
            names.Should().Equal("sarah");

            // Every event association from both rows survived, deduplicated,
            // repointed at the keeper: event-1, event-2, event-3.
            var linkedEvents = await GetNamesAsync(connection,
                "SELECT event_id FROM \"event_people\" WHERE person_id = 'person-lower' ORDER BY event_id",
                "event_id");
            linkedEvents.Should().Equal("event-1", "event-2", "event-3");
            var orphanLinks = await GetNamesAsync(connection,
                "SELECT event_id FROM \"event_people\" WHERE person_id <> 'person-lower'",
                "event_id");
            orphanLinks.Should().BeEmpty();

            // The loser's spelling is kept as an alias of the keeper.
            var aliases = await GetNamesAsync(connection,
                "SELECT alias FROM \"person_aliases\" WHERE person_id = 'person-lower'",
                "alias");
            aliases.Should().Equal("Sarah");

            // The loser's user-entered contact data was backfilled onto the
            // keeper before the delete (exactly what MergeAsync would have
            // preserved): empty columns take the loser's values and the
            // favorite flag ORs in.
            var survivor = await verifyContext.People.AsNoTracking().SingleAsync();
            survivor.PersonId.Should().Be("person-lower");
            survivor.Email.Should().Be("sarah@example.com");
            survivor.Notes.Should().Be("Met on the coast trail");
            survivor.IsFavorite.Should().BeTrue();

            // The unique name index was rebuilt with COLLATE NOCASE...
            var indexSql = await GetNamesAsync(connection,
                "SELECT sql FROM sqlite_master WHERE type='index' AND name='IX_people_name'",
                "sql");
            indexSql.Should().ContainSingle().Which.Should().Contain("COLLATE NOCASE");

            // ...and actually rejects case-variant duplicates now.
            var insertVariant = async () => await ExecuteAsync(connection,
                "INSERT INTO \"people\" (\"person_id\", \"name\", \"created_at\") " +
                "VALUES ('person-dupe', 'SARAH', '2024-06-01 00:00:00');");
            await insertVariant.Should().ThrowAsync<SqliteException>();
        }

        // The merge was logged with names and ids.
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("merged case-variant")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        // Act again - a second run must be a safe no-op
        await using (var secondUpgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(secondUpgradeContext);
        }

        // Assert - still exactly one sarah, three links, one alias, and the
        // backfilled contact data untouched
        await using (var reverifyContext = factory.CreateDbContext())
        {
            var connection = reverifyContext.Database.GetDbConnection();
            await connection.OpenAsync();

            (await GetNamesAsync(connection, "SELECT name FROM \"people\""))
                .Should().Equal("sarah");
            (await GetNamesAsync(connection,
                "SELECT event_id FROM \"event_people\" ORDER BY event_id", "event_id"))
                .Should().Equal("event-1", "event-2", "event-3");
            (await GetNamesAsync(connection,
                "SELECT alias FROM \"person_aliases\"", "alias"))
                .Should().Equal("Sarah");

            var survivor = await reverifyContext.People.AsNoTracking().SingleAsync();
            survivor.Email.Should().Be("sarah@example.com");
            survivor.Notes.Should().Be("Met on the coast trail");
            survivor.IsFavorite.Should().BeTrue();
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_UpgradedDatabase_SupportsDraftAndPersonWritesThroughContext()
    {
        // Arrange - old-shape database, then upgrade
        var factory = CreateFactory();
        await using (var setupContext = factory.CreateDbContext())
        {
            var connection = setupContext.Database.GetDbConnection();
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                """
                CREATE TABLE "people" (
                    "person_id" TEXT NOT NULL CONSTRAINT "PK_people" PRIMARY KEY,
                    "name" TEXT NOT NULL,
                    "created_at" TEXT NOT NULL
                );
                """);
        }

        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Act - the upgraded schema must accept EF writes to both entities
        await using (var writeContext = factory.CreateDbContext())
        {
            writeContext.Drafts.Add(new Draft
            {
                DraftId = "draft-1",
                DraftType = DraftTypes.Event,
                Title = "Upgraded draft",
                PayloadJson = """{"Title":"Upgraded draft"}""",
                CreatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            writeContext.People.Add(new Person
            {
                PersonId = "person-1",
                Name = "Post Upgrade",
                Nickname = "Posty",
                IsFavorite = true
            });
            await writeContext.SaveChangesAsync();
        }

        // Assert
        await using (var readContext = factory.CreateDbContext())
        {
            var draft = await readContext.Drafts.AsNoTracking().SingleAsync();
            draft.Title.Should().Be("Upgraded draft");

            var person = await readContext.People.AsNoTracking()
                .SingleAsync(p => p.PersonId == "person-1");
            person.Nickname.Should().Be("Posty");
            person.IsFavorite.Should().BeTrue();
        }
    }

    [Fact]
    public async Task FreshDatabase_CanInsertAndReadDraftAndFullyPopulatedPerson()
    {
        // Arrange - brand-new database created by EnsureCreated (full model)
        var factory = CreateFactory();
        await using (var createContext = factory.CreateDbContext())
        {
            await createContext.Database.EnsureCreatedAsync();
        }

        var draft = new Draft
        {
            DraftId = "draft-fresh",
            DraftType = DraftTypes.Person,
            Title = "Person draft",
            PayloadJson = """{"Name":"Draft Person"}""",
            CreatedAt = new DateTime(2024, 5, 1, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 5, 2, 11, 45, 0, DateTimeKind.Utc)
        };
        var person = new Person
        {
            PersonId = "person-fresh",
            Name = "Ada Lovelace",
            Nickname = "Ada",
            Relationship = "colleague",
            Email = "ada@example.com",
            Phone = "555-0142",
            Birthday = new DateTime(1815, 12, 10),
            Company = "Analytical Engines Ltd",
            Notes = "First programmer",
            PhotoPath = @"C:\photos\ada.png",
            AvatarColor = "#8A66C2",
            IsFavorite = true,
            FirstMetDate = new DateTime(2020, 1, 15),
            CreatedAt = new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 4, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        // Act
        await using (var writeContext = factory.CreateDbContext())
        {
            writeContext.Drafts.Add(draft);
            writeContext.People.Add(person);
            await writeContext.SaveChangesAsync();
        }

        // Assert - read back through a fresh context
        await using var readContext = factory.CreateDbContext();

        var storedDraft = await readContext.Drafts.AsNoTracking().SingleAsync();
        storedDraft.DraftId.Should().Be("draft-fresh");
        storedDraft.DraftType.Should().Be(DraftTypes.Person);
        storedDraft.Title.Should().Be("Person draft");
        storedDraft.PayloadJson.Should().Be("""{"Name":"Draft Person"}""");
        storedDraft.CreatedAt.Should().Be(new DateTime(2024, 5, 1, 10, 30, 0, DateTimeKind.Utc));
        storedDraft.UpdatedAt.Should().Be(new DateTime(2024, 5, 2, 11, 45, 0, DateTimeKind.Utc));

        var storedPerson = await readContext.People.AsNoTracking().SingleAsync();
        storedPerson.Name.Should().Be("Ada Lovelace");
        storedPerson.Nickname.Should().Be("Ada");
        storedPerson.Relationship.Should().Be("colleague");
        storedPerson.Email.Should().Be("ada@example.com");
        storedPerson.Phone.Should().Be("555-0142");
        storedPerson.Birthday.Should().Be(new DateTime(1815, 12, 10));
        storedPerson.Company.Should().Be("Analytical Engines Ltd");
        storedPerson.Notes.Should().Be("First programmer");
        storedPerson.PhotoPath.Should().Be(@"C:\photos\ada.png");
        storedPerson.AvatarColor.Should().Be("#8A66C2");
        storedPerson.IsFavorite.Should().BeTrue();
        storedPerson.FirstMetDate.Should().Be(new DateTime(2020, 1, 15));
    }

    // ---- SQLite helpers ----

    private TestDbContextFactory CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"PeopleSchemaTests_{Guid.NewGuid()}.db");
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
        return await GetNamesAsync(connection, $"PRAGMA table_info(\"{tableName}\")", "name");
    }

    private static async Task<List<string>> GetNamesAsync(
        DbConnection connection, string sql, string columnName = "name")
    {
        var names = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var ordinal = reader.GetOrdinal(columnName);
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
