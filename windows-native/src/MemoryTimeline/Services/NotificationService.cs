using MemoryTimeline.Core.Services;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MemoryTimeline.Services;

/// <summary>
/// Windows notification service implementation using Windows App SDK.
/// Implements the Core <see cref="INotificationService"/> contract so that
/// Core consumers (e.g. QueueService) and the app share a single abstraction.
/// </summary>
public class NotificationService : INotificationService, IDisposable
{
    private readonly Dictionary<string, Action> _actionCallbacks = new();
    private readonly object _callbackLock = new();
    private bool _isInitialized;

    public NotificationService()
    {
        InitializeNotifications();
    }

    private void InitializeNotifications()
    {
        if (_isInitialized) return;

        // Registering the notification manager can throw in unpackaged / unsupported
        // contexts. Failing here would crash resolution of this singleton, so degrade
        // gracefully: notifications simply become no-ops if registration fails.
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NotificationService initialization failed: {ex.Message}");
            _isInitialized = false;
        }
    }

    private void ShowNotification(AppNotification notification)
    {
        if (!_isInitialized)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            // Showing a toast can fail (e.g. unpackaged without proper registration).
            // Notifications are non-critical, so swallow and continue.
            System.Diagnostics.Debug.WriteLine($"Failed to show notification: {ex.Message}");
        }
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

            ShowNotification(builder.BuildNotification());
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
            lock (_callbackLock)
            {
                _actionCallbacks[actionId] = actionCallback;
            }

            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .AddButton(new AppNotificationButton(actionText)
                    .AddArgument("action", actionId));

            ShowNotification(builder.BuildNotification());
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

            ShowNotification(builder.BuildNotification());
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

            ShowNotification(builder.BuildNotification());
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
        // Handle notification actions. This fires on a background thread.
        if (args.Arguments.TryGetValue("action", out var actionId))
        {
            Action? callback = null;
            lock (_callbackLock)
            {
                if (_actionCallbacks.TryGetValue(actionId, out callback))
                {
                    _actionCallbacks.Remove(actionId);
                }
            }

            callback?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_isInitialized)
        {
            try
            {
                AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NotificationService disposal failed: {ex.Message}");
            }

            lock (_callbackLock)
            {
                _actionCallbacks.Clear();
            }
            _isInitialized = false;
        }
    }
}
