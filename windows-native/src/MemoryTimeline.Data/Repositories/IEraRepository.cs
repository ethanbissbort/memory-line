using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository interface for Era entity.
/// </summary>
public interface IEraRepository : IRepository<Era>
{
    /// <summary>
    /// Gets era by name.
    /// </summary>
    Task<Era?> GetByNameAsync(string name);

    /// <summary>
    /// Gets eras ordered by start date.
    /// </summary>
    Task<IEnumerable<Era>> GetOrderedByDateAsync();

    /// <summary>
    /// Gets era that contains a specific date.
    /// </summary>
    Task<Era?> GetByDateAsync(DateTime date);

    /// <summary>
    /// Gets eras by category ID.
    /// </summary>
    Task<IEnumerable<Era>> GetByCategoryIdAsync(string categoryId);

    /// <summary>
    /// Gets eras within a date range.
    /// </summary>
    Task<IEnumerable<Era>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);

    /// <summary>
    /// Gets eras grouped by category.
    /// </summary>
    Task<IEnumerable<IGrouping<EraCategory?, Era>>> GetGroupedByCategoryAsync();
}
