using MemoryTimeline.Data.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.StartScreen;

namespace MemoryTimeline.Services;

/// <summary>
/// Windows Jump List service implementation.
/// </summary>
public class JumpListService : IJumpListService
{
    private readonly ILogger<JumpListService> _logger;

    public JumpListService(ILogger<JumpListService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Updates the Jump List with recent events.
    /// </summary>
    public async Task UpdateRecentEventsAsync(IEnumerable<Event> events)
    {
        try
        {
            _logger.LogInformation("Updating Jump List with recent events");

            var jumpList = await JumpList.LoadCurrentAsync();
            jumpList.SystemGroupKind = JumpListSystemGroupKind.None;

            // Clear existing recent items (but keep quick actions)
            var itemsToRemove = jumpList.Items
                .Where(item => item.GroupName == "Recent Events")
                .ToList();

            foreach (var item in itemsToRemove)
            {
                jumpList.Items.Remove(item);
            }

            // Add recent events (max 10)
            foreach (var evt in events.Take(10))
            {
                var item = JumpListItem.CreateWithArguments(
                    $"event:{evt.EventId}",
                    evt.Title);

                item.GroupName = "Recent Events";
                item.Description = evt.Description ?? string.Empty;

                // Set logo (optional - requires icon file)
                // item.Logo = new Uri("ms-appx:///Assets/EventIcon.png");

                jumpList.Items.Add(item);
            }

            await jumpList.SaveAsync();

            _logger.LogInformation("Jump List updated with {Count} recent events", events.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Jump List");
            // Don't throw - Jump List is optional
        }
    }

    /// <summary>
    /// Adds quick action items to the Jump List.
    /// </summary>
    public async Task AddQuickActionsAsync()
    {
        try
        {
            _logger.LogInformation("Adding quick actions to Jump List");

            var jumpList = await JumpList.LoadCurrentAsync();
            jumpList.SystemGroupKind = JumpListSystemGroupKind.None;

            // Clear existing quick actions
            var itemsToRemove = jumpList.Items
                .Where(item => item.GroupName == "Quick Actions")
                .ToList();

            foreach (var item in itemsToRemove)
            {
                jumpList.Items.Remove(item);
            }

            // Add quick actions
            var quickActions = new[]
            {
                ("action:new-event", "New Event", "Create a new timeline event"),
                ("action:start-recording", "Start Recording", "Begin audio recording"),
                ("action:view-timeline", "View Timeline", "Open timeline view"),
                ("action:search", "Search", "Search events")
            };

            foreach (var (args, title, description) in quickActions)
            {
                var item = JumpListItem.CreateWithArguments(args, title);
                item.GroupName = "Quick Actions";
                item.Description = description;

                jumpList.Items.Add(item);
            }

            await jumpList.SaveAsync();

            _logger.LogInformation("Quick actions added to Jump List");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding quick actions to Jump List");
        }
    }

    /// <summary>
    /// Clears all Jump List items.
    /// </summary>
    public async Task ClearJumpListAsync()
    {
        try
        {
            _logger.LogInformation("Clearing Jump List");

            var jumpList = await JumpList.LoadCurrentAsync();
            jumpList.Items.Clear();
            await jumpList.SaveAsync();

            _logger.LogInformation("Jump List cleared");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing Jump List");
        }
    }

    /// <summary>
    /// Adds a specific event to the Jump List.
    /// </summary>
    public async Task AddEventToJumpListAsync(Event evt)
    {
        try
        {
            _logger.LogInformation("Adding event to Jump List: {EventId}", evt.EventId);

            var jumpList = await JumpList.LoadCurrentAsync();

            // Check if event already exists
            var existingItem = jumpList.Items
                .FirstOrDefault(item => item.Arguments == $"event:{evt.EventId}");

            if (existingItem != null)
            {
                jumpList.Items.Remove(existingItem);
            }

            var item = JumpListItem.CreateWithArguments(
                $"event:{evt.EventId}",
                evt.Title);

            item.GroupName = "Recent Events";
            item.Description = evt.Description ?? string.Empty;

            jumpList.Items.Insert(0, item); // Add at top

            // Limit to 10 recent events
            var recentItems = jumpList.Items
                .Where(i => i.GroupName == "Recent Events")
                .ToList();

            if (recentItems.Count > 10)
            {
                foreach (var oldItem in recentItems.Skip(10))
                {
                    jumpList.Items.Remove(oldItem);
                }
            }

            await jumpList.SaveAsync();

            _logger.LogInformation("Event added to Jump List");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding event to Jump List");
        }
    }
}
