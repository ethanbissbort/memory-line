using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

public interface IEventEmbeddingRepository : IRepository<EventEmbedding>
{
    Task<EventEmbedding?> GetByEventIdAsync(string eventId);
    Task<IEnumerable<EventEmbedding>> GetByProviderAsync(string provider);
    Task<IEnumerable<EventEmbedding>> FindSimilarAsync(double[] queryVector, int topK = 10, double minimumSimilarity = 0.7);
    Task<int> GetCountByProviderAsync(string provider);
}
