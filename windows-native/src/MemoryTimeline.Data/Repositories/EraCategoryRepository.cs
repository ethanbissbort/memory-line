using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;
using System.Linq.Expressions;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for EraCategory entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class EraCategoryRepository : IEraCategoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EraCategoryRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<EraCategory>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EraCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<EraCategory?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EraCategories.FindAsync(id);
    }

    public async Task<EraCategory?> GetByNameAsync(string name)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EraCategories
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower());
    }

    public async Task<IEnumerable<EraCategory>> GetOrderedAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EraCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<EraCategory>> GetVisibleAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EraCategories
            .AsNoTracking()
            .Where(c => c.IsVisible)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task EnsureDefaultCategoriesAsync()
    {
        // Fetch-then-save happens inside a single context.
        await using var context = await _contextFactory.CreateDbContextAsync();
        var existingIds = await context.EraCategories
            .Select(c => c.CategoryId)
            .ToListAsync();

        var missingCategories = DefaultEraCategories.All
            .Where(c => !existingIds.Contains(c.CategoryId))
            .ToList();

        if (missingCategories.Any())
        {
            await context.EraCategories.AddRangeAsync(missingCategories);
            await context.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<EraCategory>> FindAsync(Expression<Func<EraCategory, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EraCategories
            .AsNoTracking()
            .Where(predicate)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<EraCategory> AddAsync(EraCategory entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.EraCategories.AddAsync(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<EraCategory> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.EraCategories.AddRangeAsync(entities);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(EraCategory entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.EraCategories.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(EraCategory entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.EraCategories.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<EraCategory> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.EraCategories.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Expression<Func<EraCategory, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EraCategories.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(Expression<Func<EraCategory, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        if (predicate == null)
            return await context.EraCategories.CountAsync();

        return await context.EraCategories.CountAsync(predicate);
    }
}
