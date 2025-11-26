using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

public class PendingEventRepository : IPendingEventRepository
{
    private readonly AppDbContext _context;

    public PendingEventRepository(AppDbContext context) => _context = context;

    public async Task<PendingEvent?> GetByIdAsync(string id) =>
        await _context.PendingEvents.Include(p => p.RecordingQueue).FirstOrDefaultAsync(p => p.PendingId == id);

    public async Task<IEnumerable<PendingEvent>> GetAllAsync() =>
        await _context.PendingEvents.OrderByDescending(p => p.CreatedAt).ToListAsync();

    public async Task<PendingEvent> AddAsync(PendingEvent entity)
    {
        _context.PendingEvents.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(PendingEvent entity)
    {
        _context.PendingEvents.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(PendingEvent entity)
    {
        _context.PendingEvents.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<PendingEvent> entities)
    {
        _context.PendingEvents.AddRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<PendingEvent> entities)
    {
        _context.PendingEvents.RemoveRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<PendingEvent, bool>> predicate) =>
        await _context.PendingEvents.AnyAsync(predicate);

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<PendingEvent, bool>>? predicate = null) =>
        predicate == null
            ? await _context.PendingEvents.CountAsync()
            : await _context.PendingEvents.CountAsync(predicate);

    public async Task<IEnumerable<PendingEvent>> FindAsync(System.Linq.Expressions.Expression<Func<PendingEvent, bool>> predicate) =>
        await _context.PendingEvents.Where(predicate).ToListAsync();

    public async Task<IEnumerable<PendingEvent>> GetByQueueIdAsync(string queueId) =>
        await _context.PendingEvents.Where(p => p.QueueId == queueId).OrderBy(p => p.CreatedAt).ToListAsync();

    public async Task<IEnumerable<PendingEvent>> GetByStatusAsync(PendingStatus status) =>
        await _context.PendingEvents.Where(p => p.Status == status.ToStringValue()).OrderByDescending(p => p.CreatedAt).ToListAsync();

    public async Task<int> GetCountByStatusAsync(PendingStatus status) =>
        await _context.PendingEvents.CountAsync(p => p.Status == status.ToStringValue());

    public async Task<IEnumerable<PendingEvent>> GetRecentAsync(int count = 10) =>
        await _context.PendingEvents.OrderByDescending(p => p.CreatedAt).Take(count).ToListAsync();
}
