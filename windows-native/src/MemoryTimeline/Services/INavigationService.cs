using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace MemoryTimeline.Services;

/// <summary>
/// Service interface for application navigation.
/// </summary>
public interface INavigationService
{
    Frame? Frame { get; set; }
    bool CanGoBack { get; }
    void NavigateTo(string pageKey);
    void NavigateTo(string pageKey, object? parameter);
    void GoBack();
    void RegisterPage(string key, Type pageType);
}

/// <summary>
/// Navigation service implementation.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly Dictionary<string, Type> _pages = new();
    private readonly ILogger<NavigationService>? _logger;
    private Frame? _frame;

    public NavigationService(ILogger<NavigationService>? logger = null)
    {
        _logger = logger;
    }

    public Frame? Frame
    {
        get => _frame;
        set
        {
            if (ReferenceEquals(_frame, value))
            {
                return;
            }

            // Detach from the previous frame to avoid leaking the handler / handling
            // navigation events for a frame we no longer own.
            if (_frame != null)
            {
                _frame.Navigated -= OnNavigated;
            }

            _frame = value;

            if (_frame != null)
            {
                _frame.Navigated += OnNavigated;
            }
        }
    }

    public bool CanGoBack => Frame?.CanGoBack ?? false;

    public void RegisterPage(string key, Type pageType)
    {
        if (!_pages.ContainsKey(key))
        {
            _pages.Add(key, pageType);
        }
    }

    public void NavigateTo(string pageKey)
    {
        NavigateTo(pageKey, null);
    }

    public void NavigateTo(string pageKey, object? parameter)
    {
        if (Frame == null)
        {
            _logger?.LogWarning("Navigation to '{PageKey}' requested before a Frame was attached", pageKey);
            return;
        }

        if (_pages.TryGetValue(pageKey, out var pageType))
        {
            Frame.Navigate(pageType, parameter);
        }
        else
        {
            _logger?.LogWarning("Navigation requested for unknown page key '{PageKey}'; no page is registered under that key", pageKey);
        }
    }

    public void GoBack()
    {
        if (CanGoBack)
        {
            Frame?.GoBack();
        }
    }

    private void OnNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        // Handle navigation events if needed
    }
}
