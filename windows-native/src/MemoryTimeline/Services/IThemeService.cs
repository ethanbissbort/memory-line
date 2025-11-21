using Microsoft.UI.Xaml;

namespace MemoryTimeline.Services;

/// <summary>
/// Service interface for managing application theme.
/// </summary>
public interface IThemeService
{
    ElementTheme CurrentTheme { get; }
    Task InitializeAsync();
    Task SetThemeAsync(ElementTheme theme);
    Task<ElementTheme> GetThemeAsync();
}

/// <summary>
/// Theme service implementation.
/// </summary>
public class ThemeService : IThemeService
{
    private readonly Core.Services.ISettingsService _settingsService;
    private ElementTheme _currentTheme = ElementTheme.Default;

    public ElementTheme CurrentTheme => _currentTheme;

    public ThemeService(Core.Services.ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task InitializeAsync()
    {
        var themeString = await _settingsService.GetThemeAsync();
        _currentTheme = ParseTheme(themeString);
    }

    public async Task SetThemeAsync(ElementTheme theme)
    {
        _currentTheme = theme;
        var themeString = theme switch
        {
            ElementTheme.Light => "light",
            ElementTheme.Dark => "dark",
            _ => "default"
        };

        await _settingsService.SetThemeAsync(themeString);

        // Apply theme to current window
        if (App.Current is App app && app.Window is Window window)
        {
            if (window.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = theme;
            }
        }
    }

    public async Task<ElementTheme> GetThemeAsync()
    {
        var themeString = await _settingsService.GetThemeAsync();
        return ParseTheme(themeString);
    }

    private ElementTheme ParseTheme(string themeString)
    {
        return themeString.ToLowerInvariant() switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }
}
