using MemoryTimeline.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository interface for RecordingQueue entity.
/// </summary>
public interface IRecordingQueueRepository : IRepository<RecordingQueue>
{
    Task<IEnumerable<RecordingQueue>> GetByStatusAsync(string status);
    Task<RecordingQueue?> GetByIdWithPendingEventsAsync(string queueId);
    Task<int> GetCountByStatusAsync(string status);
}

/// <summary>
/// Repository implementation for RecordingQueue entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class RecordingQueueRepository : IRecordingQueueRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public RecordingQueueRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<RecordingQueue> AddAsync(RecordingQueue entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.RecordingQueues.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(RecordingQueue entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.RecordingQueues.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(RecordingQueue entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.RecordingQueues.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<RecordingQueue> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.RecordingQueues.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<RecordingQueue> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.RecordingQueues.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<RecordingQueue, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.RecordingQueues.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<RecordingQueue, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return predicate == null
            ? await context.RecordingQueues.CountAsync()
            : await context.RecordingQueues.CountAsync(predicate);
    }

    public async Task<IEnumerable<RecordingQueue>> FindAsync(System.Linq.Expressions.Expression<Func<RecordingQueue, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.RecordingQueues.AsNoTracking().Where(predicate).ToListAsync();
    }

    public async Task<RecordingQueue?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.RecordingQueues
            .FirstOrDefaultAsync(q => q.QueueId == id);
    }

    public async Task<IEnumerable<RecordingQueue>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.RecordingQueues
            .AsNoTracking()
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<RecordingQueue>> GetByStatusAsync(string status)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.RecordingQueues
            .AsNoTracking()
            .Where(q => q.Status == status)
            .OrderBy(q => q.CreatedAt)
            .ToListAsync();
    }

    public async Task<RecordingQueue?> GetByIdWithPendingEventsAsync(string queueId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.RecordingQueues
            .Include(q => q.PendingEvents)
            .FirstOrDefaultAsync(q => q.QueueId == queueId);
    }

    public async Task<int> GetCountByStatusAsync(string status)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.RecordingQueues
            .CountAsync(q => q.Status == status);
    }
}
