using Microsoft.UI.Xaml.Controls;

namespace MemoryTimeline.Services;

/// <summary>
/// Service interface for application navigation.
/// </summary>
public interface INavigationService
{
    Frame? Frame { get; set; }
    bool CanGoBack { get; }
    void NavigateTo(string pageKey, object? parameter = null);
    void GoBack();
    void RegisterPage(string key, Type pageType);
}

/// <summary>
/// Navigation service implementation.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly Dictionary<string, Type> _pages = new();
    private Frame? _frame;

    public Frame? Frame
    {
        get => _frame;
        set
        {
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

    public void NavigateTo(string pageKey, object? parameter = null)
    {
        if (Frame == null)
            return;

        if (_pages.TryGetValue(pageKey, out var pageType))
        {
            Frame.Navigate(pageType, parameter);
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
