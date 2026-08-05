using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Tests;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.Services;

/// <summary>
/// Tests for <see cref="PersonService"/>. A file-based SQLite database is used
/// (rather than EF InMemory) because MergePersonsAsync opens a relational
/// transaction and the tests should exercise the real people/event_people
/// schema, unique name index, and cascade behavior.
/// </summary>
public class PersonServiceTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly IPersonService _personService;
    private readonly Mock<ILogger<PersonService>> _loggerMock;
    private readonly string _databasePath;

    public PersonServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"PersonServiceTests_{Guid.NewGuid()}.db");
        _contextFactory = TestDbContextFactory.CreateSqliteFile(_databasePath);

        using (var context = _contextFactory.CreateDbContext())
        {
            context.Database.EnsureCreated();
        }

        _loggerMock = new Mock<ILogger<PersonService>>();
        _personService = new PersonService(_contextFactory, _loggerMock.Object);
    }

    // ---- CRUD round trip ----

    [Fact]
    public async Task CreatePersonAsync_ThenGetPersonAsync_RoundTripsAllContactFields()
    {
        // Arrange
        var dto = new PersonDto
        {
            Name = "Alice Smith",
            Nickname = "Ali",
            Relationship = "sister",
            Email = "alice@example.com",
            Phone = "+1 555 0100",
            Birthday = new DateTime(1990, 5, 4),
            Company = "Contoso",
            Notes = "Met at the lake house",
            PhotoPath = @"C:\photos\alice.jpg",
            AvatarColor = "#4A8FB5",
            IsFavorite = true,
            FirstMetDate = new DateTime(2005, 8, 20)
        };

        // Act
        var created = await _personService.CreatePersonAsync(dto);
        var fetched = await _personService.GetPersonAsync(created.PersonId);

        // Assert
        created.PersonId.Should().NotBeNullOrEmpty();
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Alice Smith");
        fetched.Nickname.Should().Be("Ali");
        fetched.Relationship.Should().Be("sister");
        fetched.Email.Should().Be("alice@example.com");
        fetched.Phone.Should().Be("+1 555 0100");
        fetched.Birthday.Should().Be(new DateTime(1990, 5, 4));
        fetched.Company.Should().Be("Contoso");
        fetched.Notes.Should().Be("Met at the lake house");
        fetched.PhotoPath.Should().Be(@"C:\photos\alice.jpg");
        fetched.AvatarColor.Should().Be("#4A8FB5");
        fetched.IsFavorite.Should().BeTrue();
        fetched.FirstMetDate.Should().Be(new DateTime(2005, 8, 20));
        fetched.EventCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdatePersonAsync_ChangesEditableFields_PersistsChanges()
    {
        // Arrange
        var created = await _personService.CreatePersonAsync(new PersonDto
        {
            Name = "Bruce Banner",
            Email = "old@example.com"
        });

        // Act
        created.Name = "Robert Banner";
        created.Email = "new@example.com";
        created.Company = "Gamma Labs";
        created.Birthday = new DateTime(1969, 12, 18);
        var updated = await _personService.UpdatePersonAsync(created);
        var fetched = await _personService.GetPersonAsync(created.PersonId);

        // Assert
        updated.Name.Should().Be("Robert Banner");
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Robert Banner");
        fetched.Email.Should().Be("new@example.com");
        fetched.Company.Should().Be("Gamma Labs");
        fetched.Birthday.Should().Be(new DateTime(1969, 12, 18));
    }

    [Fact]
    public async Task DeletePersonAsync_RemovesPersonAndCascadesJunctionRows()
    {
        // Arrange
        var person = await _personService.CreatePersonAsync(new PersonDto { Name = "Charlie Day" });
        var evt = await SeedEventAsync("Party", new DateTime(2024, 3, 1));
        await SeedLinkAsync(evt.EventId, person.PersonId);

        // Act
        await _personService.DeletePersonAsync(person.PersonId);

        // Assert
        (await _personService.GetPersonAsync(person.PersonId)).Should().BeNull();
        await using var verifyContext = _contextFactory.CreateDbContext();
        (await verifyContext.EventPeople.AsNoTracking().ToListAsync()).Should().BeEmpty();
        (await verifyContext.Events.AsNoTracking().ToListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task CreatePersonAsync_DuplicateNameCaseInsensitive_ThrowsInvalidOperationException()
    {
        // Arrange
        await _personService.CreatePersonAsync(new PersonDto { Name = "Bob Jones" });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _personService.CreatePersonAsync(new PersonDto { Name = "bob jones" }));
    }

    // ---- Search ----

    [Fact]
    public async Task SearchPersonsAsync_MatchesNameNicknameEmailCompanyAndRelationship()
    {
        // Arrange
        await _personService.CreatePersonAsync(new PersonDto
        {
            Name = "Carol Danvers",
            Nickname = "Cap",
            Email = "carol@starforce.example",
            Company = "Starforce",
            Relationship = "friend"
        });
        await _personService.CreatePersonAsync(new PersonDto { Name = "Wilhelm Zed" });

        // Act & Assert - each searched field matches, case-insensitively
        (await _personService.SearchPersonsAsync("carol")).Should().ContainSingle(p => p.Name == "Carol Danvers");
        (await _personService.SearchPersonsAsync("CAP")).Should().ContainSingle(p => p.Name == "Carol Danvers");
        (await _personService.SearchPersonsAsync("@starforce.example")).Should().ContainSingle(p => p.Name == "Carol Danvers");
        (await _personService.SearchPersonsAsync("starforce")).Should().ContainSingle(p => p.Name == "Carol Danvers");
        (await _personService.SearchPersonsAsync("friend")).Should().ContainSingle(p => p.Name == "Carol Danvers");

        // Non-matching term finds nothing
        (await _personService.SearchPersonsAsync("nomatchterm")).Should().BeEmpty();
    }

    [Fact]
    public async Task SearchPersonsAsync_DoesNotSearchNotes()
    {
        // Arrange
        await _personService.CreatePersonAsync(new PersonDto
        {
            Name = "Pietro Frost",
            Notes = "quicksilver runner"
        });

        // Act
        var results = await _personService.SearchPersonsAsync("quicksilver");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchPersonsAsync_WhitespaceTerm_ReturnsAllPersons()
    {
        // Arrange
        await _personService.CreatePersonAsync(new PersonDto { Name = "Anna One" });
        await _personService.CreatePersonAsync(new PersonDto { Name = "Ben Two" });

        // Act
        var results = await _personService.SearchPersonsAsync("   ");

        // Assert
        results.Should().HaveCount(2);
    }

    // ---- GetAllPersonsAsync sorting ----

    [Fact]
    public async Task GetAllPersonsAsync_NameSort_FavoritesFirstThenAlphabetical()
    {
        // Arrange
        await SeedSortFixtureAsync();

        // Act
        var favoritesFirst = await _personService.GetAllPersonsAsync(PersonSortOption.Name, favoritesFirst: true);
        var plainAlphabetical = await _personService.GetAllPersonsAsync(PersonSortOption.Name, favoritesFirst: false);

        // Assert - Ben is the only favorite
        favoritesFirst.Select(p => p.Name).Should().Equal("Ben", "Anna", "Cara");
        plainAlphabetical.Select(p => p.Name).Should().Equal("Anna", "Ben", "Cara");
    }

    [Fact]
    public async Task GetAllPersonsAsync_RecentlyAddedSort_OrdersByCreatedAtDescending()
    {
        // Arrange
        await SeedSortFixtureAsync();

        // Act
        var results = await _personService.GetAllPersonsAsync(PersonSortOption.RecentlyAdded, favoritesFirst: false);

        // Assert - CreatedAt: Ben 2024-03, Cara 2024-02, Anna 2024-01
        results.Select(p => p.Name).Should().Equal("Ben", "Cara", "Anna");
    }

    [Fact]
    public async Task GetAllPersonsAsync_RecentlyUpdatedSort_OrdersByUpdatedAtDescending()
    {
        // Arrange
        await SeedSortFixtureAsync();

        // Act
        var results = await _personService.GetAllPersonsAsync(PersonSortOption.RecentlyUpdated, favoritesFirst: false);

        // Assert - UpdatedAt: Anna 2024-06, Cara 2024-05, Ben 2024-04
        results.Select(p => p.Name).Should().Equal("Anna", "Cara", "Ben");
    }

    [Fact]
    public async Task GetAllPersonsAsync_MostEventsSort_OrdersByEventCountAndPopulatesCounts()
    {
        // Arrange
        await SeedSortFixtureAsync();
        var people = await _personService.GetAllPersonsAsync(PersonSortOption.Name, favoritesFirst: false);
        var anna = people.Single(p => p.Name == "Anna");
        var cara = people.Single(p => p.Name == "Cara");

        var e1 = await SeedEventAsync("Event 1", new DateTime(2023, 1, 1));
        var e2 = await SeedEventAsync("Event 2", new DateTime(2023, 2, 1));
        await SeedLinkAsync(e1.EventId, cara.PersonId);
        await SeedLinkAsync(e2.EventId, cara.PersonId);
        await SeedLinkAsync(e1.EventId, anna.PersonId);

        // Act
        var results = await _personService.GetAllPersonsAsync(PersonSortOption.MostEvents, favoritesFirst: false);

        // Assert - Cara 2 events, Anna 1, Ben 0; counts are populated on the DTOs
        results.Select(p => p.Name).Should().Equal("Cara", "Anna", "Ben");
        results.Select(p => p.EventCount).Should().Equal(2, 1, 0);
    }

    // ---- Favorites ----

    [Fact]
    public async Task ToggleFavoriteAsync_FlipsStateAndReturnsNewValue()
    {
        // Arrange
        var person = await _personService.CreatePersonAsync(new PersonDto { Name = "Diana Prince" });
        person.IsFavorite.Should().BeFalse();

        // Act
        var afterFirstToggle = await _personService.ToggleFavoriteAsync(person.PersonId);
        var fetchedOn = await _personService.GetPersonAsync(person.PersonId);
        var afterSecondToggle = await _personService.ToggleFavoriteAsync(person.PersonId);
        var fetchedOff = await _personService.GetPersonAsync(person.PersonId);

        // Assert
        afterFirstToggle.Should().BeTrue();
        fetchedOn!.IsFavorite.Should().BeTrue();
        afterSecondToggle.Should().BeFalse();
        fetchedOff!.IsFavorite.Should().BeFalse();
    }

    // ---- Events for person ----

    [Fact]
    public async Task GetEventsForPersonAsync_ReturnsSummariesNewestStartDateFirst()
    {
        // Arrange
        var person = await _personService.CreatePersonAsync(new PersonDto { Name = "Ed Norton" });
        var oldest = await SeedEventAsync("Oldest", new DateTime(2022, 6, 15), EventCategory.Travel);
        var middle = await SeedEventAsync("Middle", new DateTime(2023, 1, 1), EventCategory.Work,
            endDate: new DateTime(2023, 1, 5));
        var newest = await SeedEventAsync("Newest", new DateTime(2024, 1, 1), EventCategory.Milestone);
        await SeedLinkAsync(oldest.EventId, person.PersonId);
        await SeedLinkAsync(middle.EventId, person.PersonId);
        await SeedLinkAsync(newest.EventId, person.PersonId);

        // Act
        var summaries = await _personService.GetEventsForPersonAsync(person.PersonId);

        // Assert
        summaries.Select(s => s.Title).Should().Equal("Newest", "Middle", "Oldest");
        summaries[0].EventId.Should().Be(newest.EventId);
        summaries[0].StartDate.Should().Be(new DateTime(2024, 1, 1));
        summaries[0].Category.Should().Be(EventCategory.Milestone);
        summaries[1].EndDate.Should().Be(new DateTime(2023, 1, 5));
    }

    [Fact]
    public async Task GetPersonAsync_PopulatesEventCountAndFirstLastEventDates()
    {
        // Arrange
        var person = await _personService.CreatePersonAsync(new PersonDto { Name = "Fiona Glenanne" });
        var first = await SeedEventAsync("First", new DateTime(2020, 2, 2));
        var last = await SeedEventAsync("Last", new DateTime(2024, 9, 9));
        await SeedLinkAsync(first.EventId, person.PersonId);
        await SeedLinkAsync(last.EventId, person.PersonId);

        // Act
        var fetched = await _personService.GetPersonAsync(person.PersonId);

        // Assert
        fetched.Should().NotBeNull();
        fetched!.EventCount.Should().Be(2);
        fetched.FirstEventDate.Should().Be(new DateTime(2020, 2, 2));
        fetched.LastEventDate.Should().Be(new DateTime(2024, 9, 9));
    }

    // ---- Merge ----

    [Fact]
    public async Task MergePersonsAsync_RepointsJunctionsBackfillsFieldsAndDeletesSource()
    {
        // Arrange
        var source = await _personService.CreatePersonAsync(new PersonDto
        {
            Name = "Mike Old",
            Email = "mike@example.com",
            Company = "Acme",
            Notes = "Source notes",
            IsFavorite = true
        });
        var target = await _personService.CreatePersonAsync(new PersonDto
        {
            Name = "Michael New",
            Phone = "555-0100",
            Company = "TargetCo"
        });

        var e1 = await SeedEventAsync("Only source", new DateTime(2023, 1, 1));
        var e2 = await SeedEventAsync("Shared", new DateTime(2023, 2, 1));
        var e3 = await SeedEventAsync("Only target", new DateTime(2023, 3, 1));
        await SeedLinkAsync(e1.EventId, source.PersonId);
        await SeedLinkAsync(e2.EventId, source.PersonId);
        await SeedLinkAsync(e2.EventId, target.PersonId);
        await SeedLinkAsync(e3.EventId, target.PersonId);

        // Act
        await _personService.MergePersonsAsync(source.PersonId, target.PersonId);

        // Assert - source row is gone
        (await _personService.GetPersonAsync(source.PersonId)).Should().BeNull();

        // Junctions repointed; the shared event link was not duplicated
        await using var verifyContext = _contextFactory.CreateDbContext();
        var links = await verifyContext.EventPeople.AsNoTracking().ToListAsync();
        links.Should().OnlyContain(ep => ep.PersonId == target.PersonId);
        links.Select(ep => ep.EventId).Should().BeEquivalentTo(
            new[] { e1.EventId, e2.EventId, e3.EventId });

        // Missing contact fields backfilled from source; existing target values kept
        var merged = await _personService.GetPersonAsync(target.PersonId);
        merged.Should().NotBeNull();
        merged!.Email.Should().Be("mike@example.com");   // backfilled
        merged.Notes.Should().Be("Source notes");        // backfilled
        merged.Phone.Should().Be("555-0100");            // target's own value kept
        merged.Company.Should().Be("TargetCo");          // NOT overwritten by source
        merged.IsFavorite.Should().BeTrue();             // favorite flag carried over
        merged.EventCount.Should().Be(3);
    }

    // ---- Matching ----

    [Fact]
    public async Task FindBestMatchAsync_ExactNameCaseInsensitive_ReturnsExactMatch()
    {
        // Arrange
        var person = await _personService.CreatePersonAsync(new PersonDto { Name = "Henry Adams" });

        // Act
        var match = await _personService.FindBestMatchAsync("henry adams");

        // Assert
        match.Should().NotBeNull();
        match!.Kind.Should().Be(PersonMatchKind.Exact);
        match.Person.PersonId.Should().Be(person.PersonId);
    }

    [Fact]
    public async Task FindBestMatchAsync_ExactNickname_ReturnsNicknameMatch()
    {
        // Arrange
        var person = await _personService.CreatePersonAsync(new PersonDto
        {
            Name = "Katherine Bell",
            Nickname = "Kate"
        });

        // Act
        var match = await _personService.FindBestMatchAsync("kate");

        // Assert
        match.Should().NotBeNull();
        match!.Kind.Should().Be(PersonMatchKind.Nickname);
        match.Person.PersonId.Should().Be(person.PersonId);
    }

    [Fact]
    public async Task FindBestMatchAsync_SmallEditDistance_ReturnsFuzzyMatch()
    {
        // Arrange
        var person = await _personService.CreatePersonAsync(new PersonDto { Name = "Henry Adams" });

        // Act - one substitution away from "Henry Adams"
        var match = await _personService.FindBestMatchAsync("Henri Adams");

        // Assert
        match.Should().NotBeNull();
        match!.Kind.Should().Be(PersonMatchKind.Fuzzy);
        match.Person.PersonId.Should().Be(person.PersonId);
    }

    [Fact]
    public async Task FindBestMatchAsync_NothingPlausible_ReturnsNull()
    {
        // Arrange
        await _personService.CreatePersonAsync(new PersonDto { Name = "Henry Adams" });

        // Act
        var noMatch = await _personService.FindBestMatchAsync("Xqzwv Plmnr");
        var emptyName = await _personService.FindBestMatchAsync("   ");

        // Assert
        noMatch.Should().BeNull();
        emptyName.Should().BeNull();
    }

    // ---- Duplicates ----

    [Fact]
    public async Task FindPotentialDuplicatesAsync_FindsNearIdenticalNames()
    {
        // Arrange
        await _personService.CreatePersonAsync(new PersonDto { Name = "John Smith" });
        await _personService.CreatePersonAsync(new PersonDto { Name = "Jon Smith" });
        await _personService.CreatePersonAsync(new PersonDto { Name = "Completely Unrelated" });

        // Act
        var pairs = await _personService.FindPotentialDuplicatesAsync();

        // Assert
        pairs.Should().ContainSingle();
        var pair = pairs[0];
        new[] { pair.First.Name, pair.Second.Name }
            .Should().BeEquivalentTo(new[] { "John Smith", "Jon Smith" });
        pair.Reason.Should().NotBeNullOrWhiteSpace();
    }

    // ---- Seed helpers ----

    /// <summary>
    /// Seeds Anna/Ben/Cara directly through a context with explicit created/updated
    /// timestamps so sort assertions are deterministic (no wall-clock dependence).
    /// </summary>
    private async Task SeedSortFixtureAsync()
    {
        await using var context = _contextFactory.CreateDbContext();
        context.People.AddRange(
            new Person
            {
                Name = "Anna",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Person
            {
                Name = "Ben",
                IsFavorite = true,
                CreatedAt = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Person
            {
                Name = "Cara",
                CreatedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        await context.SaveChangesAsync();
    }

    private async Task<Event> SeedEventAsync(
        string title,
        DateTime startDate,
        string? category = null,
        DateTime? endDate = null)
    {
        var evt = new Event
        {
            EventId = Guid.NewGuid().ToString(),
            Title = title,
            StartDate = startDate,
            EndDate = endDate,
            Category = category ?? EventCategory.Other,
            CreatedAt = DateTime.UtcNow
        };

        await using var context = _contextFactory.CreateDbContext();
        context.Events.Add(evt);
        await context.SaveChangesAsync();
        return evt;
    }

    private async Task SeedLinkAsync(string eventId, string personId)
    {
        await using var context = _contextFactory.CreateDbContext();
        context.EventPeople.Add(new EventPerson
        {
            EventId = eventId,
            PersonId = personId,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    public void Dispose()
    {
        using (var context = _contextFactory.CreateDbContext())
        {
            context.Database.EnsureDeleted();
        }

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
