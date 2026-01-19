using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;
using System.Linq.Expressions;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Milestone entity.
/// </summary>
public class MilestoneRepository : IMilestoneRepository
{
    private readonly AppDbContext _context;

    public MilestoneRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Milestone>> GetAllAsync()
    {
        return await _context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<Milestone?> GetByIdAsync(string id)
    {
        return await _context.Milestones
            .Include(m => m.LinkedEra)
            .FirstOrDefaultAsync(m => m.MilestoneId == id);
    }

    public async Task<IEnumerable<Milestone>> GetOrderedByDateAsync()
    {
        return await _context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<IEnumerable<Milestone>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .Where(m => m.Date >= startDate && m.Date <= endDate)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<IEnumerable<Milestone>> GetByEraIdAsync(string eraId)
    {
        return await _context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .Where(m => m.LinkedEraId == eraId)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<IEnumerable<Milestone>> GetByTypeAsync(MilestoneType type)
    {
        return await _context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .Where(m => m.Type == type)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<IEnumerable<Milestone>> FindAsync(Expression<Func<Milestone, bool>> predicate)
    {
        return await _context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .Where(predicate)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<Milestone> AddAsync(Milestone entity)
    {
        await _context.Milestones.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<Milestone> entities)
    {
        await _context.Milestones.AddRangeAsync(entities);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Milestone entity)
    {
        _context.Milestones.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Milestone entity)
    {
        _context.Milestones.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<Milestone> entities)
    {
        _context.Milestones.RemoveRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Expression<Func<Milestone, bool>> predicate)
    {
        return await _context.Milestones.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(Expression<Func<Milestone, bool>>? predicate = null)
    {
        if (predicate == null)
            return await _context.Milestones.CountAsync();

        return await _context.Milestones.CountAsync(predicate);
    }
}
