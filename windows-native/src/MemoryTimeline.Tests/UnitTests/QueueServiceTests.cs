using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

public class QueueServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly Mock<IRecordingQueueRepository> _repositoryMock;
    private readonly Mock<IEventExtractionService> _extractionServiceMock;
    private readonly Mock<ILogger<QueueService>> _loggerMock;
    private readonly QueueService _queueService;

    public QueueServiceTests()
    {
        // Create in-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"QueueTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _repositoryMock = new Mock<IRecordingQueueRepository>();
        _extractionServiceMock = new Mock<IEventExtractionService>();
        _loggerMock = new Mock<ILogger<QueueService>>();

        _queueService = new QueueService(
            _repositoryMock.Object,
            _extractionServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task AddToQueueAsync_ValidRecording_AddsToQueue()
    {
        // Arrange
        var recording = new AudioRecordingDto
        {
            QueueId = Guid.NewGuid().ToString(),
            AudioFilePath = "/path/to/audio.wav",
            DurationSeconds = 120.5,
            FileSizeBytes = 1024000,
            CreatedAt = DateTime.UtcNow
        };

        var expectedQueueItem = new RecordingQueue
        {
            QueueId = recording.QueueId,
            AudioFilePath = recording.AudioFilePath,
            Status = QueueStatus.Pending,
            DurationSeconds = recording.DurationSeconds,
            FileSizeBytes = recording.FileSizeBytes,
            CreatedAt = recording.CreatedAt
        };

        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<RecordingQueue>()))
            .ReturnsAsync(expectedQueueItem);

        // Act
        var result = await _queueService.AddToQueueAsync(recording);

        // Assert
        result.Should().NotBeNull();
        result.QueueId.Should().Be(recording.QueueId);
        result.Status.Should().Be(QueueStatus.Pending);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<RecordingQueue>()), Times.Once);
    }

    [Fact]
    public async Task GetAllQueueItemsAsync_WithItems_ReturnsAllItems()
    {
        // Arrange
        var queueItems = new List<RecordingQueue>
        {
            new() { QueueId = "1", AudioFilePath = "/path1.wav", Status = QueueStatus.Pending },
            new() { QueueId = "2", AudioFilePath = "/path2.wav", Status = QueueStatus.Processing },
            new() { QueueId = "3", AudioFilePath = "/path3.wav", Status = QueueStatus.Completed }
        };

        _repositoryMock.Setup(r => r.GetAllAsync())
            .ReturnsAsync(queueItems);

        // Act
        var results = await _queueService.GetAllQueueItemsAsync();

        // Assert
        results.Should().HaveCount(3);
        results.Select(r => r.QueueId).Should().Contain(new[] { "1", "2", "3" });
    }

    [Fact]
    public async Task UpdateQueueItemStatusAsync_ExistingItem_UpdatesStatus()
    {
        // Arrange
        var queueId = "test-queue-id";
        var queueItem = new RecordingQueue
        {
            QueueId = queueId,
            AudioFilePath = "/path.wav",
            Status = QueueStatus.Pending
        };

        _repositoryMock.Setup(r => r.GetByIdAsync(queueId))
            .ReturnsAsync(queueItem);

        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<RecordingQueue>()))
            .Returns(Task.CompletedTask);

        // Act
        await _queueService.UpdateQueueItemStatusAsync(queueId, QueueStatus.Completed);

        // Assert
        _repositoryMock.Verify(r => r.UpdateAsync(It.Is<RecordingQueue>(q =>
            q.QueueId == queueId && q.Status == QueueStatus.Completed)), Times.Once);
    }

    [Fact]
    public async Task GetQueueCountByStatusAsync_WithStatusFilter_ReturnsCorrectCount()
    {
        // Arrange
        var status = QueueStatus.Pending;
        var expectedCount = 5;

        _repositoryMock.Setup(r => r.GetCountByStatusAsync(status))
            .ReturnsAsync(expectedCount);

        // Act
        var count = await _queueService.GetQueueCountByStatusAsync(status);

        // Assert
        count.Should().Be(expectedCount);
    }

    [Fact]
    public async Task RemoveQueueItemAsync_ExistingItem_RemovesItem()
    {
        // Arrange
        var queueId = "test-queue-id";
        var queueItem = new RecordingQueue
        {
            QueueId = queueId,
            AudioFilePath = "/path.wav",
            Status = QueueStatus.Pending
        };

        _repositoryMock.Setup(r => r.GetByIdAsync(queueId))
            .ReturnsAsync(queueItem);

        _repositoryMock.Setup(r => r.DeleteAsync(It.IsAny<RecordingQueue>()))
            .Returns(Task.CompletedTask);

        // Act
        await _queueService.RemoveQueueItemAsync(queueId);

        // Assert
        _repositoryMock.Verify(r => r.DeleteAsync(It.Is<RecordingQueue>(q => q.QueueId == queueId)), Times.Once);
    }

    [Fact]
    public async Task RetryFailedItemAsync_FailedItem_ResetsStatusToPending()
    {
        // Arrange
        var queueId = "failed-item";
        var failedItem = new RecordingQueue
        {
            QueueId = queueId,
            AudioFilePath = "/path.wav",
            Status = QueueStatus.Failed,
            ErrorMessage = "Processing error",
            ProcessedAt = DateTime.UtcNow
        };

        _repositoryMock.Setup(r => r.GetByIdAsync(queueId))
            .ReturnsAsync(failedItem);

        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<RecordingQueue>()))
            .Returns(Task.CompletedTask);

        // Act
        await _queueService.RetryFailedItemAsync(queueId);

        // Assert
        _repositoryMock.Verify(r => r.UpdateAsync(It.Is<RecordingQueue>(q =>
            q.QueueId == queueId &&
            q.Status == QueueStatus.Pending &&
            q.ErrorMessage == null &&
            q.ProcessedAt == null)), Times.Once);
    }

    [Fact]
    public async Task ClearCompletedItemsAsync_WithCompletedItems_RemovesAllCompleted()
    {
        // Arrange
        var completedItems = new List<RecordingQueue>
        {
            new() { QueueId = "1", Status = QueueStatus.Completed },
            new() { QueueId = "2", Status = QueueStatus.Completed },
            new() { QueueId = "3", Status = QueueStatus.Completed }
        };

        _repositoryMock.Setup(r => r.GetByStatusAsync(QueueStatus.Completed))
            .ReturnsAsync(completedItems);

        _repositoryMock.Setup(r => r.DeleteAsync(It.IsAny<RecordingQueue>()))
            .Returns(Task.CompletedTask);

        // Act
        await _queueService.ClearCompletedItemsAsync();

        // Assert
        _repositoryMock.Verify(r => r.DeleteAsync(It.IsAny<RecordingQueue>()), Times.Exactly(3));
    }

    [Fact]
    public void QueueItemStatusChanged_WhenStatusUpdated_RaisesEvent()
    {
        // Arrange
        var eventRaised = false;
        string? capturedQueueId = null;
        string? capturedNewStatus = null;

        _queueService.QueueItemStatusChanged += (sender, args) =>
        {
            eventRaised = true;
            capturedQueueId = args.QueueId;
            capturedNewStatus = args.NewStatus;
        };

        var queueId = "test-id";
        var queueItem = new RecordingQueue
        {
            QueueId = queueId,
            Status = QueueStatus.Pending
        };

        _repositoryMock.Setup(r => r.GetByIdAsync(queueId))
            .ReturnsAsync(queueItem);

        // Act
        _queueService.UpdateQueueItemStatusAsync(queueId, QueueStatus.Processing).Wait();

        // Assert
        eventRaised.Should().BeTrue();
        capturedQueueId.Should().Be(queueId);
        capturedNewStatus.Should().Be(QueueStatus.Processing);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
