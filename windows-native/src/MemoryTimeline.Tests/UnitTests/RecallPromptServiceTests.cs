using FluentAssertions;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="RecallPromptService"/>: gap generation (density gaps,
/// dangling people, era edges, thin events), the no-LLM-key template fallback,
/// dedupe against dismissed rows, the 14-day snooze, and both answer paths
/// (typed text through a REAL QueueService, and linking an externally created
/// queue row).
/// </summary>
public class RecallPromptServiceTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly RecallPromptRepository _promptRepository;
    private readonly RecordingQueueRepository _queueRepository;
    private readonly QueueService _queueService;
    private readonly AnalyticsService _analyticsService;
    private readonly Mock<ILlmService> _llmServiceMock;
    private readonly RecallPromptService _service;

    public RecallPromptServiceTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _promptRepository = new RecallPromptRepository(_contextFactory);
        _queueRepository = new RecordingQueueRepository(_contextFactory);

        // Real QueueService over the same store so StartAnsweringAsync creates
        // a REAL queue row; the processing-side dependencies are mocked.
        _queueService = new QueueService(
            _queueRepository,
            new Mock<IEventExtractionService>().Object,
            new Mock<ILogger<QueueService>>().Object,
            new Mock<INotificationService>().Object);

        _analyticsService = new AnalyticsService(
            _contextFactory,
            new Mock<ILogger<AnalyticsService>>().Object);

        // Default: NO API key. Question generation must fall back to the
        // deterministic per-kind templates without crashing.
        _llmServiceMock = new Mock<ILlmService>();
        _llmServiceMock
            .Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConfigurationException("Anthropic API key not configured"));

        _service = new RecallPromptService(
            _promptRepository,
            _queueRepository,
            _queueService,
            _analyticsService,
            _llmServiceMock.Object,
            _contextFactory,
            new Mock<ILogger<RecallPromptService>>().Object);
    }

    #region Density gap detection

    [Fact]
    public async Task GetPromptsAsync_MultiYearGap_GeneratesDensityGapPromptNamingPeriod()
    {
        // Arrange - 2 events per year across 2000-2010 with a hole 2003-2006
        // (14 events total; median non-zero yearly count 2, threshold 0.4)
        await SeedYearsAsync(2, 2000, 2001, 2002, 2007, 2008, 2009, 2010);

        // Act - LLM throws ConfigurationException (no key), so the question
        // must come from the deterministic template
        var prompts = await _service.GetPromptsAsync();

        // Assert - the gap prompt names the period
        prompts.Should().Contain(p => p.Kind == RecallPromptKind.DensityGap);
        var gap = prompts.First(p => p.Kind == RecallPromptKind.DensityGap);
        gap.Question.Should().Contain("2003");
        gap.Question.Should().Contain("2006");
        gap.AnchorStart.Should().Be(new DateTime(2003, 1, 1));
        gap.AnchorEnd.Should().Be(new DateTime(2006, 12, 31));
        gap.EntityName.Should().Be("2003-2006");
        gap.Status.Should().Be(RecallPromptStatus.Active);
    }

    [Fact]
    public async Task GetPromptsAsync_ArchiveBelowTenEvents_SkipsGapDetection()
    {
        // Arrange - a clear interior hole (1991-1994) but only 4 events total
        await SeedYearsAsync(2, 1990, 1995);

        // Act
        var prompts = await _service.GetPromptsAsync();

        // Assert - too little data for gap detection
        prompts.Should().NotContain(p => p.Kind == RecallPromptKind.DensityGap);
    }

    [Fact]
    public async Task GetPromptsAsync_CalledTwice_DoesNotDuplicatePrompts()
    {
        // Arrange
        await SeedYearsAsync(2, 2000, 2001, 2002, 2007, 2008, 2009, 2010);

        // Act
        await _service.GetPromptsAsync();
        await _service.GetPromptsAsync();

        // Assert - the same gap produced exactly one row
        await using var context = _contextFactory.CreateDbContext();
        context.RecallPrompts.Count(p => p.Kind == RecallPromptKind.DensityGap)
            .Should().Be(1);
    }

    #endregion

    #region Dangling person / thin event / era edge generation

    [Fact]
    public async Task GetPromptsAsync_DanglingPerson_GeneratesPromptForSingleLinkPersonOnly()
    {
        // Arrange - Alice linked to exactly 1 event, Bob to 2
        var event1 = await SeedEventAsync("Trip to the coast", new DateTime(1998, 4, 10), LongDescription);
        var event2 = await SeedEventAsync("Birthday dinner", new DateTime(1999, 2, 20), LongDescription);
        var aliceId = await SeedPersonAsync("Alice Anderson", event1);
        var bobId = await SeedPersonAsync("Bob Brown", event1, event2);

        // Act
        var prompts = await _service.GetPromptsAsync();

        // Assert
        prompts.Should().Contain(p => p.Kind == RecallPromptKind.DanglingPerson && p.EntityId == aliceId);
        prompts.Should().NotContain(p => p.Kind == RecallPromptKind.DanglingPerson && p.EntityId == bobId);

        var alicePrompt = prompts.First(p => p.Kind == RecallPromptKind.DanglingPerson);
        alicePrompt.Question.Should().Contain("Alice Anderson");
        alicePrompt.EntityName.Should().Be("Alice Anderson");
    }

    [Fact]
    public async Task GetPromptsAsync_ThinEvent_GeneratesForShortDescriptionOnly()
    {
        // Arrange - one detail-less event, one well-described event
        var thinId = await SeedEventAsync("Grandma's summer house", new DateTime(1998, 6, 15), description: null);
        var richId = await SeedEventAsync("Graduation day", new DateTime(1999, 5, 30), LongDescription);

        // Act
        var prompts = await _service.GetPromptsAsync();

        // Assert
        prompts.Should().Contain(p => p.Kind == RecallPromptKind.ThinEvent && p.EntityId == thinId);
        prompts.Should().NotContain(p => p.Kind == RecallPromptKind.ThinEvent && p.EntityId == richId);

        var thinPrompt = prompts.First(p => p.Kind == RecallPromptKind.ThinEvent);
        thinPrompt.Question.Should().Contain("Grandma's summer house");
    }

    [Fact]
    public async Task GetPromptsAsync_EraEdges_GeneratesForEmptyAndQuietOngoingEras()
    {
        // Arrange - an era with zero events, an ongoing era whose only event is
        // years old, and a closed era with an event (no prompt expected)
        var emptyEraId = await SeedEraAsync("College years", new DateTime(2005, 9, 1), new DateTime(2008, 6, 30));
        var quietEraId = await SeedEraAsync("Berlin life", new DateTime(2009, 1, 1), endDate: null);
        var closedEraId = await SeedEraAsync("First job", new DateTime(2013, 1, 1), new DateTime(2015, 12, 31));
        await SeedEventAsync("Moved to Berlin", new DateTime(2010, 3, 1), LongDescription, eraId: quietEraId);
        await SeedEventAsync("Started the job", new DateTime(2014, 2, 1), LongDescription, eraId: closedEraId);

        // Act
        var prompts = await _service.GetPromptsAsync();

        // Assert
        prompts.Should().Contain(p => p.Kind == RecallPromptKind.EraEdge && p.EntityId == emptyEraId);
        prompts.Should().Contain(p => p.Kind == RecallPromptKind.EraEdge && p.EntityId == quietEraId);
        prompts.Should().NotContain(p => p.Kind == RecallPromptKind.EraEdge && p.EntityId == closedEraId);

        prompts.First(p => p.EntityId == emptyEraId).Question.Should().Contain("College years");
        prompts.First(p => p.EntityId == quietEraId).Question.Should().Contain("Berlin life");
    }

    [Fact]
    public async Task GetPromptsAsync_LlmAvailable_UsesModelQuestionText()
    {
        // Arrange - the LLM answers; its text should be used instead of the template
        _llmServiceMock
            .Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("What do you and Alice still laugh about from that trip?");

        var eventId = await SeedEventAsync("Trip to the coast", new DateTime(1998, 4, 10), LongDescription);
        await SeedPersonAsync("Alice Anderson", eventId);

        // Act
        var prompts = await _service.GetPromptsAsync();

        // Assert
        var alicePrompt = prompts.First(p => p.Kind == RecallPromptKind.DanglingPerson);
        alicePrompt.Question.Should().Be("What do you and Alice still laugh about from that trip?");
    }

    #endregion

    #region Dismissal and snooze

    [Fact]
    public async Task DismissAsync_NotInterested_MarksDismissed_AndNeverRegenerates()
    {
        // Arrange - generate the gap prompt once
        await SeedYearsAsync(2, 2000, 2001, 2002, 2007, 2008, 2009, 2010);
        var prompts = await _service.GetPromptsAsync();
        var gap = prompts.First(p => p.Kind == RecallPromptKind.DensityGap);

        // Act - permanently dismiss, then ask for prompts again (which would
        // regenerate the gap if the dedupe ignored dismissed rows)
        await _service.DismissAsync(gap.PromptId, DismissReason.NotInterested);
        var promptsAfter = await _service.GetPromptsAsync();

        // Assert - gone from rotation and not regenerated
        promptsAfter.Should().NotContain(p => p.Kind == RecallPromptKind.DensityGap);

        await using var context = _contextFactory.CreateDbContext();
        var rows = context.RecallPrompts.Where(p => p.Kind == RecallPromptKind.DensityGap).ToList();
        rows.Should().HaveCount(1);
        rows[0].Status.Should().Be(RecallPromptStatus.Dismissed);
        rows[0].DismissReason.Should().Be(DismissReason.NotInterested);
    }

    [Fact]
    public async Task DismissAsync_AlreadyCovered_MarksDismissedPermanently()
    {
        // Arrange
        var prompt = await _promptRepository.AddAsync(new RecallPrompt
        {
            Kind = RecallPromptKind.ThinEvent,
            Question = "What do you remember about it?",
            EntityId = "event-1"
        });

        // Act
        await _service.DismissAsync(prompt.PromptId, DismissReason.AlreadyCovered);

        // Assert
        var stored = await _promptRepository.GetByIdAsync(prompt.PromptId);
        stored!.Status.Should().Be(RecallPromptStatus.Dismissed);
        stored.DismissReason.Should().Be(DismissReason.AlreadyCovered);
        stored.SnoozedUntil.Should().BeNull();

        (await _promptRepository.GetActiveAsync(DateTime.UtcNow.AddYears(10)))
            .Should().NotContain(p => p.PromptId == prompt.PromptId);
    }

    [Fact]
    public async Task DismissAsync_Later_SnoozesFourteenDays_ThenReturnsToRotation()
    {
        // Arrange
        var prompt = await _promptRepository.AddAsync(new RecallPrompt
        {
            Kind = RecallPromptKind.DanglingPerson,
            Question = "How did you meet Alice?",
            EntityId = "person-1"
        });

        // Act
        await _service.DismissAsync(prompt.PromptId, DismissReason.Later);

        // Assert - still Active but snoozed ~14 days out
        var stored = await _promptRepository.GetByIdAsync(prompt.PromptId);
        stored!.Status.Should().Be(RecallPromptStatus.Active);
        stored.DismissReason.Should().Be(DismissReason.Later);
        stored.SnoozedUntil.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(1));

        // Hidden now, back in rotation after the snooze passes (the repository
        // takes 'now' as a parameter precisely so this is testable)
        (await _promptRepository.GetActiveAsync(DateTime.UtcNow))
            .Should().NotContain(p => p.PromptId == prompt.PromptId);
        (await _promptRepository.GetActiveAsync(DateTime.UtcNow.AddDays(15)))
            .Should().Contain(p => p.PromptId == prompt.PromptId);
    }

    #endregion

    #region Answer paths

    [Fact]
    public async Task StartAnsweringAsync_CreatesQueueRowLabeledWithQuestion_AndMarksAnswered()
    {
        // Arrange
        var prompt = await _promptRepository.AddAsync(new RecallPrompt
        {
            Kind = RecallPromptKind.DensityGap,
            Question = "What were you doing between 2003 and 2006?"
        });

        // Act - typed path goes through the REAL QueueService
        var queueId = await _service.StartAnsweringAsync(prompt.PromptId, "I was teaching English in Prague.");

        // Assert - a real Text queue row exists, labeled with the question
        await using var context = _contextFactory.CreateDbContext();
        var row = context.RecordingQueues.Single();
        row.QueueId.Should().Be(queueId);
        row.SourceType.Should().Be(QueueSourceType.Text);
        row.SourceLabel.Should().Be("What were you doing between 2003 and 2006?");
        row.Transcript.Should().Be("I was teaching English in Prague.");
        row.AudioFilePath.Should().Be(string.Empty);
        row.Status.Should().Be(QueueStatus.Pending);

        // ...and the prompt is linked + Answered (out of rotation)
        var stored = await _promptRepository.GetByIdAsync(prompt.PromptId);
        stored!.Status.Should().Be(RecallPromptStatus.Answered);
        stored.QueueId.Should().Be(queueId);
        (await _promptRepository.GetActiveAsync(DateTime.UtcNow))
            .Should().NotContain(p => p.PromptId == prompt.PromptId);
    }

    [Fact]
    public async Task StartAnsweringAsync_EmptyText_Throws_AndLeavesPromptActive()
    {
        // Arrange
        var prompt = await _promptRepository.AddAsync(new RecallPrompt
        {
            Kind = RecallPromptKind.ThinEvent,
            Question = "What do you remember about it?",
            EntityId = "event-1"
        });

        // Act
        var act = () => _service.StartAnsweringAsync(prompt.PromptId, "   ");

        // Assert - the queue rejects empty text before the prompt is touched
        await act.Should().ThrowAsync<ArgumentException>();

        var stored = await _promptRepository.GetByIdAsync(prompt.PromptId);
        stored!.Status.Should().Be(RecallPromptStatus.Active);
        stored.QueueId.Should().BeNull();
    }

    [Fact]
    public async Task StartAnsweringAsync_UnknownPrompt_ThrowsInvalidOperation()
    {
        var act = () => _service.StartAnsweringAsync("missing-prompt", "Some answer text.");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task MarkAnsweredAsync_StampsSourceLabel_LinksAndMarksAnswered()
    {
        // Arrange - an externally created (recorded) queue row without a label
        await _queueRepository.AddAsync(new RecordingQueue
        {
            QueueId = "queue-rec-1",
            AudioFilePath = "C:\\audio\\rec.wav",
            Status = QueueStatus.Pending
        });
        var prompt = await _promptRepository.AddAsync(new RecallPrompt
        {
            Kind = RecallPromptKind.AnniversaryAnchor,
            Question = "What else was happening around then?",
            EntityId = "event-2"
        });

        // Act - record path
        await _service.MarkAnsweredAsync(prompt.PromptId, "queue-rec-1");

        // Assert - the row is stamped with the question and the prompt linked
        var row = await _queueRepository.GetByIdAsync("queue-rec-1");
        row!.SourceLabel.Should().Be("What else was happening around then?");

        var stored = await _promptRepository.GetByIdAsync(prompt.PromptId);
        stored!.Status.Should().Be(RecallPromptStatus.Answered);
        stored.QueueId.Should().Be("queue-rec-1");
    }

    [Fact]
    public async Task MarkAnsweredAsync_ExistingSourceLabel_IsNotOverwritten()
    {
        // Arrange
        await _queueRepository.AddAsync(new RecordingQueue
        {
            QueueId = "queue-rec-2",
            AudioFilePath = "C:\\audio\\rec2.wav",
            SourceLabel = "My own label",
            Status = QueueStatus.Pending
        });
        var prompt = await _promptRepository.AddAsync(new RecallPrompt
        {
            Kind = RecallPromptKind.EraEdge,
            Question = "What has been happening lately?",
            EntityId = "era-1"
        });

        // Act
        await _service.MarkAnsweredAsync(prompt.PromptId, "queue-rec-2");

        // Assert
        var row = await _queueRepository.GetByIdAsync("queue-rec-2");
        row!.SourceLabel.Should().Be("My own label");
        (await _promptRepository.GetByIdAsync(prompt.PromptId))!.Status
            .Should().Be(RecallPromptStatus.Answered);
    }

    [Fact]
    public async Task MarkAnsweredAsync_MissingQueueItem_Throws_AndLeavesPromptActive()
    {
        // Arrange
        var prompt = await _promptRepository.AddAsync(new RecallPrompt
        {
            Kind = RecallPromptKind.DensityGap,
            Question = "What was happening in 1993?"
        });

        // Act
        var act = () => _service.MarkAnsweredAsync(prompt.PromptId, "missing-queue");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _promptRepository.GetByIdAsync(prompt.PromptId))!.Status
            .Should().Be(RecallPromptStatus.Active);
    }

    #endregion

    #region Seed helpers

    /// <summary>A description comfortably longer than the 40-char "thin" cutoff.</summary>
    private const string LongDescription =
        "A long, detailed description that easily exceeds the forty character thin-event cutoff.";

    /// <summary>Seeds <paramref name="perYear"/> well-described events in each given year.</summary>
    private async Task SeedYearsAsync(int perYear, params int[] years)
    {
        await using var context = _contextFactory.CreateDbContext();
        foreach (var year in years)
        {
            for (var i = 0; i < perYear; i++)
            {
                context.Events.Add(new Event
                {
                    Title = $"Event {year}-{i}",
                    StartDate = new DateTime(year, 1 + i, 15),
                    Description = LongDescription,
                    Category = EventCategory.Other
                });
            }
        }

        await context.SaveChangesAsync();
    }

    private async Task<string> SeedEventAsync(string title, DateTime startDate, string? description, string? eraId = null)
    {
        await using var context = _contextFactory.CreateDbContext();
        var evt = new Event
        {
            Title = title,
            StartDate = startDate,
            Description = description,
            Category = EventCategory.Other,
            EraId = eraId
        };
        context.Events.Add(evt);
        await context.SaveChangesAsync();
        return evt.EventId;
    }

    private async Task<string> SeedPersonAsync(string name, params string[] eventIds)
    {
        await using var context = _contextFactory.CreateDbContext();
        var person = new Person { Name = name };
        context.People.Add(person);
        foreach (var eventId in eventIds)
        {
            context.EventPeople.Add(new EventPerson { EventId = eventId, PersonId = person.PersonId });
        }

        await context.SaveChangesAsync();
        return person.PersonId;
    }

    private async Task<string> SeedEraAsync(string name, DateTime startDate, DateTime? endDate)
    {
        await using var context = _contextFactory.CreateDbContext();
        var era = new Era
        {
            Name = name,
            StartDate = startDate,
            EndDate = endDate
        };
        context.Eras.Add(era);
        await context.SaveChangesAsync();
        return era.EraId;
    }

    #endregion

    public void Dispose()
    {
        using var context = _contextFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }
}
