using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

        // Navigate to Timeline page by default and select the item
        _navigationService.NavigateTo("Timeline");
        SelectNavigationItem("Timeline");
    }

    private void RegisterPages()
    {
        _navigationService.RegisterPage("Timeline", typeof(TimelinePage));
        _navigationService.RegisterPage("Queue", typeof(QueuePage));
        _navigationService.RegisterPage("Review", typeof(ReviewPage));
        _navigationService.RegisterPage("Connections", typeof(ConnectionsPage));
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

    #region Keyboard Navigation Handlers

    private void NavigateTo(string pageTag)
    {
        _navigationService.NavigateTo(pageTag);
        SelectNavigationItem(pageTag);
    }

    private void SelectNavigationItem(string tag)
    {
        if (tag == "Settings")
        {
            NavigationView.SelectedItem = NavigationView.SettingsItem;
            return;
        }

        foreach (var item in NavigationView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == tag)
            {
                NavigationView.SelectedItem = navItem;
                return;
            }
        }
    }

    private void NavigateToTimeline_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        NavigateTo("Timeline");
        args.Handled = true;
    }

    private void NavigateToQueue_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        NavigateTo("Queue");
        args.Handled = true;
    }

    private void NavigateToReview_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        NavigateTo("Review");
        args.Handled = true;
    }

    private void NavigateToConnections_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        NavigateTo("Connections");
        args.Handled = true;
    }

    private void NavigateToSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        NavigateTo("Search");
        args.Handled = true;
    }

    private void NavigateToAnalytics_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        NavigateTo("Analytics");
        args.Handled = true;
    }

    private void NavigateToSettings_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        NavigateTo("Settings");
        args.Handled = true;
    }

    private void Refresh_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Refresh the current page by re-navigating to it
        if (ContentFrame.CurrentSourcePageType != null)
        {
            ContentFrame.Navigate(ContentFrame.CurrentSourcePageType);
        }
        args.Handled = true;
    }

    private void GoBack_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_navigationService.CanGoBack)
        {
            _navigationService.GoBack();
        }
        args.Handled = true;
    }

    private void GoForward_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ContentFrame.CanGoForward)
        {
            ContentFrame.GoForward();
        }
        args.Handled = true;
    }

    #endregion
}
