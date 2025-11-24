using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository interface for Tag entity operations.
/// </summary>
public interface ITagRepository : IRepository<Tag>
{
    /// <summary>
    /// Gets a tag by its name.
    /// </summary>
    Task<Tag?> GetByNameAsync(string tagName);

    /// <summary>
    /// Gets all tags ordered by name.
    /// </summary>
    Task<IEnumerable<Tag>> GetOrderedByNameAsync();

    /// <summary>
    /// Gets tags that are frequently used (above threshold count).
    /// </summary>
    Task<IEnumerable<Tag>> GetFrequentlyUsedAsync(int minimumUsageCount = 5);

    /// <summary>
    /// Searches tags by partial name match.
    /// </summary>
    Task<IEnumerable<Tag>> SearchByNameAsync(string searchTerm);

    /// <summary>
    /// Gets tags associated with a specific event.
    /// </summary>
    Task<IEnumerable<Tag>> GetTagsForEventAsync(string eventId);

    /// <summary>
    /// Gets the count of events associated with a tag.
    /// </summary>
    Task<int> GetEventCountForTagAsync(string tagId);
}
