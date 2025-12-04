using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MemoryTimeline.Services;

/// <summary>
/// Windows notification service implementation using Windows App SDK.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly Dictionary<string, Action> _actionCallbacks = new();
    private bool _isInitialized;

    public NotificationService()
    {
        InitializeNotifications();
    }

    private void InitializeNotifications()
    {
        if (_isInitialized) return;

        // Register for notification events
        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
        AppNotificationManager.Default.Register();

        _isInitialized = true;
    }

    /// <summary>
    /// Shows a simple toast notification.
    /// </summary>
    public async Task ShowNotificationAsync(string title, string message)
    {
        await Task.Run(() =>
        {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message);

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
        });
    }

    /// <summary>
    /// Shows a notification with an action button.
    /// </summary>
    public async Task ShowActionNotificationAsync(string title, string message, string actionText, Action actionCallback)
    {
        await Task.Run(() =>
        {
            var actionId = Guid.NewGuid().ToString();
            _actionCallbacks[actionId] = actionCallback;

            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .AddButton(new AppNotificationButton(actionText)
                    .AddArgument("action", actionId));

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
        });
    }

    /// <summary>
    /// Shows a success notification.
    /// </summary>
    public async Task ShowSuccessAsync(string title, string message)
    {
        await Task.Run(() =>
        {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message);

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
        });
    }

    /// <summary>
    /// Shows an error notification.
    /// </summary>
    public async Task ShowErrorAsync(string title, string message)
    {
        await Task.Run(() =>
        {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message);

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
        });
    }

    /// <summary>
    /// Shows a processing complete notification.
    /// </summary>
    public async Task ShowProcessingCompleteAsync(int recordingCount, int eventCount)
    {
        var title = "Processing Complete";
        var message = $"Processed {recordingCount} recording{(recordingCount != 1 ? "s" : "")} and extracted {eventCount} event{(eventCount != 1 ? "s" : "")}";

        await ShowSuccessAsync(title, message);
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // Handle notification actions
        if (args.Arguments.TryGetValue("action", out var actionId))
        {
            if (_actionCallbacks.TryGetValue(actionId, out var callback))
            {
                callback?.Invoke();
                _actionCallbacks.Remove(actionId);
            }
        }
    }

    public void Dispose()
    {
        if (_isInitialized)
        {
            AppNotificationManager.Default.Unregister();
            _actionCallbacks.Clear();
            _isInitialized = false;
        }
    }
}
