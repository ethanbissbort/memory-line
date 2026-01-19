using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;
using System.Linq.Expressions;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Era entity.
/// </summary>
public class EraRepository : IEraRepository
{
    private readonly AppDbContext _context;

    public EraRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Era>> GetAllAsync()
    {
        return await _context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Era?> GetByIdAsync(string id)
    {
        return await _context.Eras
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .FirstOrDefaultAsync(e => e.EraId == id);
    }

    public async Task<Era?> GetByNameAsync(string name)
    {
        return await _context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .FirstOrDefaultAsync(e => e.Name.ToLower() == name.ToLower());
    }

    public async Task<IEnumerable<Era>> GetOrderedByDateAsync()
    {
        return await _context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Era?> GetByDateAsync(DateTime date)
    {
        return await _context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .Where(e => e.StartDate <= date && (e.EndDate == null || e.EndDate >= date))
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<Era>> FindAsync(Expression<Func<Era, bool>> predicate)
    {
        return await _context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .Where(predicate)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<Era> AddAsync(Era entity)
    {
        await _context.Eras.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<Era> entities)
    {
        await _context.Eras.AddRangeAsync(entities);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Era entity)
    {
        _context.Eras.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Era entity)
    {
        _context.Eras.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<Era> entities)
    {
        _context.Eras.RemoveRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Expression<Func<Era, bool>> predicate)
    {
        return await _context.Eras.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(Expression<Func<Era, bool>>? predicate = null)
    {
        if (predicate == null)
            return await _context.Eras.CountAsync();

        return await _context.Eras.CountAsync(predicate);
    }

    public async Task<IEnumerable<Era>> GetByCategoryIdAsync(string categoryId)
    {
        return await _context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .Where(e => e.CategoryId == categoryId)
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Era>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .Where(e => e.StartDate <= endDate && (e.EndDate == null || e.EndDate >= startDate))
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<IGrouping<EraCategory?, Era>>> GetGroupedByCategoryAsync()
    {
        var eras = await _context.Eras
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.EraTags)
            .OrderBy(e => e.Category != null ? e.Category.SortOrder : int.MaxValue)
            .ThenBy(e => e.StartDate)
            .ToListAsync();

        return eras.GroupBy(e => e.Category);
    }
}
