using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for the approval path's person resolution
/// (<see cref="EventExtractionService.ApprovePendingEventAsync"/>): under the
/// NOCASE unique name index, a case-variant extracted name ("sarah" next to an
/// existing "Sarah") must link the existing contact instead of attempting an
/// insert that would violate the index, and alias/tombstone lookups must
/// resolve to the living person. A file-based SQLite database is used because
/// approval opens a relational transaction and the NOCASE unique index is the
/// behavior under test.
/// </summary>
public class ApprovalPersonResolutionTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly PersonService _personService;
    private readonly EventExtractionService _extractionService;
    private readonly string _databasePath;

    public ApprovalPersonResolutionTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(), $"ApprovalPersonResolutionTests_{Guid.NewGuid()}.db");
        _contextFactory = TestDbContextFactory.CreateSqliteFile(_databasePath);

        using (var context = _contextFactory.CreateDbContext())
        {
            context.Database.EnsureCreated();
        }

        _personService = new PersonService(
            _contextFactory, new Mock<ILogger<PersonService>>().Object);
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

    [Fact]
    public async Task ApprovePendingEventAsync_CaseVariantName_LinksExistingPersonWithoutThrowing()
    {
        // Arrange - an existing "Sarah" plus an extraction that spelled her "sarah"
        var sarah = await _personService.CreatePersonAsync(new PersonDto { Name = "Sarah" });
        var pendingId = await SeedPendingEventAsync("Beach day", new[] { "sarah" });

        // Act - must NOT throw a unique-constraint violation
        var created = await _extractionService.ApprovePendingEventAsync(pendingId);

        // Assert - no duplicate person was minted; the event links the existing Sarah
        await using var context = _contextFactory.CreateDbContext();
        (await context.People.AsNoTracking().CountAsync()).Should().Be(1);
        var link = await context.EventPeople.AsNoTracking().SingleAsync();
        link.EventId.Should().Be(created.EventId);
        link.PersonId.Should().Be(sarah.PersonId);
    }

    [Fact]
    public async Task ApprovePendingEventAsync_AliasAndCanonicalSpellings_LinkSamePersonOnce()
    {
        // Arrange - "Bob" is an alias of Robert; the extraction mentions both
        // the alias and a case-variant of the canonical name
        var robert = await _personService.CreatePersonAsync(new PersonDto { Name = "Robert" });
        await _personService.AddAliasAsync(robert.PersonId, "Bob");
        var pendingId = await SeedPendingEventAsync("Fishing trip", new[] { "Bob", "robert" });

        // Act
        var created = await _extractionService.ApprovePendingEventAsync(pendingId);

        // Assert - both spellings resolved to Robert, linked exactly once
        await using var context = _contextFactory.CreateDbContext();
        (await context.People.AsNoTracking().CountAsync()).Should().Be(1);
        var link = await context.EventPeople.AsNoTracking().SingleAsync();
        link.EventId.Should().Be(created.EventId);
        link.PersonId.Should().Be(robert.PersonId);
    }

    [Fact]
    public async Task ApprovePendingEventAsync_TombstonedName_LinksSurvivingPerson()
    {
        // Arrange - Mike was merged into Michael; the extraction still says "Mike"
        var mike = await _personService.CreatePersonAsync(new PersonDto { Name = "Mike" });
        var michael = await _personService.CreatePersonAsync(new PersonDto { Name = "Michael" });
        await _personService.MergeAsync(mike.PersonId, michael.PersonId);
        var pendingId = await SeedPendingEventAsync("Old friends", new[] { "Mike" });

        // Act
        var created = await _extractionService.ApprovePendingEventAsync(pendingId);

        // Assert - the link lands on the living Michael, not the tombstone
        await using var context = _contextFactory.CreateDbContext();
        var link = await context.EventPeople.AsNoTracking().SingleAsync();
        link.EventId.Should().Be(created.EventId);
        link.PersonId.Should().Be(michael.PersonId);
    }

    [Fact]
    public async Task ApprovePendingEventAsync_GenuinelyNewName_StillCreatesPerson()
    {
        // Arrange - nobody named Zoe exists yet
        var pendingId = await SeedPendingEventAsync("New face", new[] { "Zoe" });

        // Act
        var created = await _extractionService.ApprovePendingEventAsync(pendingId);

        // Assert - a person row was created and linked
        await using var context = _contextFactory.CreateDbContext();
        var zoe = await context.People.AsNoTracking().SingleAsync();
        zoe.Name.Should().Be("Zoe");
        var link = await context.EventPeople.AsNoTracking().SingleAsync();
        link.EventId.Should().Be(created.EventId);
        link.PersonId.Should().Be(zoe.PersonId);
    }

    // ---- Seed helpers ----

    /// <summary>
    /// Seeds an approvable pending event whose ExtractedData mentions the
    /// given people, returning its id.
    /// </summary>
    private async Task<string> SeedPendingEventAsync(string title, string[] people)
    {
        var extracted = new ExtractedEvent
        {
            Title = title,
            StartDate = new DateTime(2024, 7, 1),
            Category = "other",
            People = people.ToList(),
            Confidence = 0.9
        };

        var pending = new PendingEvent
        {
            PendingId = Guid.NewGuid().ToString(),
            Title = title,
            StartDate = extracted.StartDate,
            Category = "other",
            ExtractedData = JsonSerializer.Serialize(extracted),
            Status = PendingStatus.PendingReview.ToStringValue(),
            IsApproved = false,
            CreatedAt = DateTime.UtcNow
        };

        await using var context = _contextFactory.CreateDbContext();
        context.PendingEvents.Add(pending);
        await context.SaveChangesAsync();
        return pending.PendingId;
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
