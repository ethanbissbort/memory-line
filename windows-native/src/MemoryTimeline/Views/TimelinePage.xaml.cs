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

        // "View on timeline" navigations pass an event id; move the viewport to it.
        if (e.Parameter is string eventId && !string.IsNullOrWhiteSpace(eventId))
        {
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
    }
}
