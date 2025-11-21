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
/// </summary>
public class RecordingQueueRepository : IRecordingQueueRepository
{
    private readonly AppDbContext _context;

    public RecordingQueueRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RecordingQueue> AddAsync(RecordingQueue entity)
    {
        _context.RecordingQueues.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<RecordingQueue> UpdateAsync(RecordingQueue entity)
    {
        _context.RecordingQueues.Update(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task DeleteAsync(string id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            _context.RecordingQueues.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<RecordingQueue?> GetByIdAsync(string id)
    {
        return await _context.RecordingQueues
            .FirstOrDefaultAsync(q => q.QueueId == id);
    }

    public async Task<IEnumerable<RecordingQueue>> GetAllAsync()
    {
        return await _context.RecordingQueues
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<RecordingQueue>> GetByStatusAsync(string status)
    {
        return await _context.RecordingQueues
            .Where(q => q.Status == status)
            .OrderBy(q => q.CreatedAt)
            .ToListAsync();
    }

    public async Task<RecordingQueue?> GetByIdWithPendingEventsAsync(string queueId)
    {
        return await _context.RecordingQueues
            .Include(q => q.PendingEvents)
            .FirstOrDefaultAsync(q => q.QueueId == queueId);
    }

    public async Task<int> GetCountByStatusAsync(string status)
    {
        return await _context.RecordingQueues
            .CountAsync(q => q.Status == status);
    }
}
