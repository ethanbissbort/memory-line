using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Person entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class PersonRepository : IPersonRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public PersonRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    #region IRepository<Person> Implementation

    public async Task<Person?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.People
            .Include(p => p.EventPeople)
            .FirstOrDefaultAsync(p => p.PersonId == id);
    }

    public async Task<IEnumerable<Person>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.People
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<Person> AddAsync(Person entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.People.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(Person entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.People.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Person entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.People.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<Person> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.People.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<Person> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.People.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<Person, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.People.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<Person, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return predicate == null
            ? await context.People.CountAsync()
            : await context.People.CountAsync(predicate);
    }

    public async Task<IEnumerable<Person>> FindAsync(System.Linq.Expressions.Expression<Func<Person, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.People.AsNoTracking().Where(predicate).ToListAsync();
    }

    #endregion

    #region IPersonRepository Implementation

    public async Task<Person?> GetByNameAsync(string name)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.People
            .Include(p => p.EventPeople)
            .FirstOrDefaultAsync(p => p.Name == name);
    }

    public async Task<IEnumerable<Person>> GetOrderedByNameAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.People
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Person>> SearchByNameAsync(string searchTerm)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.People
            .AsNoTracking()
            .Where(p => EF.Functions.Like(p.Name, $"%{searchTerm}%"))
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Person>> GetPeopleForEventAsync(string eventId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.People
            .AsNoTracking()
            .Where(p => p.EventPeople.Any(ep => ep.EventId == eventId))
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<int> GetEventCountForPersonAsync(string personId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventPeople
            .CountAsync(ep => ep.PersonId == personId);
    }

    public async Task<IEnumerable<Person>> GetFavoritesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.People
            .AsNoTracking()
            .Where(p => p.IsFavorite)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<Dictionary<string, int>> GetEventCountsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventPeople
            .AsNoTracking()
            .GroupBy(ep => ep.PersonId)
            .Select(g => new { PersonId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PersonId, x => x.Count);
    }

    public async Task<Dictionary<string, (DateTime First, DateTime Last)>> GetEventDateRangesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Tuples cannot be materialized server-side; project into an anonymous
        // type (Min/Max translate to SQL) and build the tuple dictionary on the
        // client.
        var ranges = await context.EventPeople
            .AsNoTracking()
            .Join(context.Events,
                ep => ep.EventId,
                e => e.EventId,
                (ep, e) => new { ep.PersonId, e.StartDate })
            .GroupBy(x => x.PersonId)
            .Select(g => new
            {
                PersonId = g.Key,
                First = g.Min(x => x.StartDate),
                Last = g.Max(x => x.StartDate)
            })
            .ToListAsync();

        return ranges.ToDictionary(r => r.PersonId, r => (r.First, r.Last));
    }

    #endregion
}
