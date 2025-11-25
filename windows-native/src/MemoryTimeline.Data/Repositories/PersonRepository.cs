using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Person entity.
/// </summary>
public class PersonRepository : IPersonRepository
{
    private readonly AppDbContext _context;

    public PersonRepository(AppDbContext context)
    {
        _context = context;
    }

    #region IRepository<Person> Implementation

    public async Task<Person?> GetByIdAsync(string id)
    {
        return await _context.People
            .Include(p => p.EventPeople)
            .FirstOrDefaultAsync(p => p.PersonId == id);
    }

    public async Task<IEnumerable<Person>> GetAllAsync()
    {
        return await _context.People
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<Person> AddAsync(Person entity)
    {
        _context.People.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(Person entity)
    {
        _context.People.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Person entity)
    {
        _context.People.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(string id)
    {
        return await _context.People.AnyAsync(p => p.PersonId == id);
    }

    public async Task<int> CountAsync()
    {
        return await _context.People.CountAsync();
    }

    public async Task<IEnumerable<Person>> FindAsync(System.Linq.Expressions.Expression<Func<Person, bool>> predicate)
    {
        return await _context.People.Where(predicate).ToListAsync();
    }

    #endregion

    #region IPersonRepository Implementation

    public async Task<Person?> GetByNameAsync(string name)
    {
        return await _context.People
            .Include(p => p.EventPeople)
            .FirstOrDefaultAsync(p => p.Name == name);
    }

    public async Task<IEnumerable<Person>> GetOrderedByNameAsync()
    {
        return await _context.People
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Person>> SearchByNameAsync(string searchTerm)
    {
        return await _context.People
            .Where(p => EF.Functions.Like(p.Name, $"%{searchTerm}%"))
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Person>> GetPeopleForEventAsync(string eventId)
    {
        return await _context.People
            .Where(p => p.EventPeople.Any(ep => ep.EventId == eventId))
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<int> GetEventCountForPersonAsync(string personId)
    {
        return await _context.EventPeople
            .CountAsync(ep => ep.PersonId == personId);
    }

    #endregion
}
