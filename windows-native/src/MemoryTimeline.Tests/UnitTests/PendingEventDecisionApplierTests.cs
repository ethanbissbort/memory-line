using System.Text.Json;
using FluentAssertions;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Sync;
using MemoryTimeline.SyncContracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="PendingEventDecisionApplier"/> over the REAL
/// <see cref="EventExtractionService"/> approval path — the same
/// <c>ApprovePendingEventAsync</c> / <c>RejectPendingEventAsync</c> /
/// <c>UpdatePendingEventAsync</c> calls the Review page makes — so these assert
/// that a companion's verdict produces exactly the rows a click on Windows
/// produces, rather than that a second approval implementation behaves the same.
///
/// A file-based SQLite database is used because approval opens a relational
/// transaction (as in <see cref="ReviewCaptureStatusTests"/>).
///
/// The properties that make accepting this one inbound write safe are what is
/// under test: the same decision applied twice creates one event, and a pending
/// event Windows already resolved is dropped rather than re-decided.
/// </summary>
public class PendingEventDecisionApplierTests : IDisposable
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly TestDbContextFactory _contextFactory;
    private readonly PendingEventRepository _pendingEventRepository;
    private readonly EventExtractionService _extractionService;
    private readonly PendingEventDecisionApplier _applier;
    private readonly string _databasePath;

    public PendingEventDecisionApplierTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(), $"PendingEventDecisionApplierTests_{Guid.NewGuid()}.db");
        _contextFactory = TestDbContextFactory.CreateSqliteFile(_databasePath);

        using (var context = _contextFactory.CreateDbContext())
        {
            context.Database.EnsureCreated();
        }

        _pendingEventRepository = new PendingEventRepository(_contextFactory);
        _extractionService = new EventExtractionService(
            new Mock<ILlmService>().Object,
            new Mock<ISpeechToTextService>().Object,
            new Mock<IEventService>().Object,
            new Mock<ISettingsService>().Object,
            new RecordingQueueRepository(_contextFactory),
            new Mock<IPersonService>().Object,
            _contextFactory,
            new Mock<ILogger<EventExtractionService>>().Object);

        _applier = new PendingEventDecisionApplier(
            _pendingEventRepository,
            _extractionService,
            NullLogger<PendingEventDecisionApplier>.Instance);
    }

    #region Approve

    [Fact]
    public async Task ApplyAsync_Approve_CreatesTheEventThroughTheWindowsApprovalPath()
    {
        // Arrange - the extraction payload carries the tags and people the
        // approve transaction is supposed to write alongside the event
        var pendingId = await SeedPendingEventAsync(pending =>
            pending.ExtractedData =
                """
                {"Title":"Ferry to the island","Tags":["ferry"],"People":["Dana"],"Locations":["Harbour Road"]}
                """);

        // Act
        var result = await _applier.ApplyAsync(DecisionChange(pendingId, PendingEventDecision.Approve));

        // Assert
        result.Should().Be(ChangeApplicationResult.Applied);

        await using var context = _contextFactory.CreateDbContext();
        var created = await context.Events.AsNoTracking().SingleAsync();
        created.Title.Should().Be("Ferry to the island");

        // The single approve transaction wrote the junctions too - which is the
        // point of routing through the existing path instead of inserting an
        // event row here.
        (await context.EventTags.CountAsync(et => et.EventId == created.EventId)).Should().Be(1);
        (await context.EventPeople.CountAsync(ep => ep.EventId == created.EventId)).Should().Be(1);

        var pending = await context.PendingEvents.AsNoTracking().SingleAsync();
        pending.IsApproved.Should().BeTrue();
        pending.Status.Should().Be(PendingStatus.Approved.ToStringValue());
        pending.ReviewedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyAsync_SameApprovalTwice_CreatesExactlyOneEvent()
    {
        // Arrange - a replayed change, a phone that retried, two devices that
        // agreed: all the same decision arriving twice.
        var pendingId = await SeedPendingEventAsync();
        var change = DecisionChange(pendingId, PendingEventDecision.Approve);

        // Act
        var first = await _applier.ApplyAsync(change);
        var second = await _applier.ApplyAsync(change);

        // Assert - the replay is dropped, not re-approved
        first.Should().Be(ChangeApplicationResult.Applied);
        second.Should().Be(ChangeApplicationResult.Skipped);

        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_ApprovalOfAnEventWindowsAlreadyApproved_IsDroppedNotReapplied()
    {
        // Arrange - the user approved it on the PC before the phone's decision
        // arrived; the local review is the one that happened in front of them
        var pendingId = await SeedPendingEventAsync();
        await _extractionService.ApprovePendingEventAsync(pendingId);

        // Act
        var result = await _applier.ApplyAsync(DecisionChange(pendingId, PendingEventDecision.Approve));

        // Assert
        result.Should().Be(ChangeApplicationResult.Skipped);

        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_RejectionOfAnEventWindowsAlreadyApproved_NeverReversesTheReview()
    {
        // Arrange - the dangerous direction: a stale phone rejecting a memory
        // the user already kept
        var pendingId = await SeedPendingEventAsync();
        var approved = await _extractionService.ApprovePendingEventAsync(pendingId);

        // Act
        var result = await _applier.ApplyAsync(DecisionChange(pendingId, PendingEventDecision.Reject));

        // Assert - the event survives and the pending row keeps its verdict
        result.Should().Be(ChangeApplicationResult.Skipped);

        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.AsNoTracking().SingleAsync()).EventId.Should().Be(approved.EventId);
        (await context.PendingEvents.AsNoTracking().SingleAsync()).IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_ApprovalOfAnEventWindowsAlreadyRejected_IsDropped()
    {
        // Arrange - rejecting DELETES the pending row, so the decision arrives
        // for something that no longer exists
        var pendingId = await SeedPendingEventAsync();
        await _extractionService.RejectPendingEventAsync(pendingId);

        // Act
        var result = await _applier.ApplyAsync(DecisionChange(pendingId, PendingEventDecision.Approve));

        // Assert - a finished review is not undone by a late approval
        result.Should().Be(ChangeApplicationResult.Skipped);

        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.CountAsync()).Should().Be(0);
    }

    #endregion

    #region Reject

    [Fact]
    public async Task ApplyAsync_Reject_RemovesThePendingEventAndCreatesNothing()
    {
        // Arrange
        var pendingId = await SeedPendingEventAsync();

        // Act
        var result = await _applier.ApplyAsync(DecisionChange(pendingId, PendingEventDecision.Reject));

        // Assert
        result.Should().Be(ChangeApplicationResult.Applied);

        await using var context = _contextFactory.CreateDbContext();
        (await context.PendingEvents.CountAsync()).Should().Be(0);
        (await context.Events.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_SameRejectionTwice_IsIdempotent()
    {
        // Arrange
        var pendingId = await SeedPendingEventAsync();
        var change = DecisionChange(pendingId, PendingEventDecision.Reject);

        // Act
        var first = await _applier.ApplyAsync(change);
        var second = await _applier.ApplyAsync(change);

        // Assert
        first.Should().Be(ChangeApplicationResult.Applied);
        second.Should().Be(ChangeApplicationResult.Skipped);
    }

    [Fact]
    public async Task ApplyAsync_DecisionIsCaseInsensitive()
    {
        // Arrange - the verdict crosses a wire from a client this build does
        // not control
        var pendingId = await SeedPendingEventAsync();

        // Act
        var result = await _applier.ApplyAsync(DecisionChange(pendingId, "Approve"));

        // Assert
        result.Should().Be(ChangeApplicationResult.Applied);

        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.CountAsync()).Should().Be(1);
    }

    #endregion

    #region Corrections

    [Fact]
    public async Task ApplyAsync_ApproveWithCorrections_AppliesThemBeforeApproval()
    {
        // Arrange - the fields a reviewer realistically fixes on a phone
        var pendingId = await SeedPendingEventAsync(pending =>
        {
            pending.Title = "ferry";
            pending.StartDate = new DateTime(2003, 1, 1);
            pending.DatePrecision = DatePrecision.Day;
            pending.Category = EventCategory.Other;
            pending.Description = "The early crossing was rough";
            pending.ConfidenceScore = 0.42;
        });

        var change = DecisionChange(pendingId, PendingEventDecision.Approve, new PendingEventCorrections
        {
            Title = "Ferry to the island",
            StartDate = new DateTime(2003, 7, 14),
            DatePrecision = "season",
            Category = EventCategory.Travel,
        });

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert - the corrections are in the event, not just the pending row
        result.Should().Be(ChangeApplicationResult.Applied);

        await using var context = _contextFactory.CreateDbContext();
        var created = await context.Events.AsNoTracking().SingleAsync();
        created.Title.Should().Be("Ferry to the island");
        created.StartDate.Should().Be(new DateTime(2003, 7, 14));
        created.DatePrecision.Should().Be(DatePrecision.Season);
        created.Category.Should().Be(EventCategory.Travel);

        // Corrections are deliberately NOT general editing: everything outside
        // the four allowed fields survives untouched.
        created.Description.Should().Be("The early crossing was rough");
        created.Confidence.Should().Be(0.42);
    }

    [Fact]
    public async Task ApplyAsync_ApproveWithNoCorrections_ApprovesAsExtracted()
    {
        // Arrange
        var pendingId = await SeedPendingEventAsync(pending => pending.Title = "Ferry to the island");

        // Act
        await _applier.ApplyAsync(DecisionChange(pendingId, PendingEventDecision.Approve));

        // Assert
        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.AsNoTracking().SingleAsync()).Title.Should().Be("Ferry to the island");
    }

    [Fact]
    public async Task ApplyAsync_CorrectionsWithAnUnknownCategory_IgnoreItAndStillApprove()
    {
        // Arrange - the reviewer's real intent was to approve; a category the
        // archive does not have is not a reason to lose the memory
        var pendingId = await SeedPendingEventAsync(pending => pending.Category = EventCategory.Travel);

        var change = DecisionChange(pendingId, PendingEventDecision.Approve, new PendingEventCorrections
        {
            Category = "roadtrip",
        });

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert
        result.Should().Be(ChangeApplicationResult.Applied);

        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.AsNoTracking().SingleAsync()).Category.Should().Be(EventCategory.Travel);
    }

    [Fact]
    public async Task ApplyAsync_CorrectionsThatEndBeforeTheyStart_AreRefusedPermanently()
    {
        // Arrange - the approve path writes the event row directly, so nothing
        // downstream would catch an inverted range
        var pendingId = await SeedPendingEventAsync(pending => pending.StartDate = new DateTime(2003, 7, 14));

        var change = DecisionChange(pendingId, PendingEventDecision.Approve, new PendingEventCorrections
        {
            EndDate = new DateTime(2003, 7, 1),
        });

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert - a retry would carry the same corrections, so this never
        // becomes retryable, and nothing is written
        result.Should().Be(ChangeApplicationResult.FailedPermanent);

        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.CountAsync()).Should().Be(0);
        (await context.PendingEvents.AsNoTracking().SingleAsync()).IsApproved.Should().BeFalse();
    }

    #endregion

    #region Malformed and unroutable changes

    [Fact]
    public async Task ApplyAsync_UnknownPendingEvent_IsDroppedWithoutWriting()
    {
        // Act
        var result = await _applier.ApplyAsync(
            DecisionChange(Guid.NewGuid().ToString(), PendingEventDecision.Approve));

        // Assert
        result.Should().Be(ChangeApplicationResult.Skipped);

        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_UnrecognizedVerdict_IsPermanentlySkipped()
    {
        // Arrange - guessing would either invent a memory or destroy one
        var pendingId = await SeedPendingEventAsync();

        // Act
        var result = await _applier.ApplyAsync(DecisionChange(pendingId, "maybe"));

        // Assert
        result.Should().Be(ChangeApplicationResult.FailedPermanent);

        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.CountAsync()).Should().Be(0);
        (await context.PendingEvents.AsNoTracking().SingleAsync()).IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_MissingOrUnparseablePayload_IsPermanentlySkipped()
    {
        // Arrange
        var noPayload = new SyncChangeDto
        {
            ChangeId = 1,
            EntityType = SyncChangeEntityType.PendingEventDecision,
            EntityId = Guid.NewGuid().ToString(),
            Operation = SyncOperation.Upsert,
        };
        var badPayload = new SyncChangeDto
        {
            ChangeId = 2,
            EntityType = SyncChangeEntityType.PendingEventDecision,
            EntityId = Guid.NewGuid().ToString(),
            Operation = SyncOperation.Upsert,
            PayloadJson = "{ this is not json",
        };

        // Act & Assert - a change that cannot be read never becomes readable
        (await _applier.ApplyAsync(noPayload)).Should().Be(ChangeApplicationResult.FailedPermanent);
        (await _applier.ApplyAsync(badPayload)).Should().Be(ChangeApplicationResult.FailedPermanent);
    }

    [Fact]
    public async Task ApplyAsync_DeleteOperation_IsSkipped()
    {
        // Arrange - a verdict is a fact about a moment, not a row to retract
        var pendingId = await SeedPendingEventAsync();
        var change = DecisionChange(pendingId, PendingEventDecision.Approve);
        change.Operation = SyncOperation.Delete;

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert
        result.Should().Be(ChangeApplicationResult.Skipped);

        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_PendingEventIdOnlyInTheEnvelope_StillResolvesTheDecision()
    {
        // Arrange - a producer that populated the change envelope but left the
        // payload's id blank
        var pendingId = await SeedPendingEventAsync();
        var change = DecisionChange(pendingId, PendingEventDecision.Approve);
        change.PayloadJson = JsonSerializer.Serialize(
            new PendingEventDecisionPayload
            {
                Decision = PendingEventDecision.Approve,
                DecidedByDeviceId = "device-1",
                DecidedAtUtc = DateTime.UtcNow,
            },
            WireJson);

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert
        result.Should().Be(ChangeApplicationResult.Applied);

        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.CountAsync()).Should().Be(1);
    }

    #endregion

    #region Routing

    [Fact]
    public async Task RemoteChangeApplier_PendingEventDecision_IsHandedToTheDecisionApplier()
    {
        // Arrange - the sync worker only ever calls IRemoteChangeApplier, so the
        // routing is what makes any of the above reachable
        var decisionApplier = new Mock<IPendingEventDecisionApplier>();
        decisionApplier
            .Setup(a => a.ApplyAsync(It.IsAny<SyncChangeDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ChangeApplicationResult.Applied);

        var applier = new RemoteChangeApplier(
            new Mock<IArtifactTransferClient>().Object,
            new Mock<ICaptureIngestionService>().Object,
            new Mock<IQueueService>().Object,
            new Mock<ISyncSettingsStore>().Object,
            NullLogger<RemoteChangeApplier>.Instance,
            decisionApplier.Object);

        var change = DecisionChange(Guid.NewGuid().ToString(), PendingEventDecision.Approve);

        // Act
        var result = await applier.ApplyAsync(change);

        // Assert
        result.Should().Be(ChangeApplicationResult.Applied);
        decisionApplier.Verify(
            a => a.ApplyAsync(change, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoteChangeApplier_WithoutADecisionApplier_SkipsInsteadOfFailingTheCycle()
    {
        // Arrange - the applier is an optional dependency, exactly like the
        // capture status publisher
        var applier = new RemoteChangeApplier(
            new Mock<IArtifactTransferClient>().Object,
            new Mock<ICaptureIngestionService>().Object,
            new Mock<IQueueService>().Object,
            new Mock<ISyncSettingsStore>().Object,
            NullLogger<RemoteChangeApplier>.Instance);

        // Act
        var result = await applier.ApplyAsync(
            DecisionChange(Guid.NewGuid().ToString(), PendingEventDecision.Approve));

        // Assert - the cursor advances rather than the cycle stalling forever
        result.Should().Be(ChangeApplicationResult.Skipped);
    }

    #endregion

    #region Helpers

    private static SyncChangeDto DecisionChange(
        string pendingEventId, string decision, PendingEventCorrections? corrections = null)
    {
        var payload = new PendingEventDecisionPayload
        {
            PendingEventId = pendingEventId,
            Decision = decision,
            DecidedByDeviceId = "device-1",
            DecidedAtUtc = DateTime.UtcNow,
            Corrections = corrections,
        };

        return new SyncChangeDto
        {
            ChangeId = 42,
            EntityType = SyncChangeEntityType.PendingEventDecision,
            EntityId = pendingEventId,
            Operation = SyncOperation.Upsert,
            Revision = 1,
            ChangedAtUtc = DateTime.UtcNow,
            SourceDeviceId = "device-1",
            PayloadJson = JsonSerializer.Serialize(payload, WireJson),
        };
    }

    /// <summary>Seeds a device capture with one extracted event awaiting review.</summary>
    private async Task<string> SeedPendingEventAsync(Action<PendingEvent>? configure = null)
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
            ExtractedData = string.Empty,
            Status = PendingStatus.PendingReview.ToStringValue(),
        };
        configure?.Invoke(pending);

        context.RecordingQueues.Add(queueItem);
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

    #endregion
}
