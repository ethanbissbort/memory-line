using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

public interface IPendingEventRepository : IRepository<PendingEvent>
{
    Task<IEnumerable<PendingEvent>> GetByQueueIdAsync(string queueId);
    Task<IEnumerable<PendingEvent>> GetByStatusAsync(PendingStatus status);
    Task<int> GetCountByStatusAsync(PendingStatus status);
    Task<IEnumerable<PendingEvent>> GetRecentAsync(int count = 10);
}
