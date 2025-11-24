using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

public class EventEmbeddingRepository : IEventEmbeddingRepository
{
    private readonly AppDbContext _context;

    public EventEmbeddingRepository(AppDbContext context) => _context = context;

    public async Task<EventEmbedding?> GetByIdAsync(string id) =>
        await _context.EventEmbeddings.Include(e => e.Event).FirstOrDefaultAsync(e => e.EmbeddingId == id);

    public async Task<IEnumerable<EventEmbedding>> GetAllAsync() =>
        await _context.EventEmbeddings.ToListAsync();

    public async Task<EventEmbedding> AddAsync(EventEmbedding entity)
    {
        _context.EventEmbeddings.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(EventEmbedding entity)
    {
        _context.EventEmbeddings.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(EventEmbedding entity)
    {
        _context.EventEmbeddings.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(string id) =>
        await _context.EventEmbeddings.AnyAsync(e => e.EmbeddingId == id);

    public async Task<int> CountAsync() =>
        await _context.EventEmbeddings.CountAsync();

    public async Task<IEnumerable<EventEmbedding>> FindAsync(System.Linq.Expressions.Expression<Func<EventEmbedding, bool>> predicate) =>
        await _context.EventEmbeddings.Where(predicate).ToListAsync();

    public async Task<EventEmbedding?> GetByEventIdAsync(string eventId) =>
        await _context.EventEmbeddings.FirstOrDefaultAsync(e => e.EventId == eventId);

    public async Task<IEnumerable<EventEmbedding>> GetByProviderAsync(string provider) =>
        await _context.EventEmbeddings.Where(e => e.EmbeddingProvider == provider).ToListAsync();

    public async Task<IEnumerable<EventEmbedding>> FindSimilarAsync(double[] queryVector, int topK = 10, double minimumSimilarity = 0.7)
    {
        // This is a simplified implementation - in production, use vector similarity search
        // For SQLite, we'd need to implement cosine similarity in-memory
        var allEmbeddings = await _context.EventEmbeddings.Include(e => e.Event).ToListAsync();

        var similarities = allEmbeddings.Select(e => new
        {
            Embedding = e,
            Similarity = CalculateCosineSimilarity(queryVector, DeserializeVector(e.EmbeddingVector))
        })
        .Where(x => x.Similarity >= minimumSimilarity)
        .OrderByDescending(x => x.Similarity)
        .Take(topK)
        .Select(x => x.Embedding);

        return similarities;
    }

    public async Task<int> GetCountByProviderAsync(string provider) =>
        await _context.EventEmbeddings.CountAsync(e => e.EmbeddingProvider == provider);

    private static double[] DeserializeVector(string vectorString)
    {
        return System.Text.Json.JsonSerializer.Deserialize<double[]>(vectorString) ?? Array.Empty<double>();
    }

    private static double CalculateCosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length) return 0;

        double dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
