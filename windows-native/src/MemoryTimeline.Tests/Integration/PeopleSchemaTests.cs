using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Tests;
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
        "updated_at"
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
