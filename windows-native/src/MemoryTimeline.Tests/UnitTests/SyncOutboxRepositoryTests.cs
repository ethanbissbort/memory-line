using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="SyncOutboxRepository"/> over the EF InMemory provider
/// (unique database per test instance). Covers the delivery-ordering, delivery
/// idempotency, and failure-bookkeeping contracts the Phase 1 sync worker
/// relies on.
/// </summary>
public class SyncOutboxRepositoryTests
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly SyncOutboxRepository _repository;

    public SyncOutboxRepositoryTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _repository = new SyncOutboxRepository(_contextFactory);
    }

    [Fact]
    public async Task GetPendingAsync_MultipleEntries_ReturnsInsertionOrderAndRespectsLimit()
    {
        // Arrange
        await AddThreeEntriesAsync();

        // Act
        var pending = await _repository.GetPendingAsync();
        var limited = await _repository.GetPendingAsync(limit: 2);

        // Assert - strict outbox_id (creation) order
        pending.Should().HaveCount(3);
        pending.Select(o => o.EntityId).Should().Equal("capture-1", "capture-2", "capture-3");
        pending.Select(o => o.OutboxId).Should().BeInAscendingOrder();

        limited.Should().HaveCount(2);
        limited.Select(o => o.EntityId).Should().Equal("capture-1", "capture-2");
    }

    [Fact]
    public async Task MarkDeliveredAsync_PendingEntry_RemovesItFromPendingAndDropsCount()
    {
        // Arrange
        var entries = await AddThreeEntriesAsync();
        (await _repository.GetPendingCountAsync()).Should().Be(3);

        // Act
        await _repository.MarkDeliveredAsync(entries[1].OutboxId);

        // Assert
        (await _repository.GetPendingCountAsync()).Should().Be(2);
        var pending = await _repository.GetPendingAsync();
        pending.Select(o => o.OutboxId).Should().Equal(entries[0].OutboxId, entries[2].OutboxId);
    }

    [Fact]
    public async Task MarkDeliveredAsync_AlreadyDeliveredEntry_IsANoOp()
    {
        // Arrange
        var entry = await AddEntryAsync("capture-1");
        await _repository.MarkDeliveredAsync(entry.OutboxId);

        var firstDeliveredAt = await GetDeliveredAtAsync(entry.OutboxId);
        firstDeliveredAt.Should().NotBeNull();

        // Act - a second delivery of the same record must not move the timestamp
        await Task.Delay(20);
        await _repository.MarkDeliveredAsync(entry.OutboxId);

        // Assert
        (await GetDeliveredAtAsync(entry.OutboxId)).Should().Be(firstDeliveredAt);
        (await _repository.GetPendingCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_RepeatedFailures_IncrementCountAndStoreErrorWithoutDelivering()
    {
        // Arrange
        var entry = await AddEntryAsync("capture-1");

        // Act
        await _repository.RecordFailedAttemptAsync(entry.OutboxId, "network unreachable");
        await _repository.RecordFailedAttemptAsync(entry.OutboxId, "sync service returned 503");

        // Assert - failures are bookkeeping only; the record stays pending
        await using var context = _contextFactory.CreateDbContext();
        var stored = await context.SyncOutboxEntries.AsNoTracking()
            .SingleAsync(o => o.OutboxId == entry.OutboxId);
        stored.AttemptCount.Should().Be(2);
        stored.LastError.Should().Be("sync service returned 503");
        stored.DeliveredAt.Should().BeNull();

        (await _repository.GetPendingCountAsync()).Should().Be(1);
    }

    #region Helpers

    private async Task<SyncOutbox> AddEntryAsync(string entityId)
    {
        return await _repository.AddAsync(new SyncOutbox
        {
            EntityType = SyncEntityType.Capture,
            EntityId = entityId,
            Operation = SyncOutboxOperation.Upsert,
            PayloadJson = $"{{\"captureId\":\"{entityId}\"}}"
        });
    }

    private async Task<List<SyncOutbox>> AddThreeEntriesAsync()
    {
        var entries = new List<SyncOutbox>();
        for (var i = 1; i <= 3; i++)
        {
            entries.Add(await AddEntryAsync($"capture-{i}"));
        }

        return entries;
    }

    private async Task<DateTime?> GetDeliveredAtAsync(long outboxId)
    {
        await using var context = _contextFactory.CreateDbContext();
        var entry = await context.SyncOutboxEntries.AsNoTracking()
            .SingleAsync(o => o.OutboxId == outboxId);
        return entry.DeliveredAt;
    }

    #endregion
}
