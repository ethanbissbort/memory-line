using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline.Views;

/// <summary>
/// Page for displaying event connections, cross-references, and AI-powered insights.
/// </summary>
public sealed partial class ConnectionsPage : Page
{
    public ConnectionsViewModel ViewModel { get; }

    public ConnectionsPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<ConnectionsViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Check if an event ID was passed as a parameter
        if (e.Parameter is string eventId && !string.IsNullOrEmpty(eventId))
        {
            await ViewModel.LoadConnectionsForEventAsync(eventId);
        }
    }
}
