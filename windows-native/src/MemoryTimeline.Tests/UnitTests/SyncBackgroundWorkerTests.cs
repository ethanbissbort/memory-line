using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MemoryTimeline.Sync;
using MemoryTimeline.SyncContracts;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="SyncBackgroundWorker"/> cycle semantics via
/// <see cref="ISyncBackgroundWorker.SyncNowAsync"/> with all collaborators
/// mocked: readiness gating, paged pulls, in-order application, cursor
/// advancement/persistence rules, ack, and outbox publication.
/// </summary>
public class SyncBackgroundWorkerTests
{
    private readonly Mock<ISyncClient> _syncClient = new();
    private readonly Mock<IRemoteChangeApplier> _changeApplier = new();
    private readonly Mock<ILocalOutboxPublisher> _outboxPublisher = new();
    private readonly FakeCursorStore _cursorStore = new();
    private readonly FakeSyncSettingsStore _settingsStore = new();
    private readonly SyncBackgroundWorker _worker;

    private readonly List<long> _appliedChangeIds = new();

    public SyncBackgroundWorkerTests()
    {
        _worker = new SyncBackgroundWorker(
            _syncClient.Object,
            _changeApplier.Object,
            _outboxPublisher.Object,
            _cursorStore,
            _settingsStore,
            NullLogger<SyncBackgroundWorker>.Instance);
    }

