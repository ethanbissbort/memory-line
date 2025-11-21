using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MemoryTimeline.Views;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline;

/// <summary>
/// Main window for the Memory Timeline application.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;

        // Set window title bar customization
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        // Navigate to Timeline page by default
        ContentFrame.Navigate(typeof(TimelinePage));
    }

    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer != null)
        {
            var navItemTag = args.SelectedItemContainer.Tag?.ToString();
            NavigateToPage(navItemTag);
        }
    }

    private void NavigateToPage(string? pageTag)
    {
        Type? pageType = pageTag switch
        {
            "Timeline" => typeof(TimelinePage),
            "Queue" => typeof(QueuePage),
            "Search" => typeof(SearchPage),
            "Analytics" => typeof(AnalyticsPage),
            "Settings" => typeof(SettingsPage),
            _ => null
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
