using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository interface for EraCategory entity.
/// </summary>
public interface IEraCategoryRepository : IRepository<EraCategory>
{
    /// <summary>
    /// Gets category by name.
    /// </summary>
    Task<EraCategory?> GetByNameAsync(string name);

    /// <summary>
    /// Gets all categories ordered by sort order.
    /// </summary>
    Task<IEnumerable<EraCategory>> GetOrderedAsync();

    /// <summary>
    /// Gets all visible categories.
    /// </summary>
    Task<IEnumerable<EraCategory>> GetVisibleAsync();

    /// <summary>
    /// Ensures default categories exist in the database.
    /// </summary>
    Task EnsureDefaultCategoriesAsync();
}
