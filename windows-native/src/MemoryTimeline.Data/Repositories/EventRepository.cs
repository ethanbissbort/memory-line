using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;
using System.Linq.Expressions;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Event entity.
/// </summary>
public class EventRepository : IEventRepository
{
    private readonly AppDbContext _context;

    public EventRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Event>> GetAllAsync()
    {
        return await _context.Events
            .Include(e => e.Era)
            .OrderByDescending(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Event?> GetByIdAsync(string id)
    {
        return await _context.Events
            .Include(e => e.Era)
            .FirstOrDefaultAsync(e => e.EventId == id);
    }

    public async Task<Event?> GetByIdWithIncludesAsync(string id)
    {
        return await _context.Events
            .Include(e => e.Era)
            .Include(e => e.EventTags)
                .ThenInclude(et => et.Tag)
            .Include(e => e.EventPeople)
                .ThenInclude(ep => ep.Person)
            .Include(e => e.EventLocations)
                .ThenInclude(el => el.Location)
            .Include(e => e.Embedding)
            .FirstOrDefaultAsync(e => e.EventId == id);
    }

    public async Task<IEnumerable<Event>> FindAsync(Expression<Func<Event, bool>> predicate)
    {
        return await _context.Events
            .Include(e => e.Era)
            .Where(predicate)
            .OrderByDescending(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Event> AddAsync(Event entity)
    {
        await _context.Events.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<Event> entities)
    {
        await _context.Events.AddRangeAsync(entities);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Event entity)
    {
        _context.Events.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Event entity)
    {
        _context.Events.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<Event> entities)
    {
        _context.Events.RemoveRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Expression<Func<Event, bool>> predicate)
    {
        return await _context.Events.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(Expression<Func<Event, bool>>? predicate = null)
    {
        if (predicate == null)
            return await _context.Events.CountAsync();

        return await _context.Events.CountAsync(predicate);
    }

    public async Task<IEnumerable<Event>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.Events
            .Include(e => e.Era)
            .Where(e => e.StartDate >= startDate && e.StartDate <= endDate)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Event>> GetByCategoryAsync(string category)
    {
        return await _context.Events
            .Include(e => e.Era)
            .Where(e => e.Category == category)
            .OrderByDescending(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Event>> GetByEraAsync(string eraId)
    {
        return await _context.Events
            .Include(e => e.Era)
            .Where(e => e.EraId == eraId)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Event>> SearchAsync(string searchTerm)
    {
        // Note: Full-text search using FTS5 would require raw SQL
        // For now, using basic LIKE search
        var term = $"%{searchTerm}%";
        return await _context.Events
            .Include(e => e.Era)
            .Where(e => EF.Functions.Like(e.Title, term) ||
                       EF.Functions.Like(e.Description ?? "", term) ||
                       EF.Functions.Like(e.RawTranscript ?? "", term))
            .OrderByDescending(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<(IEnumerable<Event> Events, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? category = null)
    {
        var query = _context.Events.Include(e => e.Era).AsQueryable();

        if (startDate.HasValue)
            query = query.Where(e => e.StartDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(e => e.StartDate <= endDate.Value);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(e => e.Category == category);

        var totalCount = await query.CountAsync();

        var events = await query
            .OrderByDescending(e => e.StartDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (events, totalCount);
    }
}
