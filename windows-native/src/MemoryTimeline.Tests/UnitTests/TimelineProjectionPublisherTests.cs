using System.Text.Json;
using FluentAssertions;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Sync;
using MemoryTimeline.SyncContracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="TimelineProjectionPublisher"/> over the REAL
/// repositories and the EF InMemory provider (unique database per test
/// instance), mirroring <see cref="CaptureStatusPublisherTests"/>. Covers the
/// payload shape a companion renders from, the denormalisation of tags/people/
/// locations, the precision-honest display string, publish deduplication,
/// delete tombstones, and the §14.5 rule that nothing archived-only crosses the
/// wire.
/// </summary>
public class TimelineProjectionPublisherTests
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly TestDbContextFactory _contextFactory;
    private readonly TimelineProjectionPublisher _publisher;

    public TimelineProjectionPublisherTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _publisher = new TimelineProjectionPublisher(
            new SyncOutboxRepository(_contextFactory),
            new EventRepository(_contextFactory),
            new EraRepository(_contextFactory),
            new PersonRepository(_contextFactory),
            new PendingEventRepository(_contextFactory),
            new EventMediaRepository(_contextFactory),
            NullLogger<TimelineProjectionPublisher>.Instance);
    }

    #region Events

    [Fact]
    public async Task PublishEventAsync_FullyLinkedEvent_DenormalisesTagsPeopleAndLocations()
    {
        // Arrange - the join a consumer would otherwise have to rebuild
        var eventId = await SeedEventAsync(
            configure: evt =>
            {
                evt.Title = "Ferry to the island";
                evt.Description = "The early crossing was rough";
                evt.Category = EventCategory.Travel;
                evt.DatePrecision = DatePrecision.Season;
                evt.StartDate = new DateTime(2003, 7, 14);
            },
            tagNames: new[] { "summer", "ferry" },
            peopleNames: new[] { "Dana", "Ruth" },
            locations: new[] { ("Harbour Road", 55.9, -3.2) });

        // Act
        await _publisher.PublishEventAsync(eventId);

        // Assert
        var row = await SingleRowAsync(SyncEntityType.Event);
        row.EntityType.Should().Be(SyncChangeEntityType.Event);
        row.EntityId.Should().Be(eventId);
        row.Operation.Should().Be(SyncOutboxOperation.Upsert);
        row.DeliveredAt.Should().BeNull();

        // The wire payload must be camelCase, like every other sync body.
        using var document = JsonDocument.Parse(row.PayloadJson!);
        document.RootElement.TryGetProperty("eventId", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("displayDate", out _).Should().BeTrue();

        var payload = Deserialize<EventProjectionPayload>(row);
        payload.EventId.Should().Be(eventId);
        payload.Title.Should().Be("Ferry to the island");
        payload.Category.Should().Be(EventCategory.Travel);
        payload.DatePrecision.Should().Be("season");

        // Names inline, resolved from the navigations - no junction rows sync.
        payload.Tags.Should().Equal("ferry", "summer");
        payload.Locations.Should().Equal("Harbour Road");

        // People cross as IDs, not names: only the person projection can also
        // express a merge, so the companion resolves them there.
        payload.PersonIds.Should().BeEquivalentTo(await AllPersonIdsAsync());

        // Coordinates ride along so the map has a pin without a second fetch.
        payload.Latitude.Should().Be(55.9);
        payload.Longitude.Should().Be(-3.2);
    }

    [Fact]
    public async Task PublishEventAsync_CoarsePrecision_UsesTheAppsOwnDateFormatter()
    {
        // Arrange - the exact failure the contract warns about: a companion that
        // re-derives the rules turns a summer into a precise day.
        var start = new DateTime(2003, 7, 14);
        var eventId = await SeedEventAsync(evt =>
        {
            evt.StartDate = start;
            evt.DatePrecision = DatePrecision.Season;
        });

        // Act
        await _publisher.PublishEventAsync(eventId);

        // Assert - byte-identical to what Windows renders, and spelled out here
        // so a change to either side shows up as a failing test rather than as
        // drift between two screens.
        var payload = Deserialize<EventProjectionPayload>(await SingleRowAsync(SyncEntityType.Event));
        payload.DisplayDate.Should().Be(DateDisplay.FormatPrecise(start, DatePrecision.Season));
        payload.DisplayDate.Should().Be("Summer 2003");
    }

    [Theory]
    [InlineData(DatePrecision.Day, "14 Jul 2003")]
    [InlineData(DatePrecision.Month, "July 2003")]
    [InlineData(DatePrecision.Year, "2003")]
    [InlineData(DatePrecision.Decade, "2000s")]
    [InlineData(DatePrecision.Unknown, "Date unknown")]
    public async Task PublishEventAsync_EachPrecision_CarriesThePrecisionHonestString(
        DatePrecision precision, string expected)
    {
        // Arrange
        var eventId = await SeedEventAsync(evt =>
        {
            evt.StartDate = new DateTime(2003, 7, 14);
            evt.DatePrecision = precision;
        });

        // Act
        await _publisher.PublishEventAsync(eventId);

        // Assert
        var payload = Deserialize<EventProjectionPayload>(await SingleRowAsync(SyncEntityType.Event));
        payload.DisplayDate.Should().Be(expected);
        payload.DatePrecision.Should().Be(DatePrecisionParser.ToStringValue(precision));
    }

    [Fact]
    public async Task PublishEventAsync_ArchiveOnlyFields_NeverReachTheWire()
    {
        // Arrange - everything §14.5 keeps on Windows, on one event
        var eventId = await SeedEventAsync(evt =>
        {
            evt.AudioFilePath = @"C:\Users\owner\AppData\cache\capture.wav";
            evt.RawTranscript = "we stopped at the overlook past mile marker 40";
            evt.ExtractionMetadata = "{\"sourceText\":\"we stopped at the overlook\"}";
        });

        // Act
        await _publisher.PublishEventAsync(eventId);

        // Assert
        var json = (await SingleRowAsync(SyncEntityType.Event)).PayloadJson!;
        json.Should().NotContain(@"C:\");
        json.Should().NotContain("mile marker 40");
        json.Should().NotContain("sourceText");
    }

    [Fact]
    public async Task PublishEventAsync_LongDescription_IsBoundedToThePreviewLimit()
    {
        // Arrange - the contract says "trimmed to a readable length", and the
        // publisher reuses the capture-status preview bound rather than
        // inventing a second number.
        var eventId = await SeedEventAsync(evt =>
            evt.Description = new string('d', CaptureStatusChangePayload.TranscriptPreviewMaxChars + 500));

        // Act
        await _publisher.PublishEventAsync(eventId);

        // Assert
        var payload = Deserialize<EventProjectionPayload>(await SingleRowAsync(SyncEntityType.Event));
        payload.Description!.Length.Should().Be(CaptureStatusChangePayload.TranscriptPreviewMaxChars);
    }

    [Fact]
    public async Task PublishEventAsync_MediaAttachments_AreCountedNotCarried()
    {
        // Arrange
        var eventId = await SeedEventAsync();
        await using (var context = _contextFactory.CreateDbContext())
        {
            context.EventMedia.AddRange(
                new EventMedia { EventId = eventId, FilePath = @"2003\07\a.jpg" },
                new EventMedia { EventId = eventId, FilePath = @"2003\07\b.jpg" });
            await context.SaveChangesAsync();
        }

        // Act
        await _publisher.PublishEventAsync(eventId);

        // Assert - a badge, without the phone fetching a single byte
        var row = await SingleRowAsync(SyncEntityType.Event);
        Deserialize<EventProjectionPayload>(row).MediaCount.Should().Be(2);
        row.PayloadJson.Should().NotContain(".jpg");
    }

    [Fact]
    public async Task PublishEventAsync_UnchangedProjection_DoesNotWriteASecondRow()
    {
        // Arrange
        var eventId = await SeedEventAsync();
        await _publisher.PublishEventAsync(eventId);

        // Act - the same projection again (e.g. a save that changed nothing a
        // consumer can see)
        await _publisher.PublishEventAsync(eventId);
        await _publisher.PublishEventAsync(eventId);

        // Assert
        (await RowsAsync(SyncEntityType.Event)).Should().HaveCount(1);
    }

    [Fact]
    public async Task PublishEventAsync_TagAddedWithoutTouchingTheEventRow_StillReachesTheCompanion()
    {
        // Arrange - junction writes deliberately do NOT bump events.updated_at,
        // so a timestamp-only comparison would lose this change entirely.
        var eventId = await SeedEventAsync(tagNames: new[] { "summer" });
        await _publisher.PublishEventAsync(eventId);

        // Act
        await AddTagAsync(eventId, "ferry");
        await _publisher.PublishEventAsync(eventId);

        // Assert
        var payloads = await PayloadsAsync<EventProjectionPayload>(SyncEntityType.Event);
        payloads.Should().HaveCount(2);
        payloads[0].Tags.Should().Equal("summer");
        payloads[1].Tags.Should().Equal("ferry", "summer");
    }

    [Fact]
    public async Task PublishEventAsync_UnchangedProjectionOnAnotherEvent_IsNotConfusedWithThisOne()
    {
        // Arrange
        var first = await SeedEventAsync(evt => evt.Title = "Ferry to the island");
        var second = await SeedEventAsync(evt => evt.Title = "Dinner at the harbour");

        // Act
        await _publisher.PublishEventAsync(first);
        await _publisher.PublishEventAsync(second);
        await _publisher.PublishEventAsync(first);

        // Assert - dedupe is per entity, not global
        var rows = await RowsAsync(SyncEntityType.Event);
        rows.Select(r => r.EntityId).Should().Equal(first, second);
    }

    [Fact]
    public async Task PublishEventAsync_UnknownEvent_PublishesNothing()
    {
        // Act - a deleted event is tombstoned through PublishDeletedAsync, which
        // still has its id; this path must not invent an empty projection.
        await _publisher.PublishEventAsync(Guid.NewGuid().ToString());

        // Assert
        (await RowsAsync(SyncEntityType.Event)).Should().BeEmpty();
    }

    #endregion

    #region Eras and people

    [Fact]
    public async Task PublishEraAsync_Era_ProjectsTheColourWindowsActuallyPaints()
    {
        // Arrange - an override beats the stored colour code on screen, so the
        // projection has to carry the effective one or the two disagree.
        string eraId;
        await using (var context = _contextFactory.CreateDbContext())
        {
            var category = new EraCategory { Name = "Education", DefaultColor = "#0078D4" };
            var era = new Era
            {
                Name = "University",
                Subtitle = "Edinburgh",
                StartDate = new DateTime(2001, 9, 1),
                EndDate = new DateTime(2005, 6, 30),
                ColorCode = "#0078D4",
                ColorOverride = "#8764B8",
                CategoryId = category.CategoryId,
                DisplayOrder = 3,
                Notes = "private notes that stay on Windows",
            };
            context.EraCategories.Add(category);
            context.Eras.Add(era);
            await context.SaveChangesAsync();
            eraId = era.EraId;
        }

        // Act
        await _publisher.PublishEraAsync(eraId);

        // Assert
        var row = await SingleRowAsync(SyncEntityType.Era);
        row.EntityType.Should().Be(SyncChangeEntityType.Era);

        var payload = Deserialize<EraProjectionPayload>(row);
        payload.EraId.Should().Be(eraId);
        payload.Name.Should().Be("University");
        payload.Subtitle.Should().Be("Edinburgh");
        payload.ColorCode.Should().Be("#8764B8");
        payload.CategoryName.Should().Be("Education");
        payload.DisplayOrder.Should().Be(3);
        payload.EndDate.Should().Be(new DateTime(2005, 6, 30));

        row.PayloadJson.Should().NotContain("private notes");
    }

    [Fact]
    public async Task PublishEraAsync_UnchangedProjection_DoesNotWriteASecondRow()
    {
        // Arrange
        var eraId = await SeedEraAsync();
        await _publisher.PublishEraAsync(eraId);

        // Act
        await _publisher.PublishEraAsync(eraId);

        // Assert
        (await RowsAsync(SyncEntityType.Era)).Should().HaveCount(1);
    }

    [Fact]
    public async Task PublishPersonAsync_Person_CarriesIdentityAndEventCountButNoContactDetails()
    {
        // Arrange
        await SeedEventAsync(peopleNames: new[] { "Dana" });
        string personId;
        await using (var context = _contextFactory.CreateDbContext())
        {
            var person = await context.People.SingleAsync(p => p.Name == "Dana");
            person.Nickname = "Dee";
            person.Relationship = "sister";
            person.IsFavorite = true;
            person.Email = "dana@example.com";
            person.Phone = "+44 7700 900000";
            person.Notes = "remembers the ferry";
            person.PhotoPath = @"C:\Users\owner\Pictures\dana.jpg";
            await context.SaveChangesAsync();
            personId = person.PersonId;
        }

        // Act
        await _publisher.PublishPersonAsync(personId);

        // Assert
        var row = await SingleRowAsync(SyncEntityType.Person);
        row.EntityType.Should().Be(SyncChangeEntityType.Person);

        var payload = Deserialize<PersonProjectionPayload>(row);
        payload.PersonId.Should().Be(personId);
        payload.Name.Should().Be("Dana");
        payload.Nickname.Should().Be("Dee");
        payload.Relationship.Should().Be("sister");
        payload.IsFavorite.Should().BeTrue();
        payload.MergedIntoId.Should().BeNull();
        payload.EventCount.Should().Be(1);

        // §14.5: contact data and file paths are not timeline content.
        row.PayloadJson.Should().NotContain("dana@example.com");
        row.PayloadJson.Should().NotContain("7700");
        row.PayloadJson.Should().NotContain("remembers the ferry");
        row.PayloadJson.Should().NotContain(@"C:\");
    }

    [Fact]
    public async Task PublishPersonAsync_MergedPerson_IsAnUpsertCarryingTheMergePointerNotADelete()
    {
        // Arrange - old events keep pointing at the merged-away id on purpose,
        // so the row has to survive for the companion to follow.
        string mergedId;
        string survivorId;
        await using (var context = _contextFactory.CreateDbContext())
        {
            var survivor = new Person { Name = "Dana Whitfield" };
            var merged = new Person { Name = "Dana", MergedIntoId = survivor.PersonId };
            context.People.AddRange(survivor, merged);
            await context.SaveChangesAsync();
            survivorId = survivor.PersonId;
            mergedId = merged.PersonId;
        }

        // Act
        await _publisher.PublishPersonAsync(mergedId);

        // Assert
        var row = await SingleRowAsync(SyncEntityType.Person);
        row.Operation.Should().Be(SyncOutboxOperation.Upsert);
        Deserialize<PersonProjectionPayload>(row).MergedIntoId.Should().Be(survivorId);
    }

    #endregion

    #region Pending events

    [Fact]
    public async Task PublishPendingEventAsync_PendingEvent_ProjectsTheQueueWithABoundedPreview()
    {
        // Arrange
        var transcript = new string('t', CaptureStatusChangePayload.TranscriptPreviewMaxChars + 200);
        var (pendingId, captureId) = await SeedPendingEventAsync(pending =>
        {
            pending.Title = "Ferry to the island";
            pending.StartDate = new DateTime(2003, 7, 14);
            pending.DatePrecision = DatePrecision.Season;
            pending.Category = EventCategory.Travel;
            pending.ConfidenceScore = 0.82;
            pending.Transcript = transcript;
            pending.ExtractedData =
                """
                {"Title":"Ferry to the island","Tags":["ferry","summer"],"People":["Dana","Ruth"]}
                """;
        });

        // Act
        await _publisher.PublishPendingEventAsync(pendingId);

        // Assert
        var row = await SingleRowAsync(SyncEntityType.PendingEvent);
        row.EntityType.Should().Be(SyncChangeEntityType.PendingEvent);

        var payload = Deserialize<PendingEventProjectionPayload>(row);
        payload.PendingEventId.Should().Be(pendingId);
        payload.CaptureId.Should().Be(captureId);
        payload.ConfidenceScore.Should().Be(0.82);
        payload.DisplayDate.Should().Be("Summer 2003");
        payload.Tags.Should().Equal("ferry", "summer");
        payload.PeopleNames.Should().Equal("Dana", "Ruth");

        // §14.5: a preview, not a replica.
        payload.TranscriptPreview!.Length
            .Should().Be(CaptureStatusChangePayload.TranscriptPreviewMaxChars);
        payload.TranscriptPreview.Should().Be(transcript[..CaptureStatusChangePayload.TranscriptPreviewMaxChars]);
    }

    [Fact]
    public async Task PublishPendingEventAsync_MalformedExtractionPayload_StillPublishesTheQueueItem()
    {
        // Arrange - a bad extraction payload costs the projection its tags and
        // people, never the whole publish
        var (pendingId, _) = await SeedPendingEventAsync(pending =>
            pending.ExtractedData = "{ this is not json");

        // Act
        await _publisher.PublishPendingEventAsync(pendingId);

        // Assert
        var payload = Deserialize<PendingEventProjectionPayload>(
            await SingleRowAsync(SyncEntityType.PendingEvent));
        payload.PendingEventId.Should().Be(pendingId);
        payload.Tags.Should().BeEmpty();
        payload.PeopleNames.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishPendingEventAsync_UnchangedProjection_DoesNotWriteASecondRow()
    {
        // Arrange
        var (pendingId, _) = await SeedPendingEventAsync();
        await _publisher.PublishPendingEventAsync(pendingId);

        // Act
        await _publisher.PublishPendingEventAsync(pendingId);

        // Assert
        (await RowsAsync(SyncEntityType.PendingEvent)).Should().HaveCount(1);
    }

    #endregion

    #region Tombstones

    [Fact]
    public async Task PublishDeletedAsync_DeletedEvent_TombstonesWithTheDeleteOperation()
    {
        // Arrange - published, then deleted on Windows
        var eventId = await SeedEventAsync();
        await _publisher.PublishEventAsync(eventId);

        // Act
        await _publisher.PublishDeletedAsync(TimelineProjectionEntity.Event, eventId);

        // Assert - the companion is told to drop its copy
        var rows = await RowsAsync(SyncEntityType.Event);
        rows.Should().HaveCount(2);
        rows[1].Operation.Should().Be(SyncOutboxOperation.Delete);
        rows[1].Operation.Should().Be(SyncOperation.Delete);
        rows[1].EntityId.Should().Be(eventId);
        // A tombstone carries identity only; there is no row left to describe.
        rows[1].PayloadJson.Should().BeNull();
    }

    [Theory]
    [InlineData(TimelineProjectionEntity.Event, SyncEntityType.Event)]
    [InlineData(TimelineProjectionEntity.Era, SyncEntityType.Era)]
    [InlineData(TimelineProjectionEntity.Person, SyncEntityType.Person)]
    [InlineData(TimelineProjectionEntity.PendingEvent, SyncEntityType.PendingEvent)]
    public async Task PublishDeletedAsync_EachEntity_TombstonesUnderItsOwnEntityType(
        TimelineProjectionEntity entity, string expectedEntityType)
    {
        // Arrange
        var entityId = Guid.NewGuid().ToString();

        // Act - a delete does not need the row to still exist
        await _publisher.PublishDeletedAsync(entity, entityId);

        // Assert
        var row = await SingleRowAsync(expectedEntityType);
        row.EntityId.Should().Be(entityId);
        row.Operation.Should().Be(SyncOutboxOperation.Delete);
    }

    [Fact]
    public async Task PublishDeletedAsync_RepeatedDeletion_DoesNotWriteASecondTombstone()
    {
        // Arrange
        var eventId = Guid.NewGuid().ToString();
        await _publisher.PublishDeletedAsync(TimelineProjectionEntity.Event, eventId);

        // Act
        await _publisher.PublishDeletedAsync(TimelineProjectionEntity.Event, eventId);
        await _publisher.PublishDeletedAsync(TimelineProjectionEntity.Event, eventId);

        // Assert - delete is the one operation that stays true on replay
        (await RowsAsync(SyncEntityType.Event)).Should().HaveCount(1);
    }

    [Fact]
    public async Task PublishEventAsync_AfterATombstone_PublishesAgainRatherThanDedupingAgainstIt()
    {
        // Arrange - an id that was tombstoned and then reappeared (an undo, or a
        // re-import). The consumer dropped its copy, so it needs the whole
        // projection again.
        var eventId = await SeedEventAsync();
        await _publisher.PublishEventAsync(eventId);
        await _publisher.PublishDeletedAsync(TimelineProjectionEntity.Event, eventId);

        // Act
        await _publisher.PublishEventAsync(eventId);

        // Assert
        var rows = await RowsAsync(SyncEntityType.Event);
        rows.Select(r => r.Operation).Should().Equal(
            SyncOutboxOperation.Upsert, SyncOutboxOperation.Delete, SyncOutboxOperation.Upsert);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Seeds an event plus the tag/person/location rows and junctions it needs,
    /// so <c>GetByIdWithIncludesAsync</c> has real navigations to denormalise.
    /// </summary>
    private async Task<string> SeedEventAsync(
        Action<Event>? configure = null,
        string[]? tagNames = null,
        string[]? peopleNames = null,
        (string Name, double? Latitude, double? Longitude)[]? locations = null)
    {
        await using var context = _contextFactory.CreateDbContext();

        var entity = new Event
        {
            Title = "Ferry to the island",
            StartDate = new DateTime(2003, 7, 14),
            Category = EventCategory.Travel,
            CreatedAt = new DateTime(2003, 7, 15, 9, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2003, 7, 15, 9, 0, 0, DateTimeKind.Utc),
        };
        configure?.Invoke(entity);
        context.Events.Add(entity);

        foreach (var tagName in tagNames ?? Array.Empty<string>())
        {
            var tag = new Tag { TagName = tagName };
            context.Tags.Add(tag);
            context.EventTags.Add(new EventTag { EventId = entity.EventId, TagId = tag.TagId });
        }

        foreach (var personName in peopleNames ?? Array.Empty<string>())
        {
            var person = new Person { Name = personName };
            context.People.Add(person);
            context.EventPeople.Add(new EventPerson { EventId = entity.EventId, PersonId = person.PersonId });
        }

        foreach (var (name, latitude, longitude) in
                 locations ?? Array.Empty<(string, double?, double?)>())
        {
            var location = new Location { Name = name, Latitude = latitude, Longitude = longitude };
            context.Locations.Add(location);
            context.EventLocations.Add(
                new EventLocation { EventId = entity.EventId, LocationId = location.LocationId });
        }

        await context.SaveChangesAsync();
        return entity.EventId;
    }

    private async Task<List<string>> AllPersonIdsAsync()
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.People.AsNoTracking().Select(p => p.PersonId).ToListAsync();
    }

    private async Task AddTagAsync(string eventId, string tagName)
    {
        await using var context = _contextFactory.CreateDbContext();
        var tag = new Tag { TagName = tagName };
        context.Tags.Add(tag);
        context.EventTags.Add(new EventTag { EventId = eventId, TagId = tag.TagId });
        await context.SaveChangesAsync();
    }

    private async Task<string> SeedEraAsync()
    {
        await using var context = _contextFactory.CreateDbContext();
        var era = new Era
        {
            Name = "University",
            StartDate = new DateTime(2001, 9, 1),
            UpdatedAt = new DateTime(2001, 9, 2, 0, 0, 0, DateTimeKind.Utc),
        };
        context.Eras.Add(era);
        await context.SaveChangesAsync();
        return era.EraId;
    }

    /// <summary>Seeds a device capture with one extracted event awaiting review.</summary>
    private async Task<(string PendingId, string CaptureId)> SeedPendingEventAsync(
        Action<PendingEvent>? configure = null)
    {
        await using var context = _contextFactory.CreateDbContext();

        var queueItem = new RecordingQueue
        {
            AudioFilePath = @"C:\cache\capture.wav",
            Status = QueueStatus.Completed,
            ProcessingStage = QueueProcessingStage.ReviewReady,
            SourceCaptureId = Guid.NewGuid().ToString(),
            SourceDeviceId = "device-1",
            SourcePlatform = CapturePlatform.Ios,
        };
        var pending = new PendingEvent
        {
            QueueId = queueItem.QueueId,
            Title = "Ferry to the island",
            StartDate = new DateTime(2003, 7, 14),
            Category = EventCategory.Travel,
            CreatedAt = new DateTime(2003, 7, 15, 9, 0, 0, DateTimeKind.Utc),
        };
        configure?.Invoke(pending);

        context.RecordingQueues.Add(queueItem);
        context.PendingEvents.Add(pending);
        await context.SaveChangesAsync();

        return (pending.PendingId, queueItem.SourceCaptureId!);
    }

    private static TPayload Deserialize<TPayload>(SyncOutbox row) =>
        JsonSerializer.Deserialize<TPayload>(row.PayloadJson!, WireJson)!;

    private async Task<List<SyncOutbox>> RowsAsync(string entityType)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.SyncOutboxEntries.AsNoTracking()
            .Where(o => o.EntityType == entityType)
            .OrderBy(o => o.OutboxId)
            .ToListAsync();
    }

    private async Task<SyncOutbox> SingleRowAsync(string entityType)
    {
        var rows = await RowsAsync(entityType);
        rows.Should().HaveCount(1);
        return rows[0];
    }

    private async Task<List<TPayload>> PayloadsAsync<TPayload>(string entityType)
    {
        var rows = await RowsAsync(entityType);
        return rows.Select(Deserialize<TPayload>).ToList();
    }

    #endregion
}
