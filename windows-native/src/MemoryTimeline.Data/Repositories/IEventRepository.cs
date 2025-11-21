using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository interface for Event entity with specialized operations.
/// </summary>
public interface IEventRepository : IRepository<Event>
{
    /// <summary>
    /// Gets events within a date range.
    /// </summary>
    Task<IEnumerable<Event>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);

    /// <summary>
    /// Gets events by category.
    /// </summary>
    Task<IEnumerable<Event>> GetByCategoryAsync(string category);

    /// <summary>
    /// Gets events by era.
    /// </summary>
    Task<IEnumerable<Event>> GetByEraAsync(string eraId);

    /// <summary>
    /// Searches events using full-text search.
    /// </summary>
    Task<IEnumerable<Event>> SearchAsync(string searchTerm);

    /// <summary>
    /// Gets events with pagination.
    /// </summary>
    Task<(IEnumerable<Event> Events, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? category = null);

    /// <summary>
    /// Gets events with their related data (Era, Tags, People, Locations).
    /// </summary>
    Task<Event?> GetByIdWithIncludesAsync(string id);
}
