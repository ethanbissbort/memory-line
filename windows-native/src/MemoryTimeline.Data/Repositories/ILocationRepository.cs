using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

public interface ILocationRepository : IRepository<Location>
{
    Task<Location?> GetByNameAsync(string name);
    Task<IEnumerable<Location>> GetOrderedByNameAsync();
    Task<IEnumerable<Location>> SearchByNameAsync(string searchTerm);
    Task<IEnumerable<Location>> GetLocationsForEventAsync(string eventId);
    Task<int> GetEventCountForLocationAsync(string locationId);
}
