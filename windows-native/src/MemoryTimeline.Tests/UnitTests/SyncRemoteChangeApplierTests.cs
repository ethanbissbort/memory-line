using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Sync;
using MemoryTimeline.SyncContracts;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="RemoteChangeApplier"/> over the REAL
/// <see cref="CaptureIngestionService"/> stack (real repositories, EF InMemory,
/// unique database per test instance — mirroring CaptureIngestionServiceTests)
/// with a fake artifact transfer client that writes known bytes to the
/// destination path. Covers the apply/skip/permanent/retryable classification
/// the sync worker's cursor advancement depends on.
/// </summary>
public class SyncRemoteChangeApplierTests : IDisposable
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly TestDbContextFactory _contextFactory;
    private readonly FakeArtifactTransferClient _transferClient;
    private readonly Mock<IQueueService> _queueService;
    private readonly FakeSyncSettingsStore _settingsStore;
    private readonly RemoteChangeApplier _applier;
    private readonly List<string> _tempFiles = new();

    public SyncRemoteChangeApplierTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        var ingestionService = new CaptureIngestionService(
            new CaptureRepository(_contextFactory),
            new RecordingQueueRepository(_contextFactory),
            new LocalFileArtifactResolver(),
            NullLogger<CaptureIngestionService>.Instance);
        _transferClient = new FakeArtifactTransferClient();
        _queueService = new Mock<IQueueService>();
        _settingsStore = new FakeSyncSettingsStore();
        _applier = new RemoteChangeApplier(
            _transferClient,
            ingestionService,
            _queueService.Object,
            _settingsStore,
            NullLogger<RemoteChangeApplier>.Instance);
    }

    [Fact]
    public async Task ApplyAsync_CompleteAudioCapture_AppliesIngestsAndAutoProcesses()
    {
        // Arrange
        var change = CreateCaptureChange(out var payload, out var content);
        _transferClient.Content = content;

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert
        result.Should().Be(ChangeApplicationResult.Applied);

        await using var context = _contextFactory.CreateDbContext();
        var queueItem = await context.RecordingQueues.SingleAsync();
        queueItem.SourceCaptureId.Should().Be(payload.CaptureId);
        queueItem.SourcePlatform.Should().Be(CapturePlatform.Ios);
        queueItem.SyncState.Should().Be(QueueSyncState.Received);
        queueItem.AudioFilePath.Should().Be(ExpectedAudioPath(payload.CaptureId));
        File.Exists(queueItem.AudioFilePath).Should().BeTrue();

        _queueService.Verify(
            q => q.ProcessCaptureAsync(queueItem.QueueId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_AutoProcessDisabled_AppliesWithoutStartingProcessing()
    {
        // Arrange
        _settingsStore.AutoProcessEnabled = false;
        var change = CreateCaptureChange(out _, out var content);
        _transferClient.Content = content;

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert - ingested for manual review, no processing kicked off
        result.Should().Be(ChangeApplicationResult.Applied);

        await using var context = _contextFactory.CreateDbContext();
        (await context.RecordingQueues.CountAsync()).Should().Be(1);
        _queueService.Verify(
            q => q.ProcessCaptureAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_MetadataOnlyChangeWithoutArtifact_IsSkippedWithoutRows()
    {
        // Arrange - capture created on the phone but audio not uploaded yet
        var change = CreateCaptureChange(out var payload, out _);
        payload.AudioArtifact = null;
        change.PayloadJson = JsonSerializer.Serialize(payload, WireJson);

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert - the artifact-complete change that follows triggers ingestion
        result.Should().Be(ChangeApplicationResult.Skipped);
        _transferClient.DownloadCount.Should().Be(0);
        await AssertNoRowsWrittenAsync();
    }

    [Fact]
    public async Task ApplyAsync_ArtifactNotYetComplete_IsSkippedWithoutRows()
    {
        // Arrange
        var change = CreateCaptureChange(out var payload, out _);
        payload.AudioArtifact!.State = ArtifactState.Uploading;
        change.PayloadJson = JsonSerializer.Serialize(payload, WireJson);

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert
        result.Should().Be(ChangeApplicationResult.Skipped);
        _transferClient.DownloadCount.Should().Be(0);
        await AssertNoRowsWrittenAsync();
    }

    [Fact]
    public async Task ApplyAsync_NonCaptureEntityType_IsSkipped()
    {
        // Arrange
        var change = new SyncChangeDto
        {
            ChangeId = 7,
            EntityType = SyncChangeEntityType.Event,
            EntityId = Guid.NewGuid().ToString(),
            Operation = SyncOperation.Upsert,
        };

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert
        result.Should().Be(ChangeApplicationResult.Skipped);
        await AssertNoRowsWrittenAsync();
    }

    [Fact]
    public async Task ApplyAsync_DeleteOperation_IsSkippedInPhase1()
    {
        // Arrange
        var change = CreateCaptureChange(out _, out _);
        change.Operation = SyncOperation.Delete;

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert
        result.Should().Be(ChangeApplicationResult.Skipped);
        await AssertNoRowsWrittenAsync();
    }

    [Fact]
    public async Task ApplyAsync_WindowsOriginatedCapture_IsSkippedAsEcho()
    {
        // Arrange - defense against our own changes echoing back from the server
        var change = CreateCaptureChange(out var payload, out _);
        payload.SourcePlatform = CapturePlatform.Windows;
        change.PayloadJson = JsonSerializer.Serialize(payload, WireJson);

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert
        result.Should().Be(ChangeApplicationResult.Skipped);
        _transferClient.DownloadCount.Should().Be(0);
        await AssertNoRowsWrittenAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("this is not json")]
    [InlineData("null")]
    public async Task ApplyAsync_MissingOrUnparseablePayload_FailsPermanent(string? payloadJson)
    {
        // Arrange
        var change = new SyncChangeDto
        {
            ChangeId = 9,
            EntityType = SyncChangeEntityType.Capture,
            EntityId = Guid.NewGuid().ToString(),
            Operation = SyncOperation.Upsert,
            PayloadJson = payloadJson,
        };

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert - logged and skipped permanently, never thrown
        result.Should().Be(ChangeApplicationResult.FailedPermanent);
        await AssertNoRowsWrittenAsync();
    }

    [Fact]
    public async Task ApplyAsync_DownloadedBytesHashDifferentlyThanDeclared_FailsPermanent()
    {
        // Arrange - byte length matches so ingestion reaches the hash check
        var change = CreateCaptureChange(out var payload, out var content);
        payload.AudioArtifact!.Sha256 = new string('a', 64);
        change.PayloadJson = JsonSerializer.Serialize(payload, WireJson);
        _transferClient.Content = content;

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert - ingestion classifies it ArtifactInvalid → permanent
        result.Should().Be(ChangeApplicationResult.FailedPermanent);
        await AssertNoRowsWrittenAsync();
    }

    [Fact]
    public async Task ApplyAsync_RetryableDownloadFailure_FailsRetryable()
    {
        // Arrange
        var change = CreateCaptureChange(out _, out _);
        _transferClient.ExceptionToThrow = new SyncApiException(
            "service unavailable", HttpStatusCode.ServiceUnavailable, null, retryable: true);

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert - the cursor must not advance past it
        result.Should().Be(ChangeApplicationResult.FailedRetryable);
        await AssertNoRowsWrittenAsync();
    }

    [Fact]
    public async Task ApplyAsync_NonRetryableDownloadFailure_FailsPermanent()
    {
        // Arrange
        var change = CreateCaptureChange(out _, out _);
        _transferClient.ExceptionToThrow = new SyncApiException(
            "artifact not found", HttpStatusCode.NotFound, null, retryable: false);

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert
        result.Should().Be(ChangeApplicationResult.FailedPermanent);
        await AssertNoRowsWrittenAsync();
    }

    [Fact]
    public async Task ApplyAsync_DuplicateReplayOfSameChange_AppliedWithSingleQueueRow()
    {
        // Arrange
        var change = CreateCaptureChange(out _, out var content);
        _transferClient.Content = content;

        // Act - the same change delivered twice (e.g. cursor persisted late)
        var first = await _applier.ApplyAsync(change);
        var second = await _applier.ApplyAsync(change);

        // Assert - idempotent: still one queue row, one download, one processing start
        first.Should().Be(ChangeApplicationResult.Applied);
        second.Should().Be(ChangeApplicationResult.Applied);

        await using var context = _contextFactory.CreateDbContext();
        (await context.RecordingQueues.CountAsync()).Should().Be(1);
        (await context.Captures.CountAsync()).Should().Be(1);

        _transferClient.DownloadCount.Should().Be(1);
        _queueService.Verify(
            q => q.ProcessCaptureAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_ProcessingFailure_DoesNotFailTheApply()
    {
        // Arrange - processing failures are visible queue states, not sync errors
        _queueService
            .Setup(q => q.ProcessCaptureAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConfigurationException("No LLM API key configured."));
        var change = CreateCaptureChange(out _, out var content);
        _transferClient.Content = content;

        // Act
        var result = await _applier.ApplyAsync(change);

        // Assert - the capture is ingested; the cycle continues
        result.Should().Be(ChangeApplicationResult.Applied);
        await using var context = _contextFactory.CreateDbContext();
        (await context.RecordingQueues.CountAsync()).Should().Be(1);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    #region Helpers

    /// <summary>
    /// Builds a capture upsert change with a complete audio artifact whose
    /// declared hash/length match <paramref name="content"/>, and registers the
    /// applier's cache path for cleanup.
    /// </summary>
    private SyncChangeDto CreateCaptureChange(out CaptureChangePayload payload, out byte[] content, long changeId = 1)
    {
        content = new byte[512];
        Random.Shared.NextBytes(content);

        payload = new CaptureChangePayload
        {
            CaptureId = Guid.NewGuid().ToString(),
            SourceDeviceId = "iphone-15-pro",
            SourcePlatform = CapturePlatform.Ios,
            CaptureType = CaptureTypes.TripLog,
            CapturedAtUtc = DateTime.UtcNow,
            Status = SyncCaptureStatus.Received,
            AudioArtifact = new ArtifactSummary
            {
                ArtifactId = Guid.NewGuid().ToString(),
                ArtifactType = "audio_original",
                MediaType = "audio/mp4",
                OriginalFileName = "roadtrip-clip.m4a",
                ByteLength = content.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                State = ArtifactState.Complete,
            },
        };

        _tempFiles.Add(ExpectedAudioPath(payload.CaptureId));

        return new SyncChangeDto
        {
            ChangeId = changeId,
            EntityType = SyncChangeEntityType.Capture,
            EntityId = payload.CaptureId,
            Operation = SyncOperation.Upsert,
            Revision = 1,
            ChangedAtUtc = DateTime.UtcNow,
            SourceDeviceId = payload.SourceDeviceId,
            PayloadJson = JsonSerializer.Serialize(payload, WireJson),
        };
    }

    /// <summary>The applier's deterministic local cache path for a capture's audio.</summary>
    private static string ExpectedAudioPath(string captureId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MemoryTimeline",
        "SyncedAudio",
        captureId + ".m4a");

    private async Task AssertNoRowsWrittenAsync()
    {
        await using var context = _contextFactory.CreateDbContext();
        (await context.Captures.CountAsync()).Should().Be(0);
        (await context.CaptureArtifacts.CountAsync()).Should().Be(0);
        (await context.RecordingQueues.CountAsync()).Should().Be(0);
        (await context.SyncOutboxEntries.CountAsync()).Should().Be(0);
    }

    /// <summary>Writes known bytes to the destination path, or throws the configured exception.</summary>
    private sealed class FakeArtifactTransferClient : IArtifactTransferClient
    {
        public byte[] Content { get; set; } = Array.Empty<byte>();

        public Exception? ExceptionToThrow { get; set; }

        public int DownloadCount { get; private set; }

        public Task DownloadArtifactAsync(
            string artifactId, string destinationPath, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            DownloadCount++;
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(destinationPath, Content);
            return Task.CompletedTask;
        }
    }

    /// <summary>Paired, enabled sync settings with a toggleable auto-process flag.</summary>
    private sealed class FakeSyncSettingsStore : ISyncSettingsStore
    {
        public bool AutoProcessEnabled { get; set; } = true;

        public Task<bool> IsSyncEnabledAsync() => Task.FromResult(true);

        public Task<string?> GetServerUrlAsync() => Task.FromResult<string?>("https://sync.example.test");

        public Task<string?> GetDeviceIdAsync() => Task.FromResult<string?>("windows-device-1");

        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("access-token");

        public Task<string?> GetRefreshTokenAsync() => Task.FromResult<string?>("refresh-token");

        public Task<bool> IsAutoProcessEnabledAsync() => Task.FromResult(AutoProcessEnabled);

        public Task SaveRegistrationAsync(string serverUrl, DeviceRegisterResponse registration) =>
            Task.CompletedTask;

        public Task SaveTokensAsync(string accessToken, string refreshToken) => Task.CompletedTask;

        public Task ClearRegistrationAsync() => Task.CompletedTask;
    }

    #endregion
}
