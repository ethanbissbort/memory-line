using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for PendingEvent entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class PendingEventRepository : IPendingEventRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public PendingEventRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<PendingEvent?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.PendingEvents.Include(p => p.RecordingQueue).FirstOrDefaultAsync(p => p.PendingId == id);
    }

    public async Task<IEnumerable<PendingEvent>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.PendingEvents.AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync();
    }

    public async Task<PendingEvent> AddAsync(PendingEvent entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.PendingEvents.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(PendingEvent entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.PendingEvents.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(PendingEvent entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.PendingEvents.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<PendingEvent> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.PendingEvents.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<PendingEvent> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.PendingEvents.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<PendingEvent, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.PendingEvents.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<PendingEvent, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return predicate == null
            ? await context.PendingEvents.CountAsync()
            : await context.PendingEvents.CountAsync(predicate);
    }

    public async Task<IEnumerable<PendingEvent>> FindAsync(System.Linq.Expressions.Expression<Func<PendingEvent, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.PendingEvents.AsNoTracking().Where(predicate).ToListAsync();
    }

    public async Task<IEnumerable<PendingEvent>> GetByQueueIdAsync(string queueId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.PendingEvents.AsNoTracking().Where(p => p.QueueId == queueId).OrderBy(p => p.CreatedAt).ToListAsync();
    }

    public async Task<IEnumerable<PendingEvent>> GetByStatusAsync(PendingStatus status)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.PendingEvents.AsNoTracking().Where(p => p.Status == status.ToStringValue()).OrderByDescending(p => p.CreatedAt).ToListAsync();
    }

    public async Task<int> GetCountByStatusAsync(PendingStatus status)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.PendingEvents.CountAsync(p => p.Status == status.ToStringValue());
    }

    public async Task<IEnumerable<PendingEvent>> GetRecentAsync(int count = 10)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.PendingEvents.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(count).ToListAsync();
    }
}
