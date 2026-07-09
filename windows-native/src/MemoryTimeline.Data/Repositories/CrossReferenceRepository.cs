using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for CrossReference entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class CrossReferenceRepository : ICrossReferenceRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public CrossReferenceRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<CrossReference?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.CrossReferences.Include(cr => cr.Event1).Include(cr => cr.Event2).FirstOrDefaultAsync(cr => cr.ReferenceId == id);
    }

    public async Task<IEnumerable<CrossReference>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.CrossReferences.AsNoTracking().OrderByDescending(cr => cr.ConfidenceScore).ToListAsync();
    }

    public async Task<CrossReference> AddAsync(CrossReference entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.CrossReferences.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(CrossReference entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.CrossReferences.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(CrossReference entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.CrossReferences.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<CrossReference> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.CrossReferences.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<CrossReference> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.CrossReferences.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<CrossReference, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.CrossReferences.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<CrossReference, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return predicate == null
            ? await context.CrossReferences.CountAsync()
            : await context.CrossReferences.CountAsync(predicate);
    }

    public async Task<IEnumerable<CrossReference>> FindAsync(System.Linq.Expressions.Expression<Func<CrossReference, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.CrossReferences.AsNoTracking().Where(predicate).ToListAsync();
    }

    public async Task<IEnumerable<CrossReference>> GetReferencesForEventAsync(string eventId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.CrossReferences.AsNoTracking().Where(cr => cr.EventId1 == eventId || cr.EventId2 == eventId)
            .Include(cr => cr.Event1).Include(cr => cr.Event2).OrderByDescending(cr => cr.ConfidenceScore).ToListAsync();
    }

    public async Task<IEnumerable<CrossReference>> GetByRelationshipTypeAsync(RelationshipType relationshipType)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.CrossReferences.AsNoTracking().Where(cr => cr.RelationshipType == relationshipType.ToStringValue())
            .OrderByDescending(cr => cr.ConfidenceScore).ToListAsync();
    }

    public async Task<IEnumerable<CrossReference>> GetHighConfidenceReferencesAsync(double minimumConfidence = 0.7)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.CrossReferences.AsNoTracking().Where(cr => cr.ConfidenceScore >= minimumConfidence)
            .OrderByDescending(cr => cr.ConfidenceScore).ToListAsync();
    }

    public async Task<CrossReference?> GetReferenceAsync(string eventId1, string eventId2)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.CrossReferences.FirstOrDefaultAsync(cr =>
            (cr.EventId1 == eventId1 && cr.EventId2 == eventId2) || (cr.EventId1 == eventId2 && cr.EventId2 == eventId1));
    }

    public async Task<int> GetReferenceCountForEventAsync(string eventId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.CrossReferences.CountAsync(cr => cr.EventId1 == eventId || cr.EventId2 == eventId);
    }
}