    [Fact]
    public async Task SyncNowAsync_SyncDisabled_DoesNotRunOrTouchTheServer()
    {
        // Arrange
        _settingsStore.SyncEnabled = false;

        // Act
        var result = await _worker.SyncNowAsync();

        // Assert
        result.Ran.Should().BeFalse();
        _syncClient.VerifyNoOtherCalls();
        _outboxPublisher.VerifyNoOtherCalls();
        _changeApplier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncNowAsync_DeviceNotRegistered_DoesNotRun()
    {
        // Arrange - enabled but never paired
        _settingsStore.DeviceId = null;

        // Act
        var result = await _worker.SyncNowAsync();

        // Assert
        result.Ran.Should().BeFalse();
        _syncClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncNowAsync_MultiplePages_AppliesInOrderPersistsCursorAcksAndPublishes()
    {
        // Arrange - two pages: [1,2] then [3]
        SetupPull(
            (0L, Page(hasMore: true, 1, 2)),
            (2L, Page(hasMore: false, 3)));
        SetupApplier(_ => ChangeApplicationResult.Applied);
        _outboxPublisher
            .Setup(p => p.PublishPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var result = await _worker.SyncNowAsync();

        // Assert
        result.Ran.Should().BeTrue();
        result.Error.Should().BeNull();
        result.ChangesApplied.Should().Be(3);
        result.ChangesFailedPermanent.Should().Be(0);
        result.OutboxPublished.Should().Be(2);
        result.Cursor.Should().Be(3);

        _appliedChangeIds.Should().Equal(1, 2, 3);
        _cursorStore.SetHistory.Should().Equal(1, 2, 3);
        _cursorStore.Cursor.Should().Be(3);

        _syncClient.Verify(c => c.PullChangesAsync(0, 100, It.IsAny<CancellationToken>()), Times.Once);
        _syncClient.Verify(c => c.PullChangesAsync(2, 100, It.IsAny<CancellationToken>()), Times.Once);
        _syncClient.Verify(c => c.AckAsync(3, It.IsAny<CancellationToken>()), Times.Once);
        _outboxPublisher.Verify(p => p.PublishPendingAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncNowAsync_RetryableFailure_StopsPullWithoutAdvancingPastPriorChange()
    {
        // Arrange - change 11 fails retryably; 12 must not even be attempted
        SetupPull((0L, Page(hasMore: false, 10, 11, 12)));
        SetupApplier(changeId => changeId == 11
            ? ChangeApplicationResult.FailedRetryable
            : ChangeApplicationResult.Applied);

        // Act
        var result = await _worker.SyncNowAsync();

        // Assert - the cursor stays at the last durably applied change
        result.Ran.Should().BeTrue();
        result.ChangesApplied.Should().Be(1);
        result.Cursor.Should().Be(10);
        result.Error.Should().NotBeNullOrEmpty();

        _appliedChangeIds.Should().Equal(10, 11);
        _cursorStore.SetHistory.Should().Equal(10);
        _cursorStore.Cursor.Should().Be(10);

        _syncClient.Verify(c => c.AckAsync(10, It.IsAny<CancellationToken>()), Times.Once);
        _outboxPublisher.Verify(p => p.PublishPendingAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncNowAsync_PermanentFailure_AdvancesPastItAndCountsIt()
    {
        // Arrange - change 5 is permanently unappliable, 6 applies
        SetupPull((0L, Page(hasMore: false, 5, 6)));
        SetupApplier(changeId => changeId == 5
            ? ChangeApplicationResult.FailedPermanent
            : ChangeApplicationResult.Applied);

        // Act
        var result = await _worker.SyncNowAsync();

        // Assert - a poisoned change cannot block the queue (design §7.4)
        result.Ran.Should().BeTrue();
        result.ChangesApplied.Should().Be(1);
        result.ChangesFailedPermanent.Should().Be(1);
        result.Cursor.Should().Be(6);
        result.Error.Should().BeNull();

        _appliedChangeIds.Should().Equal(5, 6);
        _cursorStore.SetHistory.Should().Equal(5, 6);
        _syncClient.Verify(c => c.AckAsync(6, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncNowAsync_SkippedChanges_AdvanceCursorWithoutCountingAsApplied()
    {
        // Arrange
        SetupPull((0L, Page(hasMore: false, 1, 2)));
        SetupApplier(_ => ChangeApplicationResult.Skipped);

        // Act
        var result = await _worker.SyncNowAsync();

        // Assert
        result.ChangesApplied.Should().Be(0);
        result.Cursor.Should().Be(2);
        _cursorStore.SetHistory.Should().Equal(1, 2);
    }

    [Fact]
    public async Task SyncNowAsync_PullThrows_ReturnsErrorResultWithoutPublishing()
    {
        // Arrange
        _syncClient
            .Setup(c => c.PullChangesAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SyncApiException(
                "service unavailable", HttpStatusCode.ServiceUnavailable, null, retryable: true));

        // Act
        var result = await _worker.SyncNowAsync();

        // Assert - the failure is surfaced on the result, never thrown
        result.Ran.Should().BeTrue();
        result.Error.Should().NotBeNullOrEmpty();
        result.ChangesApplied.Should().Be(0);
        _cursorStore.SetHistory.Should().BeEmpty();
        _outboxPublisher.Verify(p => p.PublishPendingAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    #region Helpers

    /// <summary>Serves each pull page keyed by the cursor it is requested at.</summary>
    private void SetupPull(params (long Cursor, SyncPullResponse Page)[] pages)
    {
        var pagesByCursor = pages.ToDictionary(p => p.Cursor, p => p.Page);
        _syncClient
            .Setup(c => c.PullChangesAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((long cursor, int _, CancellationToken _) => Task.FromResult(pagesByCursor[cursor]));
    }

    /// <summary>Applies per-change results while recording the application order.</summary>
    private void SetupApplier(Func<long, ChangeApplicationResult> resultFor)
    {
        _changeApplier
            .Setup(a => a.ApplyAsync(It.IsAny<SyncChangeDto>(), It.IsAny<CancellationToken>()))
            .Returns((SyncChangeDto change, CancellationToken _) =>
            {
                _appliedChangeIds.Add(change.ChangeId);
                return Task.FromResult(resultFor(change.ChangeId));
            });
    }

    private static SyncPullResponse Page(bool hasMore, params long[] changeIds) => new()
    {
        Changes = changeIds.Select(id => new SyncChangeDto
        {
            ChangeId = id,
            EntityType = SyncChangeEntityType.Capture,
            EntityId = $"capture-{id}",
            Operation = SyncOperation.Upsert,
        }).ToList(),
        NextCursor = changeIds.Length == 0 ? 0 : changeIds[^1],
        HasMore = hasMore,
    };

    /// <summary>In-memory cursor store recording every persisted advancement.</summary>
    private sealed class FakeCursorStore : ISyncCursorStore
    {
        public long Cursor { get; set; }

        public List<long> SetHistory { get; } = new();

        public Task<long> GetCursorAsync() => Task.FromResult(Cursor);

        public Task SetCursorAsync(long cursor)
        {
            Cursor = cursor;
            SetHistory.Add(cursor);
            return Task.CompletedTask;
        }
    }

    /// <summary>Enabled, paired sync settings with toggleable enabled/device fields.</summary>
    private sealed class FakeSyncSettingsStore : ISyncSettingsStore
    {
        public bool SyncEnabled { get; set; } = true;

        public string? ServerUrl { get; set; } = "https://sync.example.test";

        public string? DeviceId { get; set; } = "windows-device-1";

        public Task<bool> IsSyncEnabledAsync() => Task.FromResult(SyncEnabled);

        public Task<string?> GetServerUrlAsync() => Task.FromResult(ServerUrl);

        public Task<string?> GetDeviceIdAsync() => Task.FromResult(DeviceId);

        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("access-token");

        public Task<string?> GetRefreshTokenAsync() => Task.FromResult<string?>("refresh-token");

        public Task<bool> IsAutoProcessEnabledAsync() => Task.FromResult(true);

        public Task SaveRegistrationAsync(string serverUrl, DeviceRegisterResponse registration) =>
            Task.CompletedTask;

        public Task SaveTokensAsync(string accessToken, string refreshToken) => Task.CompletedTask;

        public Task ClearRegistrationAsync() => Task.CompletedTask;
    }

    #endregion
}
