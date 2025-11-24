using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MemoryTimeline.Services;
using MemoryTimeline.Views;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline;

/// <summary>
/// Main window for the Memory Timeline application.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly INavigationService _navigationService;

    public MainWindow(MainViewModel viewModel, INavigationService navigationService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _navigationService = navigationService;

        // Set window title bar customization
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        // Initialize navigation service
        _navigationService.Frame = ContentFrame;
        RegisterPages();

        // Navigate to Timeline page by default
        _navigationService.NavigateTo("Timeline");
    }

    private void RegisterPages()
    {
        _navigationService.RegisterPage("Timeline", typeof(TimelinePage));
        _navigationService.RegisterPage("Queue", typeof(QueuePage));
        _navigationService.RegisterPage("Search", typeof(SearchPage));
        _navigationService.RegisterPage("Analytics", typeof(AnalyticsPage));
        _navigationService.RegisterPage("Settings", typeof(SettingsPage));
    }

    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            _navigationService.NavigateTo("Settings");
        }
        else if (args.SelectedItemContainer != null)
        {
            var navItemTag = args.SelectedItemContainer.Tag?.ToString();
            if (!string.IsNullOrEmpty(navItemTag))
            {
                _navigationService.NavigateTo(navItemTag);
            }
        }
    }
}
