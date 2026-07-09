using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Location entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class LocationRepository : ILocationRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public LocationRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Location?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations.Include(l => l.EventLocations).FirstOrDefaultAsync(l => l.LocationId == id);
    }

    public async Task<IEnumerable<Location>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations.AsNoTracking().OrderBy(l => l.Name).ToListAsync();
    }

    public async Task<Location> AddAsync(Location entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Locations.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(Location entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.Locations.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Location entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.Locations.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<Location> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Locations.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<Location> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Locations.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<Location, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<Location, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return predicate == null
            ? await context.Locations.CountAsync()
            : await context.Locations.CountAsync(predicate);
    }

    public async Task<IEnumerable<Location>> FindAsync(System.Linq.Expressions.Expression<Func<Location, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations.AsNoTracking().Where(predicate).ToListAsync();
    }

    public async Task<Location?> GetByNameAsync(string name)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations.Include(l => l.EventLocations).FirstOrDefaultAsync(l => l.Name == name);
    }

    public async Task<IEnumerable<Location>> GetOrderedByNameAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations.AsNoTracking().OrderBy(l => l.Name).ToListAsync();
    }

    public async Task<IEnumerable<Location>> SearchByNameAsync(string searchTerm)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations.AsNoTracking().Where(l => EF.Functions.Like(l.Name, $"%{searchTerm}%")).OrderBy(l => l.Name).ToListAsync();
    }

    public async Task<IEnumerable<Location>> GetLocationsForEventAsync(string eventId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Locations.AsNoTracking().Where(l => l.EventLocations.Any(el => el.EventId == eventId)).OrderBy(l => l.Name).ToListAsync();
    }

    public async Task<int> GetEventCountForLocationAsync(string locationId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventLocations.CountAsync(el => el.LocationId == locationId);
    }
}
