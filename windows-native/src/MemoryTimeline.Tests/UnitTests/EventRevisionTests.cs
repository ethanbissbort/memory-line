using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Revision-history behavior (F12) over a real file-based SQLite database
/// (junction/NOCASE/transaction semantics matter here): lifecycle hooks write
/// the right revisions, the enable gate works, and restore reconciles both
/// scalars and junctions while keeping history append-only.
/// </summary>
public class EventRevisionTests : IDisposable
{
    private readonly string _databasePath;
    private readonly TestDbContextFactory _contextFactory;
    private readonly SettingsService _settingsService;
    private readonly EventRevisionWriter _revisionWriter;
    private readonly EventRepository _eventRepository;
    private readonly EventService _eventService;
    private readonly EventRevisionRepository _revisionRepository;
    private readonly RevisionService _revisionService;

    public EventRevisionTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"EventRevisionTests_{Guid.NewGuid()}.db");
        _contextFactory = TestDbContextFactory.CreateSqliteFile(_databasePath);
        using (var context = _contextFactory.CreateDbContext())
        {
            context.Database.EnsureCreated(); // seeds revision_history_enabled=true
        }

        _settingsService = new SettingsService(_contextFactory, new Mock<ILogger<SettingsService>>().Object);
        _revisionWriter = new EventRevisionWriter(
            _contextFactory, _settingsService, new Mock<ILogger<EventRevisionWriter>>().Object);
        _eventRepository = new EventRepository(_contextFactory);
        _eventService = new EventService(
            _eventRepository,
            _contextFactory,
            new Mock<ILogger<EventService>>().Object,
            embeddingService: null,
            mediaService: null,
            revisionWriter: _revisionWriter);
        _revisionRepository = new EventRevisionRepository(_contextFactory);
        _revisionService = new RevisionService(
            _revisionRepository,
            _eventService,
            _contextFactory,
            _revisionWriter,
            new Mock<ILogger<RevisionService>>().Object);
    }

    #region Lifecycle hooks

    [Fact]
    public async Task CreateEventAsync_WritesCreatedRevision()
    {
        // Act
        var created = await _eventService.CreateEventAsync(new Event
        {
            Title = "First memory",
            StartDate = new DateTime(2020, 5, 1),
            Category = EventCategory.Other
        });

        // Assert
        var revisions = await _revisionRepository.GetForEventAsync(created.EventId);
        revisions.Should().HaveCount(1);
        revisions[0].RevisionKind.Should().Be(RevisionKind.Created);
        revisions[0].ChangedFieldsCsv.Should().BeNull();

        var snapshot = EventRevisionWriter.TryDeserialize(revisions[0].SnapshotJson);
        snapshot.Should().NotBeNull();
        snapshot!.Title.Should().Be("First memory");
        snapshot.StartDate.Should().Be(new DateTime(2020, 5, 1));
        snapshot.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateEventAsync_WritesEditedRevision_WithPreEditValues_AndChangedFields()
    {
        // Arrange
        var created = await _eventService.CreateEventAsync(new Event
        {
            Title = "Original title",
            StartDate = new DateTime(2021, 3, 10),
            Description = "Same description"
        });

        // Act - change title and add an end date (mutating the detached
        // entity, the same shape production callers pass to UpdateEventAsync)
        created.Title = "Renamed title";
        created.EndDate = new DateTime(2021, 3, 12);
        await _eventService.UpdateEventAsync(created);

        // Assert - the Edited revision holds the PRE-edit values
        var revisions = await _revisionRepository.GetForEventAsync(created.EventId);
        revisions.Should().HaveCount(2);

        var edited = revisions.Single(r => r.RevisionKind == RevisionKind.Edited);
        edited.ChangedFieldsCsv.Should().Be("title,end_date");

        var snapshot = EventRevisionWriter.TryDeserialize(edited.SnapshotJson);
        snapshot.Should().NotBeNull();
        snapshot!.Title.Should().Be("Original title");
        snapshot.EndDate.Should().BeNull();
        snapshot.Description.Should().Be("Same description");
    }

    [Fact]
    public async Task UpdateEventAsync_EditedSnapshot_IncludesJunctionNames()
    {
        // Arrange - an event with a tag and a person linked
        var created = await _eventService.CreateEventAsync(new Event
        {
            Title = "Tagged event",
            StartDate = new DateTime(2022, 1, 1)
        });
        var tagId = await SeedTagAsync("holiday");
        var personId = await SeedPersonAsync("Sarah");
        await _eventService.AddTagToEventAsync(created.EventId, tagId);
        await _eventService.AddPersonToEventAsync(created.EventId, personId);

        // Act
        created.Title = "Tagged event v2";
        await _eventService.UpdateEventAsync(created);

        // Assert - the pre-edit snapshot captured the junction names
        var edited = (await _revisionRepository.GetForEventAsync(created.EventId))
            .Single(r => r.RevisionKind == RevisionKind.Edited);
        var snapshot = EventRevisionWriter.TryDeserialize(edited.SnapshotJson)!;
        snapshot.Tags.Should().BeEquivalentTo(new[] { "holiday" });
        snapshot.People.Should().BeEquivalentTo(new[] { "Sarah" });
    }

    [Fact]
    public async Task ApprovePendingEventAsync_WritesApprovedRevision_WithExtractedNames()
    {
        // Arrange - a pending event whose ExtractedData carries names
        var extracted = new ExtractedEvent
        {
            Title = "Trip to Paris",
            StartDate = new DateTime(2023, 6, 1),
            Category = "Travel",
            Tags = new List<string> { "vacation" },
            People = new List<string> { "Sarah" },
            Locations = new List<string> { "Paris" },
            Confidence = 0.9
        };

        string pendingId;
        await using (var context = _contextFactory.CreateDbContext())
        {
            var pending = new PendingEvent
            {
                Title = "Trip to Paris",
                StartDate = new DateTime(2023, 6, 1),
                Category = "travel",
                ExtractedData = System.Text.Json.JsonSerializer.Serialize(extracted),
                Status = PendingStatus.PendingReview.ToStringValue(),
                IsApproved = false,
                CreatedAt = DateTime.UtcNow
            };
            context.PendingEvents.Add(pending);
            await context.SaveChangesAsync();
            pendingId = pending.PendingId;
        }

        var extractionService = new EventExtractionService(
            new Mock<ILlmService>().Object,
            new Mock<ISpeechToTextService>().Object,
            new Mock<IEventService>().Object,
            new Mock<ISettingsService>().Object,
            new Mock<IRecordingQueueRepository>().Object,
            new Mock<IPersonService>().Object,
            _contextFactory,
            new Mock<ILogger<EventExtractionService>>().Object,
            revisionWriter: _revisionWriter);

        // Act
        var approved = await extractionService.ApprovePendingEventAsync(pendingId);

        // Assert - one Approved revision, committed with the approve transaction
        var revisions = await _revisionRepository.GetForEventAsync(approved.EventId);
        revisions.Should().HaveCount(1);
        revisions[0].RevisionKind.Should().Be(RevisionKind.Approved);

        var snapshot = EventRevisionWriter.TryDeserialize(revisions[0].SnapshotJson)!;
        snapshot.Title.Should().Be("Trip to Paris");
        snapshot.Tags.Should().BeEquivalentTo(new[] { "vacation" });
        snapshot.People.Should().BeEquivalentTo(new[] { "Sarah" });
        snapshot.Locations.Should().BeEquivalentTo(new[] { "Paris" });
    }

    [Fact]
    public async Task RevisionWrites_WhenHistoryDisabled_WriteNothing()
    {
        // Arrange - flip the per-call gate off
        await _settingsService.SetSettingAsync(SettingKeys.RevisionHistoryEnabled, "false");

        // Act - create + update, the two most common hooks
        var created = await _eventService.CreateEventAsync(new Event
        {
            Title = "Silent event",
            StartDate = new DateTime(2020, 1, 1)
        });
        created.Title = "Silent event v2";
        await _eventService.UpdateEventAsync(created);

        // Assert
        (await _revisionRepository.CountAsync()).Should().Be(0);

        // Re-enabling takes effect immediately (read per call, no restart)
        await _settingsService.SetSettingAsync(SettingKeys.RevisionHistoryEnabled, "true");
        created.Title = "Silent event v3";
        await _eventService.UpdateEventAsync(created);
        (await _revisionRepository.CountAsync(created.EventId)).Should().Be(1);
    }

    #endregion

    #region Restore

    [Fact]
    public async Task RestoreRevisionAsync_RestoresScalarsAndJunctions_AndAppendsRestoredRevision()
    {
        // Arrange - v1 state: title "V1", tag "alpha", person "Sarah"
        var created = await _eventService.CreateEventAsync(new Event
        {
            Title = "V1",
            StartDate = new DateTime(2021, 7, 1)
        });
        var alphaId = await SeedTagAsync("alpha");
        var sarahId = await SeedPersonAsync("Sarah");
        await _eventService.AddTagToEventAsync(created.EventId, alphaId);
        await _eventService.AddPersonToEventAsync(created.EventId, sarahId);

        // Edit -> the Edited revision snapshots the v1 state (incl. junctions)
        created.Title = "V2";
        await _eventService.UpdateEventAsync(created);

        // Mutate to the current state: drop alpha + Sarah, add beta
        var betaId = await SeedTagAsync("beta");
        await _eventService.RemoveTagFromEventAsync(created.EventId, alphaId);
        await _eventService.RemovePersonFromEventAsync(created.EventId, sarahId);
        await _eventService.AddTagToEventAsync(created.EventId, betaId);

        // Delete the "alpha" tag row entirely so restore must exercise the
        // create-or-link path (create by name, not just re-link).
        await using (var context = _contextFactory.CreateDbContext())
        {
            var alpha = await context.Tags.FindAsync(alphaId);
            context.Tags.Remove(alpha!);
            await context.SaveChangesAsync();
        }

        var editedRevision = (await _revisionRepository.GetForEventAsync(created.EventId))
            .Single(r => r.RevisionKind == RevisionKind.Edited);
        var countBeforeRestore = await _revisionRepository.CountAsync(created.EventId);

        // Act
        await _revisionService.RestoreRevisionAsync(created.EventId, editedRevision.RevisionId);

        // Assert - scalars back to v1
        var restored = await _eventService.GetEventWithDetailsAsync(created.EventId);
        restored!.Title.Should().Be("V1");

        // Junctions reconciled to the snapshot names (case-insensitive
        // create-or-link: "alpha" recreated, "beta" unlinked, "Sarah" re-linked)
        restored.EventTags.Select(et => et.Tag.TagName)
            .Should().BeEquivalentTo(new[] { "alpha" });
        restored.EventPeople.Select(ep => ep.Person.Name)
            .Should().BeEquivalentTo(new[] { "Sarah" });

        // History is append-only: a NEW Restored revision holding the
        // PRE-restore state was added
        var revisions = await _revisionRepository.GetForEventAsync(created.EventId);
        revisions.Should().HaveCount(countBeforeRestore + 1);

        var restoredRevision = revisions.Single(r => r.RevisionKind == RevisionKind.Restored);
        var preRestoreSnapshot = EventRevisionWriter.TryDeserialize(restoredRevision.SnapshotJson)!;
        preRestoreSnapshot.Title.Should().Be("V2");
        preRestoreSnapshot.Tags.Should().BeEquivalentTo(new[] { "beta" });
        preRestoreSnapshot.People.Should().BeEmpty();

        restoredRevision.ChangedFieldsCsv.Should().Contain("title");
        restoredRevision.ChangedFieldsCsv.Should().Contain("tags");
        restoredRevision.ChangedFieldsCsv.Should().Contain("people");
    }

    [Fact]
    public async Task RestoreRevisionAsync_RevisionOfDifferentEvent_Throws()
    {
        // Arrange
        var first = await _eventService.CreateEventAsync(new Event
        {
            Title = "First",
            StartDate = new DateTime(2020, 1, 1)
        });
        var second = await _eventService.CreateEventAsync(new Event
        {
            Title = "Second",
            StartDate = new DateTime(2020, 2, 1)
        });
        var firstRevision = (await _revisionRepository.GetForEventAsync(first.EventId)).Single();

        // Act / Assert
        var act = () => _revisionService.RestoreRevisionAsync(second.EventId, firstRevision.RevisionId);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different event*");
    }

    #endregion

    #region Restore projections

    [Fact]
    public async Task RestoreRevisionAsync_ScalarOnlyChange_PublishesTheRestoredEvent()
    {
        // Arrange - the case that published nothing at all. ApplyScalarFieldsAsync
        // writes through its own context specifically to dodge UpdateEventAsync's
        // revision hook, and it took the projection with it, so a companion kept
        // showing the pre-restore title indefinitely.
        var projected = new List<string>();
        var (eventService, revisionService) = BuildPublishingServices(projected);
        var created = await eventService.CreateEventAsync(new Event
        {
            Title = "V1",
            StartDate = new DateTime(2020, 1, 1),
            Category = EventCategory.Other
        });
        created.Title = "V2";
        await eventService.UpdateEventAsync(created);
        var v1Revision = (await _revisionRepository.GetForEventAsync(created.EventId))
            .Single(r => r.RevisionKind == RevisionKind.Created);
        projected.Clear();

        // Act - no junction moves, so nothing else in this path would publish
        await revisionService.RestoreRevisionAsync(created.EventId, v1Revision.RevisionId);

        // Assert
        projected.Should().Equal($"event:{created.EventId}");

        var stored = await eventService.GetEventByIdAsync(created.EventId);
        stored!.Title.Should().Be("V1", "the publish must describe a restore that really happened");
    }

    [Fact]
    public async Task RestoreRevisionAsync_CreatingAPerson_PublishesThatPersonAndTheEvent()
    {
        // Arrange - a snapshot naming somebody who no longer has a row. The
        // restore mints one, and an event payload carries person IDS only, so
        // without a person projection the restored event names an id no
        // companion can resolve to a name.
        var projected = new List<string>();
        var (eventService, revisionService) = BuildPublishingServices(projected);
        var created = await eventService.CreateEventAsync(new Event
        {
            Title = "Weekend away",
            StartDate = new DateTime(2020, 1, 1),
            Category = EventCategory.Other
        });
        var danaId = await SeedPersonAsync("Dana");
        await eventService.AddPersonToEventAsync(created.EventId, danaId);

        // Edit so the pre-edit revision captures Dana, then unlink and delete
        // her, leaving the snapshot's name with nothing behind it.
        created.Title = "Weekend away (edited)";
        await eventService.UpdateEventAsync(created);
        await eventService.RemovePersonFromEventAsync(created.EventId, danaId);
        await using (var context = _contextFactory.CreateDbContext())
        {
            context.People.Remove(await context.People.SingleAsync(p => p.PersonId == danaId));
            await context.SaveChangesAsync();
        }

        var withDana = (await _revisionRepository.GetForEventAsync(created.EventId))
            .Single(r => r.RevisionKind == RevisionKind.Edited);
        projected.Clear();

        // Act
        await revisionService.RestoreRevisionAsync(created.EventId, withDana.RevisionId);

        // Assert - a new Dana exists and was projected
        string newDanaId;
        await using (var context = _contextFactory.CreateDbContext())
        {
            var dana = await context.People.AsNoTracking().SingleAsync(p => p.Name == "Dana");
            newDanaId = dana.PersonId;
            newDanaId.Should().NotBe(danaId, "the original row was deleted, so this is a new contact");
        }

        // The exact sequence, because each entry is a separate decision: the
        // contact is published the moment it exists rather than left to the
        // link that follows (a row in the archive should not wait on a later
        // step to become visible), the link then republishes both the event and
        // Dana's now-nonzero count, and the restore's own publish closes it out
        // — that last one carries the restored scalars, and is the entire
        // projection when a restore moves no junction at all.
        projected.Should().Equal(
            $"person:{newDanaId}",
            $"event:{created.EventId}",
            $"person:{newDanaId}",
            $"event:{created.EventId}");
    }

    [Fact]
    public async Task RestoreRevisionAsync_ProjectionPublisherThrows_StillRestores()
    {
        // Arrange - the restore has already committed by the time anything is
        // published, so a failed publish must not report a failure for it
        var projected = new List<string>();
        var publisher = CreateRecordingPublisher(projected);
        publisher
            .Setup(p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is locked"));
        var (eventService, revisionService) = BuildPublishingServices(publisher.Object);

        var created = await eventService.CreateEventAsync(new Event
        {
            Title = "V1",
            StartDate = new DateTime(2020, 1, 1),
            Category = EventCategory.Other
        });
        created.Title = "V2";
        await eventService.UpdateEventAsync(created);
        var v1Revision = (await _revisionRepository.GetForEventAsync(created.EventId))
            .Single(r => r.RevisionKind == RevisionKind.Created);

        // Act
        await revisionService.RestoreRevisionAsync(created.EventId, v1Revision.RevisionId);

        // Assert
        var stored = await eventService.GetEventByIdAsync(created.EventId);
        stored!.Title.Should().Be("V1");
    }

    [Fact]
    public async Task RestoreRevisionAsync_WithoutAProjectionPublisher_StillRestores()
    {
        // The fixture's services are built WITHOUT a publisher — the shape every
        // pre-existing construction uses, and the reason the parameter is a
        // trailing optional one.
        var created = await _eventService.CreateEventAsync(new Event
        {
            Title = "V1",
            StartDate = new DateTime(2020, 1, 1),
            Category = EventCategory.Other
        });
        created.Title = "V2";
        await _eventService.UpdateEventAsync(created);
        var v1Revision = (await _revisionRepository.GetForEventAsync(created.EventId))
            .Single(r => r.RevisionKind == RevisionKind.Created);

        // Act
        await _revisionService.RestoreRevisionAsync(created.EventId, v1Revision.RevisionId);

        // Assert
        var stored = await _eventService.GetEventByIdAsync(created.EventId);
        stored!.Title.Should().Be("V1");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// A second EventService/RevisionService pair over the SAME database as the
    /// fixture's, both wired to a publisher that records its calls in order.
    /// Both need it: the restore's junction reconciliation runs through
    /// IEventService, so leaving that one unpublished would credit RevisionService
    /// with projections it never made.
    /// </summary>
    private (EventService, RevisionService) BuildPublishingServices(List<string> projected)
        => BuildPublishingServices(CreateRecordingPublisher(projected).Object);

    private (EventService, RevisionService) BuildPublishingServices(
        ITimelineProjectionPublisher publisher)
    {
        var eventService = new EventService(
            _eventRepository,
            _contextFactory,
            new Mock<ILogger<EventService>>().Object,
            revisionWriter: _revisionWriter,
            projectionPublisher: publisher);
        var revisionService = new RevisionService(
            _revisionRepository,
            eventService,
            _contextFactory,
            _revisionWriter,
            new Mock<ILogger<RevisionService>>().Object,
            projectionPublisher: publisher);
        return (eventService, revisionService);
    }

    private static Mock<ITimelineProjectionPublisher> CreateRecordingPublisher(List<string> projected)
    {
        var publisher = new Mock<ITimelineProjectionPublisher>();
        publisher
            .Setup(p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => projected.Add($"event:{id}"))
            .Returns(Task.CompletedTask);
        publisher
            .Setup(p => p.PublishPersonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => projected.Add($"person:{id}"))
            .Returns(Task.CompletedTask);
        publisher
            .Setup(p => p.PublishDeletedAsync(
                It.IsAny<TimelineProjectionEntity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<TimelineProjectionEntity, string, CancellationToken>(
                (entity, id, _) => projected.Add($"delete:{entity}:{id}"))
            .Returns(Task.CompletedTask);
        return publisher;
    }

    private async Task<string> SeedTagAsync(string name)
    {
        await using var context = _contextFactory.CreateDbContext();
        var tag = new Tag { TagId = Guid.NewGuid().ToString(), TagName = name, CreatedAt = DateTime.UtcNow };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        return tag.TagId;
    }

    private async Task<string> SeedPersonAsync(string name)
    {
        await using var context = _contextFactory.CreateDbContext();
        var person = new Person { PersonId = Guid.NewGuid().ToString(), Name = name, CreatedAt = DateTime.UtcNow };
        context.People.Add(person);
        await context.SaveChangesAsync();
        return person.PersonId;
    }

    #endregion

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
