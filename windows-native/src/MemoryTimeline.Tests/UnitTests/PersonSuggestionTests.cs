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

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for the person-suggestion pipeline on
/// <see cref="EventExtractionService"/>: classification of extracted names into
/// new/known/update suggestions and one-click apply. Uses a REAL
/// <see cref="PersonService"/> over the shared in-memory context factory; the
/// unrelated collaborator services are Moq mocks (never invoked on these paths).
/// </summary>
public class PersonSuggestionTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly IPersonService _personService;
    private readonly EventExtractionService _extractionService;

    public PersonSuggestionTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _personService = new PersonService(_contextFactory, new Mock<ILogger<PersonService>>().Object);

        _extractionService = new EventExtractionService(
            new Mock<ILlmService>().Object,
            new Mock<ISpeechToTextService>().Object,
            new Mock<IEventService>().Object,
            new Mock<ISettingsService>().Object,
            new Mock<IRecordingQueueRepository>().Object,
            _personService,
            _contextFactory,
            new Mock<ILogger<EventExtractionService>>().Object);
    }

    // ---- GetPersonSuggestionsAsync ----

    [Fact]
    public async Task GetPersonSuggestionsAsync_ClassifiesNewKnownAndUpdateDetails()
    {
        // Arrange - two existing contacts: Alice already has the suggested
        // relationship (nothing to add), Marcus lacks one (update candidate)
        var alice = await _personService.CreatePersonAsync(new PersonDto
        {
            Name = "Alice Johnson",
            Relationship = "sister",
            Notes = "Met in 2019"
        });
        var marcus = await _personService.CreatePersonAsync(new PersonDto { Name = "Marcus Lee" });

        var extracted = new ExtractedEvent
        {
            Title = "Family dinner",
            PeopleDetails = new List<ExtractedPersonDetail>
            {
                new() { Name = "Alice Johnson", Relationship = "sister" },
                new() { Name = "Marcus Lee", Relationship = "coworker" },
                new() { Name = "Zara Quinn", Relationship = "neighbor", Details = "Just moved in next door" }
            }
        };
        var pendingEventId = await SeedPendingEventAsync(JsonSerializer.Serialize(extracted));

        // Act
        var suggestions = await _extractionService.GetPersonSuggestionsAsync(pendingEventId);

        // Assert
        suggestions.Should().HaveCount(3);
        suggestions.Should().OnlyContain(s => s.PendingEventId == pendingEventId && !s.IsApplied);

        var newPerson = suggestions.Single(s => s.Name == "Zara Quinn");
        newPerson.Kind.Should().Be(PersonSuggestionKind.NewPerson);
        newPerson.MatchedPersonId.Should().BeNull();
        newPerson.SuggestedRelationship.Should().Be("neighbor");
        newPerson.SuggestedDetails.Should().Be("Just moved in next door");
        newPerson.CanApply.Should().BeTrue();

        var known = suggestions.Single(s => s.Name == "Alice Johnson");
        known.Kind.Should().Be(PersonSuggestionKind.KnownPerson);
        known.MatchedPersonId.Should().Be(alice.PersonId);
        known.MatchedPersonName.Should().Be("Alice Johnson");
        known.CanApply.Should().BeFalse();

        var update = suggestions.Single(s => s.Name == "Marcus Lee");
        update.Kind.Should().Be(PersonSuggestionKind.UpdateDetails);
        update.MatchedPersonId.Should().Be(marcus.PersonId);
        update.SuggestedRelationship.Should().Be("coworker");
        update.CanApply.Should().BeTrue();
    }

    [Fact]
    public async Task GetPersonSuggestionsAsync_LegacyPeopleOnlyPayload_FallsBackToFlatList()
    {
        // Arrange - older extractions carry only the flat People list
        var extracted = new ExtractedEvent
        {
            Title = "Coffee catch-up",
            People = new List<string> { "Casey Brown" },
            PeopleDetails = null
        };
        var pendingEventId = await SeedPendingEventAsync(JsonSerializer.Serialize(extracted));

        // Act
        var suggestions = await _extractionService.GetPersonSuggestionsAsync(pendingEventId);

        // Assert - no contacts exist, so the name becomes a NewPerson suggestion
        suggestions.Should().ContainSingle();
        suggestions[0].Name.Should().Be("Casey Brown");
        suggestions[0].Kind.Should().Be(PersonSuggestionKind.NewPerson);
        suggestions[0].SuggestedRelationship.Should().BeNull();
        suggestions[0].SuggestedDetails.Should().BeNull();
    }

    [Fact]
    public async Task GetPersonSuggestionsAsync_MalformedExtractedData_ReturnsEmptyList()
    {
        // Arrange
        var pendingEventId = await SeedPendingEventAsync("{ this is not valid json");

        // Act
        var suggestions = await _extractionService.GetPersonSuggestionsAsync(pendingEventId);

        // Assert
        suggestions.Should().BeEmpty();
    }

    // ---- ApplyPersonSuggestionAsync ----

    [Fact]
    public async Task ApplyPersonSuggestionAsync_NewPerson_CreatesContactWithRelationshipAndNotes()
    {
        // Arrange
        var suggestion = new PersonSuggestionDto
        {
            PendingEventId = "pending-1",
            Name = "Dana White",
            Kind = PersonSuggestionKind.NewPerson,
            SuggestedRelationship = "mentor",
            SuggestedDetails = "Met at the conference"
        };

        // Act
        var applied = await _extractionService.ApplyPersonSuggestionAsync(suggestion);

        // Assert
        applied.IsApplied.Should().BeTrue();
        applied.MatchedPersonId.Should().NotBeNullOrEmpty();
        applied.MatchedPersonName.Should().Be("Dana White");

        var created = await _personService.GetPersonByNameAsync("Dana White");
        created.Should().NotBeNull();
        created!.PersonId.Should().Be(applied.MatchedPersonId);
        created.Relationship.Should().Be("mentor");
        created.Notes.Should().Be("Met at the conference");
    }

    [Fact]
    public async Task ApplyPersonSuggestionAsync_NewPersonDuplicateRace_ResolvesToExistingContact()
    {
        // Arrange - the contact already exists when the NewPerson suggestion is applied
        var existing = await _personService.CreatePersonAsync(new PersonDto { Name = "Grace Kim" });
        var suggestion = new PersonSuggestionDto
        {
            Name = "Grace Kim",
            Kind = PersonSuggestionKind.NewPerson,
            SuggestedRelationship = "cousin"
        };

        // Act
        var applied = await _extractionService.ApplyPersonSuggestionAsync(suggestion);

        // Assert - no duplicate contact; the suggestion resolves to the existing one
        applied.IsApplied.Should().BeTrue();
        applied.MatchedPersonId.Should().Be(existing.PersonId);
        applied.MatchedPersonName.Should().Be("Grace Kim");
        (await _personService.GetAllPersonsAsync()).Should().ContainSingle(p => p.Name == "Grace Kim");
    }

    [Fact]
    public async Task ApplyPersonSuggestionAsync_UpdateDetails_FillsOnlyMissingFields()
    {
        // Arrange - Elena has notes already but no relationship
        var elena = await _personService.CreatePersonAsync(new PersonDto
        {
            Name = "Elena Ruiz",
            Notes = "Existing notes"
        });
        var suggestion = new PersonSuggestionDto
        {
            Name = "Elena Ruiz",
            Kind = PersonSuggestionKind.UpdateDetails,
            MatchedPersonId = elena.PersonId,
            MatchedPersonName = elena.Name,
            SuggestedRelationship = "aunt",
            SuggestedDetails = "Must not overwrite existing notes"
        };

        // Act
        var applied = await _extractionService.ApplyPersonSuggestionAsync(suggestion);

        // Assert - the missing relationship is filled; existing notes are untouched
        applied.IsApplied.Should().BeTrue();
        var updated = await _personService.GetPersonAsync(elena.PersonId);
        updated.Should().NotBeNull();
        updated!.Relationship.Should().Be("aunt");
        updated.Notes.Should().Be("Existing notes");
    }

    [Fact]
    public async Task ApplyPersonSuggestionAsync_KnownPerson_NoOpsButMarksApplied()
    {
        // Arrange
        var frank = await _personService.CreatePersonAsync(new PersonDto
        {
            Name = "Frank Ocean",
            Relationship = "friend"
        });
        var before = await _personService.GetPersonAsync(frank.PersonId);
        var suggestion = new PersonSuggestionDto
        {
            Name = "Frank Ocean",
            Kind = PersonSuggestionKind.KnownPerson,
            MatchedPersonId = frank.PersonId,
            MatchedPersonName = frank.Name,
            SuggestedRelationship = "friend"
        };

        // Act
        var applied = await _extractionService.ApplyPersonSuggestionAsync(suggestion);

        // Assert - IsApplied flips but the contact is untouched
        applied.IsApplied.Should().BeTrue();
        var after = await _personService.GetPersonAsync(frank.PersonId);
        after.Should().NotBeNull();
        after!.Relationship.Should().Be("friend");
        after.Notes.Should().BeNull();
        after.UpdatedAt.Should().Be(before!.UpdatedAt);
    }

    // ---- Seed helper ----

    private async Task<string> SeedPendingEventAsync(string extractedData)
    {
        var pendingEvent = new PendingEvent
        {
            PendingId = Guid.NewGuid().ToString(),
            Title = "Extracted event",
            StartDate = new DateTime(2024, 5, 1),
            ExtractedData = extractedData,
            ConfidenceScore = 0.9
        };

        await using var context = _contextFactory.CreateDbContext();
        context.PendingEvents.Add(pendingEvent);
        await context.SaveChangesAsync();
        return pendingEvent.PendingId;
    }

    public void Dispose()
    {
        using var context = _contextFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }
}
