using MemoryTimeline.Data.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MemoryTimeline.Services;

/// <summary>
/// Service interface for Windows Jump List management.
/// </summary>
public interface IJumpListService
{
    /// <summary>
    /// Updates the Jump List with recent events.
    /// </summary>
    /// <param name="events">Recent events to add</param>
    Task UpdateRecentEventsAsync(IEnumerable<Event> events);

    /// <summary>
    /// Adds quick action items to the Jump List.
    /// </summary>
    Task AddQuickActionsAsync();

    /// <summary>
    /// Clears all Jump List items.
    /// </summary>
    Task ClearJumpListAsync();

    /// <summary>
    /// Adds a specific event to the Jump List.
    /// </summary>
    /// <param name="evt">Event to add</param>
    Task AddEventToJumpListAsync(Event evt);
}
