using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;
using System.Linq.Expressions;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Event entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class EventRepository : IEventRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EventRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<Event>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Events
            .Include(e => e.Era)
            .AsNoTracking() // Read-only optimization
            .OrderByDescending(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Event?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Events
            .Include(e => e.Era)
            .FirstOrDefaultAsync(e => e.EventId == id);
    }

    public async Task<Event?> GetByIdWithIncludesAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Events
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
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Events
            .Include(e => e.Era)
            .AsNoTracking() // Read-only optimization
            .Where(predicate)
            .OrderByDescending(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Event> AddAsync(Event entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Events.AddAsync(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<Event> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Events.AddRangeAsync(entities);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Event entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached (loaded by a different, already-disposed context):
        // Update attaches it and marks it Modified.
        context.Events.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Event entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.Events.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<Event> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Events.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Expression<Func<Event, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Events.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(Expression<Func<Event, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        if (predicate == null)
            return await context.Events.CountAsync();

        return await context.Events.CountAsync(predicate);
    }

    public async Task<IEnumerable<Event>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Events
            .Include(e => e.Era)
            .AsNoTracking() // Read-only optimization
            .Where(e => e.StartDate >= startDate && e.StartDate <= endDate)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Event>> GetByCategoryAsync(string category)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Events
            .Include(e => e.Era)
            .AsNoTracking() // Read-only optimization
            .Where(e => e.Category == category)
            .OrderByDescending(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Event>> GetByEraAsync(string eraId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Events
            .Include(e => e.Era)
            .AsNoTracking() // Read-only optimization
            .Where(e => e.EraId == eraId)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Event>> SearchAsync(string searchTerm)
    {
        // Note: Full-text search using FTS5 would require raw SQL
        // For now, using basic LIKE search
        var term = $"%{searchTerm}%";
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Events
            .Include(e => e.Era)
            .AsNoTracking() // Read-only optimization
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
        await using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.Events
            .Include(e => e.Era)
            .AsNoTracking() // Read-only optimization
            .AsQueryable();

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
