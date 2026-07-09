using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;
using System.Linq.Expressions;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Milestone entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class MilestoneRepository : IMilestoneRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MilestoneRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<Milestone>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<Milestone?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Milestones
            .Include(m => m.LinkedEra)
            .FirstOrDefaultAsync(m => m.MilestoneId == id);
    }

    public async Task<IEnumerable<Milestone>> GetOrderedByDateAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<IEnumerable<Milestone>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .Where(m => m.Date >= startDate && m.Date <= endDate)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<IEnumerable<Milestone>> GetByEraIdAsync(string eraId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .Where(m => m.LinkedEraId == eraId)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<IEnumerable<Milestone>> GetByTypeAsync(MilestoneType type)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .Where(m => m.Type == type)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<IEnumerable<Milestone>> FindAsync(Expression<Func<Milestone, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Milestones
            .AsNoTracking()
            .Include(m => m.LinkedEra)
            .Where(predicate)
            .OrderBy(m => m.Date)
            .ToListAsync();
    }

    public async Task<Milestone> AddAsync(Milestone entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Milestones.AddAsync(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<Milestone> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Milestones.AddRangeAsync(entities);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Milestone entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.Milestones.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Milestone entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.Milestones.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<Milestone> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Milestones.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Expression<Func<Milestone, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Milestones.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(Expression<Func<Milestone, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        if (predicate == null)
            return await context.Milestones.CountAsync();

        return await context.Milestones.CountAsync(predicate);
    }
}
