using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for EventEmbedding entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class EventEmbeddingRepository : IEventEmbeddingRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EventEmbeddingRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<EventEmbedding?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventEmbeddings.Include(e => e.Event).FirstOrDefaultAsync(e => e.EmbeddingId == id);
    }

    public async Task<IEnumerable<EventEmbedding>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventEmbeddings.AsNoTracking().ToListAsync();
    }

    public async Task<EventEmbedding> AddAsync(EventEmbedding entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.EventEmbeddings.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(EventEmbedding entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.EventEmbeddings.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(EventEmbedding entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.EventEmbeddings.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<EventEmbedding> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.EventEmbeddings.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<EventEmbedding> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.EventEmbeddings.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<EventEmbedding, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventEmbeddings.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<EventEmbedding, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return predicate == null
            ? await context.EventEmbeddings.CountAsync()
            : await context.EventEmbeddings.CountAsync(predicate);
    }

    public async Task<IEnumerable<EventEmbedding>> FindAsync(System.Linq.Expressions.Expression<Func<EventEmbedding, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventEmbeddings.AsNoTracking().Where(predicate).ToListAsync();
    }

    public async Task<EventEmbedding?> GetByEventIdAsync(string eventId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventEmbeddings.FirstOrDefaultAsync(e => e.EventId == eventId);
    }

    public async Task<IEnumerable<EventEmbedding>> GetByProviderAsync(string provider)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventEmbeddings.AsNoTracking().Where(e => e.EmbeddingProvider == provider).ToListAsync();
    }

    public async Task<IEnumerable<EventEmbedding>> FindSimilarAsync(double[] queryVector, int topK = 10, double minimumSimilarity = 0.7)
    {
        // This is a simplified implementation - in production, use vector similarity search
        // For SQLite, we'd need to implement cosine similarity in-memory
        await using var context = await _contextFactory.CreateDbContextAsync();
        var allEmbeddings = await context.EventEmbeddings.AsNoTracking().Include(e => e.Event).ToListAsync();

        var similarities = allEmbeddings.Select(e => new
        {
            Embedding = e,
            Similarity = CalculateCosineSimilarity(queryVector, DeserializeVector(e.EmbeddingVector))
        })
        .Where(x => x.Similarity >= minimumSimilarity)
        .OrderByDescending(x => x.Similarity)
        .Take(topK)
        .Select(x => x.Embedding)
        .ToList();

        return similarities;
    }

    public async Task<int> GetCountByProviderAsync(string provider)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EventEmbeddings.CountAsync(e => e.EmbeddingProvider == provider);
    }

    private static double[] DeserializeVector(string vectorString)
    {
        return System.Text.Json.JsonSerializer.Deserialize<double[]>(vectorString) ?? Array.Empty<double>();
    }

    private static double CalculateCosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        double dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        // Guard against zero-magnitude vectors (would produce NaN/Infinity).
        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        if (denominator == 0) return 0;

        return dotProduct / denominator;
    }
}
