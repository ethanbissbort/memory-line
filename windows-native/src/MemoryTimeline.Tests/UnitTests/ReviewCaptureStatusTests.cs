using FluentAssertions;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// The review half of the Phase 3 status handoff (design §19): approving or
/// rejecting on Windows must republish the capture's status, because nothing
/// else does. <see cref="QueueService"/> only publishes on a stage transition,
/// and review never causes one — so without this the phone's review counts
/// freeze at extraction time and a capture reads "Ready for review" forever,
/// even after the user finished reviewing on the PC.
///
/// A file-based SQLite database is used because approval opens a relational
/// transaction. The publisher is an optional dependency exactly like
/// <see cref="QueueService"/>'s, so the no-publisher path is covered too.
/// </summary>
public class ReviewCaptureStatusTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly RecordingQueueRepository _queueRepository;
    private readonly Mock<ICaptureStatusPublisher> _statusPublisher = new();
    private readonly List<RecordingQueue> _published = new();
    private readonly EventExtractionService _extractionService;
    private readonly string _databasePath;

    public ReviewCaptureStatusTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(), $"ReviewCaptureStatusTests_{Guid.NewGuid()}.db");
        _contextFactory = TestDbContextFactory.CreateSqliteFile(_databasePath);

        using (var context = _contextFactory.CreateDbContext())
        {
            context.Database.EnsureCreated();
        }

        _queueRepository = new RecordingQueueRepository(_contextFactory);

        // Snapshot each publish: the service hands over the live queue row, so
        // the assertions need the values as they were at publish time.
        _statusPublisher
            .Setup(p => p.PublishAsync(It.IsAny<RecordingQueue>(), It.IsAny<CancellationToken>()))
            .Callback<RecordingQueue, CancellationToken>((item, _) => _published.Add(new RecordingQueue
            {
                QueueId = item.QueueId,
                Status = item.Status,
                ProcessingStage = item.ProcessingStage,
                SourceCaptureId = item.SourceCaptureId,
            }))
            .Returns(Task.CompletedTask);

        _extractionService = BuildService(_statusPublisher.Object);
    }

    [Fact]
    public async Task ApprovePendingEventAsync_LastPendingEventApproved_CompletesTheQueueItemAndPublishes()
    {
        // Arrange - one capture waiting on its only extracted event
        var queueId = await SeedQueueItemAsync(QueueProcessingStage.ReviewReady);
        var pendingId = await SeedPendingEventAsync(queueId, "Ferry to the island");

        // Act
        await _extractionService.ApprovePendingEventAsync(pendingId);

        // Assert - the review is over, so the capture is done
        _published.Should().ContainSingle();
        _published[0].QueueId.Should().Be(queueId);
        _published[0].ProcessingStage.Should().Be(QueueProcessingStage.Completed);
        _published[0].Status.Should().Be(QueueStatus.Completed);

        var stored = await _queueRepository.GetByIdAsync(queueId);
        stored!.ProcessingStage.Should().Be(QueueProcessingStage.Completed);
        stored.Status.Should().Be(QueueStatus.Completed);
        stored.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ApprovePendingEventAsync_PendingEventsRemain_PublishesWithoutCompleting()
    {
        // Arrange - two events, only one approved
        var queueId = await SeedQueueItemAsync(QueueProcessingStage.ReviewReady);
        var first = await SeedPendingEventAsync(queueId, "Ferry to the island");
        await SeedPendingEventAsync(queueId, "Dinner at the harbour");

        // Act
        await _extractionService.ApprovePendingEventAsync(first);

        // Assert - the counts moved, but the review is not over
        _published.Should().ContainSingle();
        _published[0].ProcessingStage.Should().Be(QueueProcessingStage.ReviewReady);

        var stored = await _queueRepository.GetByIdAsync(queueId);
        stored!.ProcessingStage.Should().Be(QueueProcessingStage.ReviewReady);
    }

    [Fact]
    public async Task RejectPendingEventAsync_LastPendingEventRejected_CompletesTheQueueItemAndPublishes()
    {
        // Arrange - rejecting the last event finishes the review just as
        // approving it does
        var queueId = await SeedQueueItemAsync(QueueProcessingStage.ReviewReady);
        var pendingId = await SeedPendingEventAsync(queueId, "Nothing worth keeping");

        // Act
        await _extractionService.RejectPendingEventAsync(pendingId);

        // Assert
        _published.Should().ContainSingle();
        _published[0].ProcessingStage.Should().Be(QueueProcessingStage.Completed);

        var stored = await _queueRepository.GetByIdAsync(queueId);
        stored!.ProcessingStage.Should().Be(QueueProcessingStage.Completed);
    }

    [Fact]
    public async Task RejectPendingEventAsync_EventsRemain_PublishesWithoutCompleting()
    {
        // Arrange
        var queueId = await SeedQueueItemAsync(QueueProcessingStage.ReviewReady);
        var first = await SeedPendingEventAsync(queueId, "Duplicate");
        await SeedPendingEventAsync(queueId, "Ferry to the island");

        // Act
        await _extractionService.RejectPendingEventAsync(first);

        // Assert
        _published.Should().ContainSingle();
        _published[0].ProcessingStage.Should().Be(QueueProcessingStage.ReviewReady);
    }

    [Fact]
    public async Task ApproveBatch_WithPublishSuppressed_PublishesOncePerCaptureNotOncePerEvent()
    {
        // Arrange - what ReviewViewModel.ApproveAllAsync does: suppress the
        // per-event publish, then publish once per affected capture
        var queueId = await SeedQueueItemAsync(QueueProcessingStage.ReviewReady);
        var pendingIds = new List<string>
        {
            await SeedPendingEventAsync(queueId, "Ferry to the island"),
            await SeedPendingEventAsync(queueId, "Dinner at the harbour"),
            await SeedPendingEventAsync(queueId, "Late walk on the pier"),
        };

        // Act
        foreach (var pendingId in pendingIds)
        {
            await _extractionService.ApprovePendingEventAsync(pendingId, publishCaptureStatus: false);
        }

        _published.Should().BeEmpty("a batch must not publish once per event");
        await _extractionService.PublishCaptureStatusAsync(queueId);

        // Assert - one publish, carrying the finished state
        _published.Should().ContainSingle();
        _published[0].ProcessingStage.Should().Be(QueueProcessingStage.Completed);
    }

    [Fact]
    public async Task PublishCaptureStatusAsync_QueueItemStillProcessing_IsNotForcedToCompleted()
    {
        // Arrange - a capture that is still transcribing has no review to finish
        var queueId = await SeedQueueItemAsync(QueueProcessingStage.Transcribing, QueueStatus.Processing);

        // Act
        await _extractionService.PublishCaptureStatusAsync(queueId);

        // Assert - the status is still projected, but the stage is untouched
        _published.Should().ContainSingle();
        _published[0].ProcessingStage.Should().Be(QueueProcessingStage.Transcribing);

        var stored = await _queueRepository.GetByIdAsync(queueId);
        stored!.ProcessingStage.Should().Be(QueueProcessingStage.Transcribing);
        stored.Status.Should().Be(QueueStatus.Processing);
    }

    [Fact]
    public async Task PublishCaptureStatusAsync_UnknownQueueId_PublishesNothing()
    {
        // Act
        await _extractionService.PublishCaptureStatusAsync(Guid.NewGuid().ToString());
        await _extractionService.PublishCaptureStatusAsync(string.Empty);

        // Assert
        _published.Should().BeEmpty();
    }

    [Fact]
    public async Task ApprovePendingEventAsync_PublisherThrows_StillApprovesTheEvent()
    {
        // Arrange - a status the phone misses is republished by the next
        // transition; it must never cost the user their approval
        var queueId = await SeedQueueItemAsync(QueueProcessingStage.ReviewReady);
        var pendingId = await SeedPendingEventAsync(queueId, "Ferry to the island");
        _statusPublisher
            .Setup(p => p.PublishAsync(It.IsAny<RecordingQueue>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        // Act
        var approved = await _extractionService.ApprovePendingEventAsync(pendingId);

        // Assert
        approved.Title.Should().Be("Ferry to the island");
        await using var context = _contextFactory.CreateDbContext();
        (await context.Events.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ApprovePendingEventAsync_WithoutAStatusPublisher_StillApproves()
    {
        // Arrange - the publisher is optional, exactly as in QueueService, so an
        // unregistered path (and every existing test) keeps working
        var service = BuildService(statusPublisher: null);
        var queueId = await SeedQueueItemAsync(QueueProcessingStage.ReviewReady);
        var pendingId = await SeedPendingEventAsync(queueId, "Ferry to the island");

        // Act
        var approved = await service.ApprovePendingEventAsync(pendingId);
        await service.PublishCaptureStatusAsync(queueId);

        // Assert - approved, and the queue row was left exactly as it was
        approved.Title.Should().Be("Ferry to the island");
        var stored = await _queueRepository.GetByIdAsync(queueId);
        stored!.ProcessingStage.Should().Be(QueueProcessingStage.ReviewReady);
    }

    // ---- Helpers ----

    private EventExtractionService BuildService(ICaptureStatusPublisher? statusPublisher) =>
        new(
            new Mock<ILlmService>().Object,
            new Mock<ISpeechToTextService>().Object,
            new Mock<IEventService>().Object,
            new Mock<ISettingsService>().Object,
            _queueRepository,
            new Mock<IPersonService>().Object,
            _contextFactory,
            new Mock<ILogger<EventExtractionService>>().Object,
            revisionWriter: null,
            statusPublisher: statusPublisher);

    /// <summary>Seeds a device capture sitting at the given stage.</summary>
    private async Task<string> SeedQueueItemAsync(string stage, string status = QueueStatus.Completed)
    {
        var item = new RecordingQueue
        {
            QueueId = Guid.NewGuid().ToString(),
            AudioFilePath = @"C:\cache\capture.wav",
            Status = status,
            ProcessingStage = stage,
            SourceCaptureId = Guid.NewGuid().ToString(),
            SourceDeviceId = "device-1",
            SourcePlatform = CapturePlatform.Ios,
            SyncState = QueueSyncState.Received,
            CreatedAt = DateTime.UtcNow,
        };

        await _queueRepository.AddAsync(item);
        return item.QueueId;
    }

    /// <summary>
    /// Seeds an approvable pending event on a queue item. ExtractedData is left
    /// empty so approval skips tag/person/location mapping — the subject here is
    /// the status republish, not metadata.
    /// </summary>
    private async Task<string> SeedPendingEventAsync(string queueId, string title)
    {
        var pending = new PendingEvent
        {
            PendingId = Guid.NewGuid().ToString(),
            QueueId = queueId,
            Title = title,
            StartDate = new DateTime(2024, 7, 1),
            Category = "other",
            ExtractedData = string.Empty,
            Status = PendingStatus.PendingReview.ToStringValue(),
            IsApproved = false,
            CreatedAt = DateTime.UtcNow,
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
