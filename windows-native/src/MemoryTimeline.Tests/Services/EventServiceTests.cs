using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.Services;

public class EventServiceTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly IEventRepository _repository;
    private readonly IEventService _eventService;
    private readonly Mock<ILogger<EventService>> _loggerMock;

    public EventServiceTests()
    {
        // Create factory over a uniquely named in-memory database; every context
        // created from it shares the same store.
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _repository = new EventRepository(_contextFactory);
        _loggerMock = new Mock<ILogger<EventService>>();
        _eventService = new EventService(_repository, _contextFactory, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateEventAsync_ValidEvent_ReturnsEventWithId()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "Test Event",
            StartDate = DateTime.UtcNow,
            Description = "Test Description",
            Category = EventCategory.Milestone
        };

        // Act
        var result = await _eventService.CreateEventAsync(newEvent);

        // Assert
        result.Should().NotBeNull();
        result.EventId.Should().NotBeNullOrEmpty();
        result.Title.Should().Be("Test Event");
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateEventAsync_NullTitle_ThrowsArgumentException()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "",
            StartDate = DateTime.UtcNow
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _eventService.CreateEventAsync(newEvent));
    }

    [Fact]
    public async Task GetEventByIdAsync_ExistingEvent_ReturnsEvent()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "Find Me",
            StartDate = DateTime.UtcNow,
            Category = EventCategory.Work
        };
        var created = await _eventService.CreateEventAsync(newEvent);

        // Act
        var result = await _eventService.GetEventByIdAsync(created.EventId);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Find Me");
    }

    [Fact]
    public async Task GetEventByIdAsync_NonExistentEvent_ReturnsNull()
    {
        // Act
        var result = await _eventService.GetEventByIdAsync("non-existent-id");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateEventAsync_ExistingEvent_UpdatesSuccessfully()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "Original Title",
            StartDate = DateTime.UtcNow,
            Category = EventCategory.Education
        };
        var created = await _eventService.CreateEventAsync(newEvent);

        // Act
        created.Title = "Updated Title";
        created.Description = "New Description";
        var updated = await _eventService.UpdateEventAsync(created);

        // Assert
        updated.Title.Should().Be("Updated Title");
        updated.Description.Should().Be("New Description");

        var fetched = await _eventService.GetEventByIdAsync(created.EventId);
        fetched!.Title.Should().Be("Updated Title");
    }

    [Fact]
    public async Task DeleteEventAsync_ExistingEvent_DeletesSuccessfully()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "Delete Me",
            StartDate = DateTime.UtcNow
        };
        var created = await _eventService.CreateEventAsync(newEvent);

        // Act
        await _eventService.DeleteEventAsync(created.EventId);

        // Assert
        var result = await _eventService.GetEventByIdAsync(created.EventId);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetEventsByDateRangeAsync_MultipleEvents_ReturnsCorrectEvents()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 12, 31);

        var event1 = new Event { Title = "Event 2024", StartDate = new DateTime(2024, 6, 15) };
        var event2 = new Event { Title = "Event 2023", StartDate = new DateTime(2023, 6, 15) };
        var event3 = new Event { Title = "Event 2025", StartDate = new DateTime(2025, 6, 15) };

        await _eventService.CreateEventAsync(event1);
        await _eventService.CreateEventAsync(event2);
        await _eventService.CreateEventAsync(event3);

        // Act
        var results = await _eventService.GetEventsByDateRangeAsync(startDate, endDate);

        // Assert
        results.Should().HaveCount(1);
        results.First().Title.Should().Be("Event 2024");
    }

    [Fact]
    public async Task GetTotalEventCountAsync_AfterCreatingEvents_ReturnsCorrectCount()
    {
        // Arrange
        await _eventService.CreateEventAsync(new Event { Title = "Event 1", StartDate = DateTime.UtcNow });
        await _eventService.CreateEventAsync(new Event { Title = "Event 2", StartDate = DateTime.UtcNow });
        await _eventService.CreateEventAsync(new Event { Title = "Event 3", StartDate = DateTime.UtcNow });

        // Act
        var count = await _eventService.GetTotalEventCountAsync();

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task SearchEventsAsync_WithMatchingTitle_ReturnsEvents()
    {
        // Arrange
        await _eventService.CreateEventAsync(new Event { Title = "Important Meeting", StartDate = DateTime.UtcNow });
        await _eventService.CreateEventAsync(new Event { Title = "Regular Checkup", StartDate = DateTime.UtcNow });
        await _eventService.CreateEventAsync(new Event { Title = "Another Important Call", StartDate = DateTime.UtcNow });

        // Act
        var results = await _eventService.SearchEventsAsync("Important");

        // Assert
        results.Should().HaveCount(2);
        results.All(e => e.Title.Contains("Important")).Should().BeTrue();
    }

    [Fact]
    public async Task CreateEventAsync_EndDateBeforeStartDate_ThrowsArgumentException()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "Bad Range",
            StartDate = new DateTime(2024, 6, 15),
            EndDate = new DateTime(2024, 6, 10) // Before start date
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _eventService.CreateEventAsync(newEvent));
    }

    [Fact]
    public async Task CreateEventAsync_InvalidCategory_ThrowsArgumentException()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "Bad Category",
            StartDate = DateTime.UtcNow,
            Category = "not-a-real-category"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _eventService.CreateEventAsync(newEvent));
    }

    [Fact]
    public async Task CreateEventAsync_MixedCaseCategory_IsAcceptedAndNormalizedToLowercase()
    {
        // Category validation is now case-insensitive and categories are normalized
        // to their canonical lowercase form on create.
        var newEvent = new Event
        {
            Title = "Mixed Case Category",
            StartDate = DateTime.UtcNow,
            Category = "WoRk"
        };

        // Act
        var result = await _eventService.CreateEventAsync(newEvent);

        // Assert
        result.Category.Should().Be(EventCategory.Work); // "work"

        var fetched = await _eventService.GetEventByIdAsync(result.EventId);
        fetched!.Category.Should().Be(EventCategory.Work);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchEventsAsync_EmptyOrWhitespaceTerm_ReturnsEmpty(string term)
    {
        // Arrange
        await _eventService.CreateEventAsync(new Event { Title = "Some Event", StartDate = DateTime.UtcNow });

        // Act
        var results = await _eventService.SearchEventsAsync(term);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTotalEventCountAsync_NoEvents_ReturnsZero()
    {
        // Act
        var count = await _eventService.GetTotalEventCountAsync();

        // Assert
        count.Should().Be(0);
    }

    #region Timeline projection publishing

    [Fact]
    public async Task CreateEventAsync_WithProjectionPublisher_PublishesTheNewEvent()
    {
        // Arrange
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);

        // Act
        var created = await service.CreateEventAsync(new Event
        {
            Title = "First Memory",
            StartDate = DateTime.UtcNow
        });

        // Assert
        publisher.Verify(
            p => p.PublishEventAsync(created.EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateEventAsync_WithProjectionPublisher_PublishesTheSameEvent()
    {
        // Arrange
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var created = await service.CreateEventAsync(new Event
        {
            Title = "Original Title",
            StartDate = DateTime.UtcNow
        });
        publisher.Invocations.Clear(); // the create's own publish is not what this asserts

        // Act
        created.Title = "Updated Title";
        await service.UpdateEventAsync(created);

        // Assert
        publisher.Verify(
            p => p.PublishEventAsync(created.EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteEventAsync_WithProjectionPublisher_PublishesATombstoneNotAnUpsert()
    {
        // Arrange
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var created = await service.CreateEventAsync(new Event
        {
            Title = "Delete Me",
            StartDate = DateTime.UtcNow
        });
        publisher.Invocations.Clear();

        // Act
        await service.DeleteEventAsync(created.EventId);

        // Assert
        publisher.Verify(
            p => p.PublishDeletedAsync(
                TimelineProjectionEntity.Event, created.EventId, It.IsAny<CancellationToken>()),
            Times.Once);

        // An upsert here would find no row and project nothing, leaving the
        // companion device showing a memory the user deleted.
        publisher.Verify(
            p => p.PublishEventAsync(created.EventId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateEventAsync_ProjectionPublisherThrows_StillPersistsTheEvent()
    {
        // Arrange - an outbox that is full, locked or misconfigured
        var publisher = new Mock<ITimelineProjectionPublisher>();
        publisher
            .Setup(p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));
        var service = CreateServiceWithPublisher(publisher.Object);

        // Act - the archive write already committed, so it must survive
        var created = await service.CreateEventAsync(new Event
        {
            Title = "Survives A Failed Publish",
            StartDate = DateTime.UtcNow
        });

        // Assert
        var fetched = await service.GetEventByIdAsync(created.EventId);
        fetched.Should().NotBeNull();
        fetched!.Title.Should().Be("Survives A Failed Publish");
    }

    [Fact]
    public async Task DeleteEventAsync_ProjectionPublisherThrows_StillDeletesTheEvent()
    {
        // Arrange
        var publisher = CreatePublisherMock();
        publisher
            .Setup(p => p.PublishDeletedAsync(
                It.IsAny<TimelineProjectionEntity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));
        var service = CreateServiceWithPublisher(publisher.Object);
        var created = await service.CreateEventAsync(new Event
        {
            Title = "Gone Regardless",
            StartDate = DateTime.UtcNow
        });

        // Act
        await service.DeleteEventAsync(created.EventId);

        // Assert - the failed tombstone must not resurrect the row
        var fetched = await service.GetEventByIdAsync(created.EventId);
        fetched.Should().BeNull();
    }

    [Fact]
    public async Task AddTagToEventAsync_WithProjectionPublisher_PublishesTheEvent()
    {
        // Arrange - tags are denormalised into the event projection, so linking
        // one changes what a phone renders even though no event column moved
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var created = await service.CreateEventAsync(new Event
        {
            Title = "Tagged Event",
            StartDate = DateTime.UtcNow
        });
        var tagId = await SeedTagAsync("holiday");
        publisher.Invocations.Clear();

        // Act
        await service.AddTagToEventAsync(created.EventId, tagId);

        // Assert
        publisher.Verify(
            p => p.PublishEventAsync(created.EventId, It.IsAny<CancellationToken>()), Times.Once);

        // Re-adding the same tag writes nothing, so there is no new state to
        // project either.
        await service.AddTagToEventAsync(created.EventId, tagId);
        publisher.Verify(
            p => p.PublishEventAsync(created.EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveTagFromEventAsync_WithProjectionPublisher_PublishesTheEvent()
    {
        // Arrange
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var created = await service.CreateEventAsync(new Event
        {
            Title = "Tagged Event",
            StartDate = DateTime.UtcNow
        });
        var tagId = await SeedTagAsync("holiday");
        await service.AddTagToEventAsync(created.EventId, tagId);
        publisher.Invocations.Clear();

        // Act
        await service.RemoveTagFromEventAsync(created.EventId, tagId);

        // Assert
        publisher.Verify(
            p => p.PublishEventAsync(created.EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddPersonToEventAsync_WithProjectionPublisher_PublishesTheEvent()
    {
        // Arrange
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var created = await service.CreateEventAsync(new Event
        {
            Title = "Event With People",
            StartDate = DateTime.UtcNow
        });
        var personId = await SeedPersonAsync("Sarah");
        publisher.Invocations.Clear();

        // Act
        await service.AddPersonToEventAsync(created.EventId, personId);

        // Assert - who was there is half of what a timeline entry says
        publisher.Verify(
            p => p.PublishEventAsync(created.EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddPersonToEventAsync_PublishesTheEventThenThePersonWhoseCountMoved()
    {
        // Arrange - a person projection's EventCount is derived from the
        // event_people junction this method writes, so publishing only the
        // event leaves the companion's "Dana — 4 memories" frozen at four
        var projected = new List<string>();
        var publisher = CreateRecordingPublisherMock(projected);
        var service = CreateServiceWithPublisher(publisher.Object);
        var created = await service.CreateEventAsync(new Event
        {
            Title = "Event With People",
            StartDate = DateTime.UtcNow
        });
        var personId = await SeedPersonAsync("Dana");
        projected.Clear();

        // Act
        await service.AddPersonToEventAsync(created.EventId, personId);

        // Assert - both, and the event first. Outbox delivery is ordered, so a
        // batch that truncates should leave a count trailing the timeline
        // rather than a count that disagrees with the events sitting next to it.
        projected.Should().Equal($"event:{created.EventId}", $"person:{personId}");
    }

    [Fact]
    public async Task RemovePersonFromEventAsync_PublishesTheEventThenThePersonWhoseCountMoved()
    {
        // Arrange
        var projected = new List<string>();
        var publisher = CreateRecordingPublisherMock(projected);
        var service = CreateServiceWithPublisher(publisher.Object);
        var created = await service.CreateEventAsync(new Event
        {
            Title = "Event With People",
            StartDate = DateTime.UtcNow
        });
        var personId = await SeedPersonAsync("Dana");
        await service.AddPersonToEventAsync(created.EventId, personId);
        projected.Clear();

        // Act
        await service.RemovePersonFromEventAsync(created.EventId, personId);

        // Assert - unlinking moves the count exactly as linking did
        projected.Should().Equal($"event:{created.EventId}", $"person:{personId}");
    }

    [Fact]
    public async Task AddPersonToEventAsync_AlreadyLinked_PublishesNeitherASecondTime()
    {
        // Arrange - the cost bound for the person publishes: they are paid per
        // junction row that actually moved, and a re-link writes nothing
        var projected = new List<string>();
        var publisher = CreateRecordingPublisherMock(projected);
        var service = CreateServiceWithPublisher(publisher.Object);
        var created = await service.CreateEventAsync(new Event
        {
            Title = "Event With People",
            StartDate = DateTime.UtcNow
        });
        var personId = await SeedPersonAsync("Dana");
        await service.AddPersonToEventAsync(created.EventId, personId);
        projected.Clear();

        // Act
        await service.AddPersonToEventAsync(created.EventId, personId);

        // Assert
        projected.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateEventAsync_WithLinkedPeople_PublishesTheEventAndNobodyElse()
    {
        // Arrange - the other half of the cost bound. Only the paths that WRITE
        // event_people publish people, so an event with many of them still
        // costs one outbox row for an ordinary edit; a title never moved a count.
        var projected = new List<string>();
        var publisher = CreateRecordingPublisherMock(projected);
        var service = CreateServiceWithPublisher(publisher.Object);
        var created = await service.CreateEventAsync(new Event
        {
            Title = "Original Title",
            StartDate = DateTime.UtcNow
        });
        foreach (var name in new[] { "Dana", "Sarah", "Robert" })
        {
            await service.AddPersonToEventAsync(created.EventId, await SeedPersonAsync(name));
        }
        projected.Clear();

        // Act
        created.Title = "Updated Title";
        await service.UpdateEventAsync(created);

        // Assert
        projected.Should().Equal($"event:{created.EventId}");
    }

    [Fact]
    public async Task NoProjectionPublisher_CrudAndAssociationsStillWork()
    {
        // The fixture's service is built WITHOUT a publisher — the shape every
        // pre-existing caller uses, and the shape MS DI produces wherever the
        // sync assembly is not wired up.
        var created = await _eventService.CreateEventAsync(new Event
        {
            Title = "Unpublished",
            StartDate = DateTime.UtcNow
        });
        var tagId = await SeedTagAsync("private");

        await _eventService.AddTagToEventAsync(created.EventId, tagId);
        var personId = await SeedPersonAsync("Unprojected");
        await _eventService.AddPersonToEventAsync(created.EventId, personId);
        created.Title = "Still Unpublished";
        await _eventService.UpdateEventAsync(created);
        await _eventService.RemoveTagFromEventAsync(created.EventId, tagId);
        await _eventService.RemovePersonFromEventAsync(created.EventId, personId);
        await _eventService.DeleteEventAsync(created.EventId);

        var fetched = await _eventService.GetEventByIdAsync(created.EventId);
        fetched.Should().BeNull();
    }

    /// <summary>
    /// The fixture's service plus a projection publisher; everything else is
    /// shared so these tests read against the same in-memory archive.
    /// </summary>
    private IEventService CreateServiceWithPublisher(ITimelineProjectionPublisher publisher) =>
        new EventService(_repository, _contextFactory, _loggerMock.Object, projectionPublisher: publisher);

    private static Mock<ITimelineProjectionPublisher> CreatePublisherMock()
    {
        var publisher = new Mock<ITimelineProjectionPublisher>();
        publisher
            .Setup(p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher
            .Setup(p => p.PublishDeletedAsync(
                It.IsAny<TimelineProjectionEntity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return publisher;
    }

    /// <summary>
    /// A publisher that appends every call to <paramref name="projected"/> as a
    /// string. Recorded in ONE list because the order is as much of the contract
    /// as the contents: the outbox delivers in id order, so which of an event
    /// and its people arrives first decides what a half-delivered batch looks
    /// like on a companion.
    /// </summary>
    private static Mock<ITimelineProjectionPublisher> CreateRecordingPublisherMock(List<string> projected)
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
        using var context = _contextFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }
}

/// <summary>
/// The one projection case above that a shared in-memory fixture cannot state:
/// deleting an event must republish the people it linked, and their ids have to
/// be read BEFORE the delete because the junction rows are cascade-deleted.
///
/// <para>This needs a FILE-BASED SQLite database, and that is the whole point
/// rather than an incidental preference. Both of <c>event_people</c>'s foreign
/// keys are configured <c>ON DELETE CASCADE</c> and the repository removes a
/// DETACHED event, so nothing ever loads the junction into the change tracker
/// and SQLite performs the cascade itself. The EF InMemory provider has no
/// foreign keys to enforce and cascades only what is tracked, so it would leave
/// the junction rows sitting there — and a version of this service that read
/// the ids one line too late would pass an in-memory test while publishing
/// nothing at all in production. That is exactly the failure this file is here
/// to catch, so the test has to run where the cascade is real.</para>
/// </summary>
public class EventServiceDeleteCascadeProjectionTests : IDisposable
{
    private readonly string _databasePath;
    private readonly TestDbContextFactory _contextFactory;
    private readonly EventRepository _repository;
    private readonly List<string> _projected = new();
    private readonly Mock<ITimelineProjectionPublisher> _publisher = new();
    private readonly EventService _service;

    public EventServiceDeleteCascadeProjectionTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(), $"EventServiceDeleteCascadeTests_{Guid.NewGuid()}.db");
        _contextFactory = TestDbContextFactory.CreateSqliteFile(_databasePath);
        using (var context = _contextFactory.CreateDbContext())
        {
            context.Database.EnsureCreated();
        }

        _publisher
            .Setup(p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => _projected.Add($"event:{id}"))
            .Returns(Task.CompletedTask);
        _publisher
            .Setup(p => p.PublishPersonAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => _projected.Add($"person:{id}"))
            .Returns(Task.CompletedTask);
        _publisher
            .Setup(p => p.PublishDeletedAsync(
                It.IsAny<TimelineProjectionEntity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<TimelineProjectionEntity, string, CancellationToken>(
                (entity, id, _) => _projected.Add($"delete:{entity}:{id}"))
            .Returns(Task.CompletedTask);

        _repository = new EventRepository(_contextFactory);
        _service = new EventService(
            _repository,
            _contextFactory,
            new Mock<ILogger<EventService>>().Object,
            projectionPublisher: _publisher.Object);
    }

    [Fact]
    public async Task DeleteEventAsync_CapturesLinkedPeopleBeforeTheCascadeAndRepublishesThem()
    {
        // Arrange - one event, three people on it
        var created = await _service.CreateEventAsync(new Event
        {
            Title = "Weekend away",
            StartDate = new DateTime(2024, 7, 1)
        });
        var danaId = await SeedPersonAsync("Dana");
        var sarahId = await SeedPersonAsync("Sarah");
        var robertId = await SeedPersonAsync("Robert");
        foreach (var personId in new[] { danaId, sarahId, robertId })
        {
            await _service.AddPersonToEventAsync(created.EventId, personId);
        }

        _projected.Clear();

        // Act
        await _service.DeleteEventAsync(created.EventId);

        // Assert - the junction really is empty, so the ids could only have come
        // from a read taken before the delete
        await using (var context = _contextFactory.CreateDbContext())
        {
            (await context.EventPeople.AsNoTracking()
                .CountAsync(ep => ep.EventId == created.EventId)).Should().Be(0);
            (await context.People.AsNoTracking().CountAsync()).Should()
                .Be(3, "a cascade takes the links, never the contacts");
        }

        // ...and the tombstone leads: a count that drops while the companion is
        // still drawing the event contradicts data the companion can see, while
        // a count that lags a removed event is the staleness that already existed.
        _projected.Should().HaveCount(4);
        _projected[0].Should().Be($"delete:{TimelineProjectionEntity.Event}:{created.EventId}");
        _projected.Skip(1).Should().BeEquivalentTo(new[]
        {
            $"person:{danaId}", $"person:{sarahId}", $"person:{robertId}"
        });
    }

    [Fact]
    public async Task DeleteEventAsync_WithNoLinkedPeople_PublishesOnlyTheTombstone()
    {
        // Arrange
        var created = await _service.CreateEventAsync(new Event
        {
            Title = "A quiet afternoon",
            StartDate = new DateTime(2024, 7, 1)
        });
        _projected.Clear();

        // Act
        await _service.DeleteEventAsync(created.EventId);

        // Assert - nobody's count moved, so nobody is published
        _projected.Should().Equal($"delete:{TimelineProjectionEntity.Event}:{created.EventId}");
    }

    [Fact]
    public async Task DeleteEventAsync_PersonPublishFails_StillDeletesAndPublishesTheRest()
    {
        // Arrange - each publish is guarded on its own, and the delete has
        // already committed by the time any of them runs
        var created = await _service.CreateEventAsync(new Event
        {
            Title = "Weekend away",
            StartDate = new DateTime(2024, 7, 1)
        });
        var danaId = await SeedPersonAsync("Dana");
        var sarahId = await SeedPersonAsync("Sarah");
        await _service.AddPersonToEventAsync(created.EventId, danaId);
        await _service.AddPersonToEventAsync(created.EventId, sarahId);
        _projected.Clear();
        _publisher
            .Setup(p => p.PublishPersonAsync(danaId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        // Act
        await _service.DeleteEventAsync(created.EventId);

        // Assert - the memory is gone regardless, and Sarah still got published
        (await _service.GetEventByIdAsync(created.EventId)).Should().BeNull();
        _projected.Should().Equal(
            $"delete:{TimelineProjectionEntity.Event}:{created.EventId}",
            $"person:{sarahId}");
    }

    private async Task<string> SeedPersonAsync(string name)
    {
        await using var context = _contextFactory.CreateDbContext();
        var person = new Person
        {
            PersonId = Guid.NewGuid().ToString(),
            Name = name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.People.Add(person);
        await context.SaveChangesAsync();
        return person.PersonId;
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
