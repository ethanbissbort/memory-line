using System;
using System.Threading.Tasks;

namespace MemoryTimeline.Services;

/// <summary>
/// Service interface for Windows notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows a simple toast notification.
    /// </summary>
    /// <param name="title">Notification title</param>
    /// <param name="message">Notification message</param>
    Task ShowNotificationAsync(string title, string message);

    /// <summary>
    /// Shows a notification with an action button.
    /// </summary>
    /// <param name="title">Notification title</param>
    /// <param name="message">Notification message</param>
    /// <param name="actionText">Action button text</param>
    /// <param name="actionCallback">Callback when action is clicked</param>
    Task ShowActionNotificationAsync(string title, string message, string actionText, Action actionCallback);

    /// <summary>
    /// Shows a success notification.
    /// </summary>
    Task ShowSuccessAsync(string title, string message);

    /// <summary>
    /// Shows an error notification.
    /// </summary>
    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Shows a processing complete notification.
    /// </summary>
    /// <param name="recordingCount">Number of recordings processed</param>
    /// <param name="eventCount">Number of events extracted</param>
    Task ShowProcessingCompleteAsync(int recordingCount, int eventCount);
}

/// <summary>
/// Notification types for styling.
/// </summary>
public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}
