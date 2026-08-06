using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for <see cref="EventRevision"/>.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class EventRevisionRepository : IEventRevisionRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EventRevisionRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<EventRevision>> GetForEventAsync(string eventId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventRevisions
            .AsNoTracking() // Read-only optimization
            .Where(r => r.EventId == eventId)
            .OrderByDescending(r => r.RevisedAt)
            .ThenByDescending(r => r.RevisionId) // deterministic tie-break for same-instant writes
            .ToListAsync();
    }

    public async Task<EventRevision?> GetByIdAsync(string revisionId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RevisionId == revisionId);
    }

    public async Task<EventRevision> AddAsync(EventRevision revision)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.EventRevisions.AddAsync(revision);
        await context.SaveChangesAsync();
        return revision;
    }

    public async Task<int> CountAsync(string? eventId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.EventRevisions.AsNoTracking();
        if (!string.IsNullOrEmpty(eventId))
        {
            query = query.Where(r => r.EventId == eventId);
        }

        return await query.CountAsync();
    }
}
