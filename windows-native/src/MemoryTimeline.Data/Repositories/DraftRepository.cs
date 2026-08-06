using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Draft entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class DraftRepository : IDraftRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public DraftRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    #region IRepository<Draft> Implementation

    public async Task<Draft?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Drafts
            .FirstOrDefaultAsync(d => d.DraftId == id);
    }

    public async Task<IEnumerable<Draft>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Drafts
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<Draft> AddAsync(Draft entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Drafts.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(Draft entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.Drafts.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Draft entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.Drafts.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<Draft> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Drafts.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<Draft> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Drafts.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<Draft, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Drafts.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<Draft, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return predicate == null
            ? await context.Drafts.CountAsync()
            : await context.Drafts.CountAsync(predicate);
    }

    public async Task<IEnumerable<Draft>> FindAsync(System.Linq.Expressions.Expression<Func<Draft, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Drafts.AsNoTracking().Where(predicate).ToListAsync();
    }

    #endregion

    #region IDraftRepository Implementation

    public async Task<IEnumerable<Draft>> GetByTypeAsync(string draftType)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Drafts
            .AsNoTracking()
            .Where(d => d.DraftType == draftType)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Draft>> GetAllOrderedAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Drafts
            .AsNoTracking()
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync();
    }

    #endregion
}
