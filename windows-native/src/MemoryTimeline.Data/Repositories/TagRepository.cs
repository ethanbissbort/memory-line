using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Tag entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class TagRepository : ITagRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TagRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    #region IRepository<Tag> Implementation

    public async Task<Tag?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags
            .Include(t => t.EventTags)
            .FirstOrDefaultAsync(t => t.TagId == id);
    }

    public async Task<IEnumerable<Tag>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags
            .AsNoTracking()
            .OrderBy(t => t.TagName)
            .ToListAsync();
    }

    public async Task<Tag> AddAsync(Tag entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Tags.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(Tag entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.Tags.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Tag entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.Tags.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<Tag> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Tags.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<Tag> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Tags.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<Tag, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<Tag, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return predicate == null
            ? await context.Tags.CountAsync()
            : await context.Tags.CountAsync(predicate);
    }

    public async Task<IEnumerable<Tag>> FindAsync(System.Linq.Expressions.Expression<Func<Tag, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags.AsNoTracking().Where(predicate).ToListAsync();
    }

    #endregion

    #region ITagRepository Implementation

    public async Task<Tag?> GetByNameAsync(string tagName)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags
            .Include(t => t.EventTags)
            .FirstOrDefaultAsync(t => t.TagName == tagName);
    }

    public async Task<IEnumerable<Tag>> GetOrderedByNameAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags
            .AsNoTracking()
            .OrderBy(t => t.TagName)
            .ToListAsync();
    }

    public async Task<IEnumerable<Tag>> GetFrequentlyUsedAsync(int minimumUsageCount = 5)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags
            .AsNoTracking()
            .Where(t => t.EventTags.Count >= minimumUsageCount)
            .OrderByDescending(t => t.EventTags.Count)
            .ToListAsync();
    }

    public async Task<IEnumerable<Tag>> SearchByNameAsync(string searchTerm)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags
            .AsNoTracking()
            .Where(t => EF.Functions.Like(t.TagName, $"%{searchTerm}%"))
            .OrderBy(t => t.TagName)
            .ToListAsync();
    }

    public async Task<IEnumerable<Tag>> GetTagsForEventAsync(string eventId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Tags
            .AsNoTracking()
            .Where(t => t.EventTags.Any(et => et.EventId == eventId))
            .OrderBy(t => t.TagName)
            .ToListAsync();
    }

    public async Task<int> GetEventCountForTagAsync(string tagId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventTags
            .CountAsync(et => et.TagId == tagId);
    }

    #endregion
}
