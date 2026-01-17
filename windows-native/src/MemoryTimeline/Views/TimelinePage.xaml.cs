using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
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
}
