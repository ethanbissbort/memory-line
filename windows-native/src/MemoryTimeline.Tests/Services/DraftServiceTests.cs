using FluentAssertions;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Moq;
using System.Text.Json;
using Xunit;

namespace MemoryTimeline.Tests.Services;

/// <summary>
/// Tests for <see cref="DraftService"/> over a real <see cref="DraftRepository"/>
/// backed by the shared in-memory context factory, plus the
/// <see cref="DraftDto.GetPayload{T}"/> serialization round trips.
/// </summary>
public class DraftServiceTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly IDraftRepository _repository;
    private readonly IDraftService _draftService;
    private readonly Mock<ILogger<DraftService>> _loggerMock;

    public DraftServiceTests()
    {
        // Factory over a uniquely named in-memory database; every context
        // created from it shares the same store.
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _repository = new DraftRepository(_contextFactory);
        _loggerMock = new Mock<ILogger<DraftService>>();
        _draftService = new DraftService(_repository, _loggerMock.Object);
    }

    // ---- SaveDraftAsync ----

    [Fact]
    public async Task SaveDraftAsync_NullDraftId_CreatesNewDraft()
    {
        // Act
        var saved = await _draftService.SaveDraftAsync(DraftTypes.Event, "My party", """{"Title":"My party"}""");

        // Assert
        saved.DraftId.Should().NotBeNullOrEmpty();
        saved.DraftType.Should().Be(DraftTypes.Event);
        saved.Title.Should().Be("My party");
        saved.PayloadJson.Should().Be("""{"Title":"My party"}""");

        var fetched = await _draftService.GetDraftAsync(saved.DraftId);
        fetched.Should().NotBeNull();
        fetched!.Title.Should().Be("My party");
        (await _draftService.GetDraftCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SaveDraftAsync_ExistingDraftId_UpsertsInPlace()
    {
        // Arrange
        var original = await _draftService.SaveDraftAsync(DraftTypes.Era, "College years", """{"Name":"College"}""");

        // Act
        var updated = await _draftService.SaveDraftAsync(
            DraftTypes.Era, "University years", """{"Name":"University"}""", original.DraftId);

        // Assert - same draft was updated, not a second one created
        updated.DraftId.Should().Be(original.DraftId);
        updated.Title.Should().Be("University years");
        updated.PayloadJson.Should().Be("""{"Name":"University"}""");
        updated.CreatedAt.Should().Be(original.CreatedAt);
        (await _draftService.GetDraftCountAsync()).Should().Be(1);

        var fetched = await _draftService.GetDraftAsync(original.DraftId);
        fetched!.Title.Should().Be("University years");
    }

    [Fact]
    public async Task SaveDraftAsync_UnknownDraftId_CreatesNewDraftWithNewId()
    {
        // Act
        var saved = await _draftService.SaveDraftAsync(
            DraftTypes.Person, "Aunt May", "{}", draftId: "no-such-draft-id");

        // Assert - a fresh id is minted; the unknown id is not reused
        saved.DraftId.Should().NotBeNullOrEmpty();
        saved.DraftId.Should().NotBe("no-such-draft-id");
        (await _draftService.GetDraftCountAsync()).Should().Be(1);
        (await _draftService.GetDraftAsync("no-such-draft-id")).Should().BeNull();
    }

    [Theory]
    [InlineData("event", "Untitled event")]
    [InlineData("era", "Untitled era")]
    [InlineData("person", "Untitled person")]
    public async Task SaveDraftAsync_EmptyTitle_FallsBackToUntitledTypeDisplay(string draftType, string expectedTitle)
    {
        // Act
        var fromEmpty = await _draftService.SaveDraftAsync(draftType, "", "{}");
        var fromWhitespace = await _draftService.SaveDraftAsync(draftType, "   ", "{}");

        // Assert
        fromEmpty.Title.Should().Be(expectedTitle);
        fromWhitespace.Title.Should().Be(expectedTitle);
    }

    // ---- GetDraftsAsync ----

    [Fact]
    public async Task GetDraftsAsync_NoFilter_ReturnsAllOrderedByUpdatedAtDescending()
    {
        // Arrange - seed through the repository with explicit timestamps so
        // ordering is deterministic (added entities are not re-stamped on save)
        await SeedDraftAsync(DraftTypes.Event, "Oldest", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedDraftAsync(DraftTypes.Era, "Newest", new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedDraftAsync(DraftTypes.Person, "Middle", new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var drafts = await _draftService.GetDraftsAsync();

        // Assert
        drafts.Select(d => d.Title).Should().Equal("Newest", "Middle", "Oldest");
    }

    [Fact]
    public async Task GetDraftsAsync_TypeFilter_ReturnsOnlyThatTypeOrderedByUpdatedAtDescending()
    {
        // Arrange
        await SeedDraftAsync(DraftTypes.Event, "Event old", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedDraftAsync(DraftTypes.Event, "Event new", new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedDraftAsync(DraftTypes.Era, "Era draft", new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedDraftAsync(DraftTypes.Person, "Person draft", new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var eventDrafts = await _draftService.GetDraftsAsync(DraftTypes.Event);
        var personDrafts = await _draftService.GetDraftsAsync(DraftTypes.Person);

        // Assert
        eventDrafts.Select(d => d.Title).Should().Equal("Event new", "Event old");
        eventDrafts.Should().OnlyContain(d => d.DraftType == DraftTypes.Event);
        personDrafts.Select(d => d.Title).Should().Equal("Person draft");
    }

    // ---- DeleteDraftAsync / GetDraftCountAsync ----

    [Fact]
    public async Task DeleteDraftAsync_ExistingDraft_RemovesIt()
    {
        // Arrange
        var saved = await _draftService.SaveDraftAsync(DraftTypes.Event, "Doomed", "{}");

        // Act
        await _draftService.DeleteDraftAsync(saved.DraftId);

        // Assert
        (await _draftService.GetDraftAsync(saved.DraftId)).Should().BeNull();
        (await _draftService.GetDraftCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteDraftAsync_UnknownDraftId_IsIgnored()
    {
        // Arrange
        await _draftService.SaveDraftAsync(DraftTypes.Event, "Survivor", "{}");

        // Act - must not throw
        await _draftService.DeleteDraftAsync("no-such-draft-id");

        // Assert
        (await _draftService.GetDraftCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetDraftCountAsync_CountsAllTypes()
    {
        // Arrange
        (await _draftService.GetDraftCountAsync()).Should().Be(0);
        await _draftService.SaveDraftAsync(DraftTypes.Event, "One", "{}");
        await _draftService.SaveDraftAsync(DraftTypes.Era, "Two", "{}");
        await _draftService.SaveDraftAsync(DraftTypes.Person, "Three", "{}");

        // Act
        var count = await _draftService.GetDraftCountAsync();

        // Assert
        count.Should().Be(3);
    }

    // ---- Payload round trips ----

    [Fact]
    public async Task GetPayload_EventDraftPayload_RoundTripsThroughSaveAndFetch()
    {
        // Arrange
        var payload = new EventDraftPayload
        {
            Title = "Road trip",
            Description = "Two weeks on the coast",
            StartDate = new DateTime(2024, 7, 1),
            EndDate = new DateTime(2024, 7, 14),
            Category = "travel",
            EraId = "era-1",
            Location = "Highway 1",
            TagNames = new List<string> { "vacation", "driving" },
            PersonIds = new List<string> { "person-1" },
            PersonNames = new List<string> { "New Friend" }
        };

        // Act
        var saved = await _draftService.SaveDraftAsync(
            DraftTypes.Event, payload.Title, JsonSerializer.Serialize(payload));
        var fetched = await _draftService.GetDraftAsync(saved.DraftId);
        var roundTripped = fetched!.GetPayload<EventDraftPayload>();

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public async Task GetPayload_EraDraftPayload_RoundTripsThroughSaveAndFetch()
    {
        // Arrange
        var payload = new EraDraftPayload
        {
            Name = "College",
            Subtitle = "Undergrad",
            StartDate = new DateTime(2010, 9, 1),
            EndDate = new DateTime(2014, 6, 15),
            CategoryId = "cat-education",
            ColorCode = "#0078D4",
            Description = "Four formative years",
            Notes = "Dorm then apartment",
            Tags = new List<string> { "school", "friends" }
        };

        // Act
        var saved = await _draftService.SaveDraftAsync(
            DraftTypes.Era, payload.Name, JsonSerializer.Serialize(payload));
        var fetched = await _draftService.GetDraftAsync(saved.DraftId);
        var roundTripped = fetched!.GetPayload<EraDraftPayload>();

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public async Task GetPayload_PersonDraftPayload_RoundTripsThroughSaveAndFetch()
    {
        // Arrange
        var payload = new PersonDraftPayload
        {
            Name = "Grace Hopper",
            Nickname = "Amazing Grace",
            Relationship = "mentor",
            Email = "grace@example.com",
            Phone = "555-0199",
            Birthday = new DateTime(1906, 12, 9),
            Company = "Navy",
            Notes = "Compiler pioneer",
            AvatarColor = "#5C74C4",
            IsFavorite = true,
            FirstMetDate = new DateTime(1985, 3, 3)
        };

        // Act
        var saved = await _draftService.SaveDraftAsync(
            DraftTypes.Person, payload.Name, JsonSerializer.Serialize(payload));
        var fetched = await _draftService.GetDraftAsync(saved.DraftId);
        var roundTripped = fetched!.GetPayload<PersonDraftPayload>();

        // Assert
        roundTripped.Should().NotBeNull();
        roundTripped.Should().BeEquivalentTo(payload);
    }

    [Fact]
    public void GetPayload_MalformedJson_ReturnsNullWithoutThrowing()
    {
        // Arrange
        var dto = new DraftDto
        {
            DraftType = DraftTypes.Event,
            PayloadJson = "{ this is not valid json"
        };

        // Act
        var payload = dto.GetPayload<EventDraftPayload>();

        // Assert
        payload.Should().BeNull();
    }

    [Fact]
    public void GetPayload_EmptyPayload_ReturnsNull()
    {
        // Arrange
        var dto = new DraftDto { PayloadJson = "   " };

        // Act & Assert
        dto.GetPayload<EventDraftPayload>().Should().BeNull();
    }

    // ---- Seed helper ----

    /// <summary>
    /// Seeds a draft directly through the repository with an explicit UpdatedAt.
    /// Added entities are not timestamp-stamped by AppDbContext (only Modified
    /// ones are), so the seeded ordering is fully deterministic.
    /// </summary>
    private async Task<Draft> SeedDraftAsync(string draftType, string title, DateTime updatedAt)
    {
        var draft = new Draft
        {
            DraftId = Guid.NewGuid().ToString(),
            DraftType = draftType,
            Title = title,
            PayloadJson = "{}",
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };
        await _repository.AddAsync(draft);
        return draft;
    }

    public void Dispose()
    {
        using var context = _contextFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }
}
