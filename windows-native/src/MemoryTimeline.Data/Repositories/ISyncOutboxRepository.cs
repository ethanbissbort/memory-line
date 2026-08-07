using MemoryTimeline.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository for the local sync outbox. The (future, Phase 1) sync worker
/// publishes undelivered records in outbox_id order and marks them delivered.
/// </summary>
public interface ISyncOutboxRepository
{
    Task<SyncOutbox> AddAsync(SyncOutbox entry);

    /// <summary>Undelivered records in strict outbox_id (creation) order.</summary>
    Task<IReadOnlyList<SyncOutbox>> GetPendingAsync(int limit = 100);

    Task<int> GetPendingCountAsync();

    Task MarkDeliveredAsync(long outboxId);

    /// <summary>Records a failed delivery attempt without consuming the record.</summary>
    Task RecordFailedAttemptAsync(long outboxId, string error);
}

/// <summary>
/// Repository implementation for the sync outbox.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class SyncOutboxRepository : ISyncOutboxRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public SyncOutboxRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<SyncOutbox> AddAsync(SyncOutbox entry)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.SyncOutboxEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry;
    }

    public async Task<IReadOnlyList<SyncOutbox>> GetPendingAsync(int limit = 100)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.SyncOutboxEntries
            .AsNoTracking()
            .Where(o => o.DeliveredAt == null)
            .OrderBy(o => o.OutboxId)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> GetPendingCountAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.SyncOutboxEntries.CountAsync(o => o.DeliveredAt == null);
    }

    public async Task MarkDeliveredAsync(long outboxId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entry = await context.SyncOutboxEntries.FirstOrDefaultAsync(o => o.OutboxId == outboxId);
        if (entry == null || entry.DeliveredAt != null)
        {
            return;
        }

        entry.DeliveredAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    public async Task RecordFailedAttemptAsync(long outboxId, string error)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entry = await context.SyncOutboxEntries.FirstOrDefaultAsync(o => o.OutboxId == outboxId);
        if (entry == null)
        {
            return;
        }

        entry.AttemptCount++;
        entry.LastError = error;
        await context.SaveChangesAsync();
    }
}
