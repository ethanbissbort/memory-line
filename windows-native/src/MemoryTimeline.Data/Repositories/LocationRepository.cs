using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

public class LocationRepository : ILocationRepository
{
    private readonly AppDbContext _context;

    public LocationRepository(AppDbContext context) => _context = context;

    public async Task<Location?> GetByIdAsync(string id) =>
        await _context.Locations.Include(l => l.EventLocations).FirstOrDefaultAsync(l => l.LocationId == id);

    public async Task<IEnumerable<Location>> GetAllAsync() =>
        await _context.Locations.OrderBy(l => l.Name).ToListAsync();

    public async Task<Location> AddAsync(Location entity)
    {
        _context.Locations.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(Location entity)
    {
        _context.Locations.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Location entity)
    {
        _context.Locations.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(string id) =>
        await _context.Locations.AnyAsync(l => l.LocationId == id);

    public async Task<int> CountAsync() =>
        await _context.Locations.CountAsync();

    public async Task<IEnumerable<Location>> FindAsync(System.Linq.Expressions.Expression<Func<Location, bool>> predicate) =>
        await _context.Locations.Where(predicate).ToListAsync();

    public async Task<Location?> GetByNameAsync(string name) =>
        await _context.Locations.Include(l => l.EventLocations).FirstOrDefaultAsync(l => l.Name == name);

    public async Task<IEnumerable<Location>> GetOrderedByNameAsync() =>
        await _context.Locations.OrderBy(l => l.Name).ToListAsync();

    public async Task<IEnumerable<Location>> SearchByNameAsync(string searchTerm) =>
        await _context.Locations.Where(l => EF.Functions.Like(l.Name, $"%{searchTerm}%")).OrderBy(l => l.Name).ToListAsync();

    public async Task<IEnumerable<Location>> GetLocationsForEventAsync(string eventId) =>
        await _context.Locations.Where(l => l.EventLocations.Any(el => el.EventId == eventId)).OrderBy(l => l.Name).ToListAsync();

    public async Task<int> GetEventCountForLocationAsync(string locationId) =>
        await _context.EventLocations.CountAsync(el => el.LocationId == locationId);
}
