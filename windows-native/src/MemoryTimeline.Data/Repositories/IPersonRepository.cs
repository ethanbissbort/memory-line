using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository interface for Person entity operations.
/// </summary>
public interface IPersonRepository : IRepository<Person>
{
    /// <summary>
    /// Gets a person by their name.
    /// </summary>
    Task<Person?> GetByNameAsync(string name);

    /// <summary>
    /// Gets all people ordered by name.
    /// </summary>
    Task<IEnumerable<Person>> GetOrderedByNameAsync();

    /// <summary>
    /// Searches people by partial name match.
    /// </summary>
    Task<IEnumerable<Person>> SearchByNameAsync(string searchTerm);

    /// <summary>
    /// Gets people associated with a specific event.
    /// </summary>
    Task<IEnumerable<Person>> GetPeopleForEventAsync(string eventId);

    /// <summary>
    /// Gets the count of events associated with a person.
    /// </summary>
    Task<int> GetEventCountForPersonAsync(string personId);
}
