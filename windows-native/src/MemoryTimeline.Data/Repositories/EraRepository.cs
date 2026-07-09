using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;
using System.Linq.Expressions;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Era entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class EraRepository : IEraRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EraRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<Era>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Era?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Eras
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .FirstOrDefaultAsync(e => e.EraId == id);
    }

    public async Task<Era?> GetByNameAsync(string name)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .FirstOrDefaultAsync(e => e.Name.ToLower() == name.ToLower());
    }

    public async Task<IEnumerable<Era>> GetOrderedByDateAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Era?> GetByDateAsync(DateTime date)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .Where(e => e.StartDate <= date && (e.EndDate == null || e.EndDate >= date))
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<Era>> FindAsync(Expression<Func<Era, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .Where(predicate)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Era> AddAsync(Era entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Eras.AddAsync(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<Era> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Eras.AddRangeAsync(entities);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Era entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.Eras.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Era entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.Eras.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<Era> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Eras.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Expression<Func<Era, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Eras.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(Expression<Func<Era, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        if (predicate == null)
            return await context.Eras.CountAsync();

        return await context.Eras.CountAsync(predicate);
    }

    public async Task<IEnumerable<Era>> GetByCategoryIdAsync(string categoryId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .Where(e => e.CategoryId == categoryId)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Era>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .Where(e => e.StartDate <= endDate && (e.EndDate == null || e.EndDate >= startDate))
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<IGrouping<EraCategory?, Era>>> GetGroupedByCategoryAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var eras = await context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .OrderBy(e => e.Category != null ? e.Category.SortOrder : int.MaxValue)
            .ThenBy(e => e.StartDate)
            .ToListAsync();

        return eras.GroupBy(e => e.Category);
    }
}
