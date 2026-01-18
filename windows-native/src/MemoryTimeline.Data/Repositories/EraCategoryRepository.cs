using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;
using System.Linq.Expressions;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for EraCategory entity.
/// </summary>
public class EraCategoryRepository : IEraCategoryRepository
{
    private readonly AppDbContext _context;

    public EraCategoryRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<EraCategory>> GetAllAsync()
    {
        return await _context.EraCategories
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<EraCategory?> GetByIdAsync(string id)
    {
        return await _context.EraCategories.FindAsync(id);
    }

    public async Task<EraCategory?> GetByNameAsync(string name)
    {
        return await _context.EraCategories
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower());
    }

    public async Task<IEnumerable<EraCategory>> GetOrderedAsync()
    {
        return await _context.EraCategories
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<EraCategory>> GetVisibleAsync()
    {
        return await _context.EraCategories
            .Where(c => c.IsVisible)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task EnsureDefaultCategoriesAsync()
    {
        var existingIds = await _context.EraCategories
            .Select(c => c.CategoryId)
            .ToListAsync();

        var missingCategories = DefaultEraCategories.All
            .Where(c => !existingIds.Contains(c.CategoryId))
            .ToList();

        if (missingCategories.Any())
        {
            await _context.EraCategories.AddRangeAsync(missingCategories);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<EraCategory>> FindAsync(Expression<Func<EraCategory, bool>> predicate)
    {
        return await _context.EraCategories
            .Where(predicate)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<EraCategory> AddAsync(EraCategory entity)
    {
        await _context.EraCategories.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<EraCategory> entities)
    {
        await _context.EraCategories.AddRangeAsync(entities);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(EraCategory entity)
    {
        _context.EraCategories.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(EraCategory entity)
    {
        _context.EraCategories.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<EraCategory> entities)
    {
        _context.EraCategories.RemoveRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Expression<Func<EraCategory, bool>> predicate)
    {
        return await _context.EraCategories.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(Expression<Func<EraCategory, bool>>? predicate = null)
    {
        if (predicate == null)
            return await _context.EraCategories.CountAsync();

        return await _context.EraCategories.CountAsync(predicate);
    }
}
