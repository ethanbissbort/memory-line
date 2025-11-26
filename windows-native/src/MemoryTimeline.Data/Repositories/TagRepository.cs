using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for Tag entity.
/// </summary>
public class TagRepository : ITagRepository
{
    private readonly AppDbContext _context;

    public TagRepository(AppDbContext context)
    {
        _context = context;
    }

    #region IRepository<Tag> Implementation

    public async Task<Tag?> GetByIdAsync(string id)
    {
        return await _context.Tags
            .Include(t => t.EventTags)
            .FirstOrDefaultAsync(t => t.TagId == id);
    }

    public async Task<IEnumerable<Tag>> GetAllAsync()
    {
        return await _context.Tags
            .OrderBy(t => t.TagName)
            .ToListAsync();
    }

    public async Task<Tag> AddAsync(Tag entity)
    {
        _context.Tags.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(Tag entity)
    {
        _context.Tags.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Tag entity)
    {
        _context.Tags.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<Tag> entities)
    {
        _context.Tags.AddRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<Tag> entities)
    {
        _context.Tags.RemoveRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<Tag, bool>> predicate)
    {
        return await _context.Tags.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<Tag, bool>>? predicate = null)
    {
        return predicate == null
            ? await _context.Tags.CountAsync()
            : await _context.Tags.CountAsync(predicate);
    }

    public async Task<IEnumerable<Tag>> FindAsync(System.Linq.Expressions.Expression<Func<Tag, bool>> predicate)
    {
        return await _context.Tags.Where(predicate).ToListAsync();
    }

    #endregion

    #region ITagRepository Implementation

    public async Task<Tag?> GetByNameAsync(string tagName)
    {
        return await _context.Tags
            .Include(t => t.EventTags)
            .FirstOrDefaultAsync(t => t.TagName == tagName);
    }

    public async Task<IEnumerable<Tag>> GetOrderedByNameAsync()
    {
        return await _context.Tags
            .OrderBy(t => t.TagName)
            .ToListAsync();
    }

    public async Task<IEnumerable<Tag>> GetFrequentlyUsedAsync(int minimumUsageCount = 5)
    {
        return await _context.Tags
            .Where(t => t.EventTags.Count >= minimumUsageCount)
            .OrderByDescending(t => t.EventTags.Count)
            .ToListAsync();
    }

    public async Task<IEnumerable<Tag>> SearchByNameAsync(string searchTerm)
    {
        return await _context.Tags
            .Where(t => EF.Functions.Like(t.TagName, $"%{searchTerm}%"))
            .OrderBy(t => t.TagName)
            .ToListAsync();
    }

    public async Task<IEnumerable<Tag>> GetTagsForEventAsync(string eventId)
    {
        return await _context.Tags
            .Where(t => t.EventTags.Any(et => et.EventId == eventId))
            .OrderBy(t => t.TagName)
            .ToListAsync();
    }

    public async Task<int> GetEventCountForTagAsync(string tagId)
    {
        return await _context.EventTags
            .CountAsync(et => et.TagId == tagId);
    }

    #endregion
}
