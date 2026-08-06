using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MemoryTimeline.Core.Services;
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

        // Use standard title bar to avoid overlap with navigation
        // (ExtendsContentIntoTitleBar causes the drag region to block navigation clicks)

        // Initialize navigation service
        _navigationService.Frame = ContentFrame;
        RegisterPages();

        // Land on Home immediately (no blank frame while settings load), then
        // asynchronously honor home_is_default_page: when the user has turned
        // the Home landing off, flip to Timeline - but only if nothing else
        // (deep link, activation action) has navigated away in the meantime.
        _navigationService.NavigateTo("Home");
        SelectNavigationItem("Home");
        _ = ApplyDefaultLandingPreferenceAsync();
    }

    /// <summary>
    /// Reads home_is_default_page (seeded "true") and, when it is explicitly
    /// "false", moves the initial navigation from Home to Timeline. Runs
    /// fire-and-forget from the constructor: awaits resume on the UI thread
    /// (the constructor runs under the DispatcherQueueSynchronizationContext),
    /// so touching ContentFrame afterwards is safe. Failures leave the app on
    /// Home - a fully functional fallback - and are logged.
    /// </summary>
    private async Task ApplyDefaultLandingPreferenceAsync()
    {
        try
        {
            var settingsService = App.Current.Services.GetRequiredService<ISettingsService>();
            var homeIsDefault = await settingsService.GetSettingAsync<string>(SettingKeys.HomeIsDefaultPage, "true");

            if (string.Equals(homeIsDefault?.Trim(), "false", StringComparison.OrdinalIgnoreCase) &&
                ContentFrame.CurrentSourcePageType == typeof(HomePage))
            {
                NavigateTo("Timeline");
            }
        }
        catch (Exception ex)
        {
            var logger = App.Current.Services.GetRequiredService<ILogger<MainWindow>>();
            logger.LogWarning(ex, "Could not read the default landing page setting; staying on Home");
        }
    }

    private void RegisterPages()
    {
        _navigationService.RegisterPage("Home", typeof(HomePage));
        _navigationService.RegisterPage("Timeline", typeof(TimelinePage));
        _navigationService.RegisterPage("Ask", typeof(AskPage));
        _navigationService.RegisterPage("Queue", typeof(QueuePage));
        _navigationService.RegisterPage("Review", typeof(ReviewPage));
        _navigationService.RegisterPage("People", typeof(ContactsPage));
        _navigationService.RegisterPage("Connections", typeof(ConnectionsPage));
        _navigationService.RegisterPage("Eras", typeof(ErasPage));
        _navigationService.RegisterPage("Map", typeof(MapPage));
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

    private void PasteCapture_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Sync the nav selection FIRST: programmatic selection raises
        // SelectionChanged, which performs a plain parameterless "Queue"
        // navigation. The parameterized navigation below must come last so
        // the "paste" parameter reaches QueuePage.OnNavigatedTo and opens
        // the paste-capture dialog.
        SelectNavigationItem("Queue");
        _navigationService.NavigateTo("Queue", "paste");
        args.Handled = true;
    }

    private void Refresh_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // On the timeline, refresh the data in place: re-navigating the Frame
        // no-ops for the singleton TimelineViewModel (its viewport/state is
        // preserved across navigation), so the user would see nothing happen.
        if (ContentFrame.CurrentSourcePageType == typeof(TimelinePage))
        {
            var timelineViewModel = App.Current.Services.GetRequiredService<TimelineViewModel>();
            if (timelineViewModel.RefreshCommand.CanExecute(null))
            {
                timelineViewModel.RefreshCommand.Execute(null);
            }
        }
        else if (ContentFrame.CurrentSourcePageType != null)
        {
            // Other pages reload their data on navigation; re-navigate to refresh.
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
