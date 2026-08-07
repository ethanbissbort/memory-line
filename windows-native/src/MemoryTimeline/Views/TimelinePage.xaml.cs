using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MemoryTimeline.Core.Services;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline.Views;

/// <summary>
/// Timeline visualization page.
/// </summary>
public sealed partial class TimelinePage : Page
{
    public TimelineViewModel ViewModel { get; }

    public TimelinePage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<TimelineViewModel>();

        // Set the ViewModel on the TimelineControl
        TimelineControl.ViewModel = ViewModel;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not string parameter || string.IsNullOrWhiteSpace(parameter))
        {
            return;
        }

        // "draft:<draftId>" navigations (from the Review page's Drafts tab)
        // resume an event draft in the create-event dialog.
        if (parameter.StartsWith("draft:", StringComparison.OrdinalIgnoreCase))
        {
            var draftId = parameter.Substring("draft:".Length).Trim();
            if (!string.IsNullOrEmpty(draftId))
            {
                OpenEventDraftWhenReady(draftId);
            }
            return;
        }

        // "View on timeline" navigations pass an event id; move the viewport to it.
        var eventId = parameter;
        try
        {
            var eventService = App.Current.Services.GetRequiredService<IEventService>();
            var targetEvent = await eventService.GetEventByIdAsync(eventId);
            if (targetEvent != null && ViewModel.GoToDateCommand.CanExecute(targetEvent.StartDate))
            {
                ViewModel.GoToDateCommand.Execute(targetEvent.StartDate);
            }
        }
        catch
        {
            // Navigation focus is best-effort; the timeline still renders normally.
        }
    }

    /// <summary>
    /// Opens the create-event dialog prefilled from the given draft once the
    /// timeline control is loaded (a ContentDialog needs a live XamlRoot), then
    /// defers the actual show through the DispatcherQueue so the layout pass
    /// has completed.
    /// </summary>
    private void OpenEventDraftWhenReady(string draftId)
    {
        void ShowDeferred()
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    await TimelineControl.ShowEventDialogFromDraftAsync(draftId);
                }
                catch
                {
                    // Draft resume is best-effort; the timeline still renders normally.
                }
            });
        }

        if (TimelineControl.IsLoaded)
        {
            ShowDeferred();
            return;
        }

        Microsoft.UI.Xaml.RoutedEventHandler? loadedHandler = null;
        loadedHandler = (_, _) =>
        {
            TimelineControl.Loaded -= loadedHandler;
            ShowDeferred();
        };
        TimelineControl.Loaded += loadedHandler;
    }
}
