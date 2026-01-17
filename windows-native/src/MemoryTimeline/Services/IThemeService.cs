using Microsoft.UI.Xaml;

namespace MemoryTimeline.Services;

/// <summary>
/// Available application themes.
/// </summary>
public enum AppTheme
{
    /// <summary>Follow system theme.</summary>
    System,
    /// <summary>Light theme.</summary>
    Light,
    /// <summary>Dark theme.</summary>
    Dark,
    /// <summary>Solarized Dark theme.</summary>
    SolarizedDark
}

/// <summary>
/// Service interface for managing application theme.
/// </summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    IReadOnlyList<AppTheme> AvailableThemes { get; }
    Task InitializeAsync();
    Task SetThemeAsync(AppTheme theme);
    Task<AppTheme> GetThemeAsync();
    string GetThemeDisplayName(AppTheme theme);
}

/// <summary>
/// Theme service implementation.
/// </summary>
public class ThemeService : IThemeService
{
    private readonly Core.Services.ISettingsService _settingsService;
    private AppTheme _currentTheme = AppTheme.System;
    private ResourceDictionary? _solarizedDictionary;

    public AppTheme CurrentTheme => _currentTheme;

    public IReadOnlyList<AppTheme> AvailableThemes { get; } = new[]
    {
        AppTheme.System,
        AppTheme.Light,
        AppTheme.Dark,
        AppTheme.SolarizedDark
    };

    public ThemeService(Core.Services.ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task InitializeAsync()
    {
        var themeString = await _settingsService.GetThemeAsync();
        _currentTheme = ParseTheme(themeString);
        await ApplyThemeAsync(_currentTheme);
    }

    public async Task SetThemeAsync(AppTheme theme)
    {
        _currentTheme = theme;
        var themeString = theme switch
        {
            AppTheme.Light => "light",
            AppTheme.Dark => "dark",
            AppTheme.SolarizedDark => "solarized-dark",
            _ => "system"
        };

        await _settingsService.SetThemeAsync(themeString);
        await ApplyThemeAsync(theme);
    }

    public async Task<AppTheme> GetThemeAsync()
    {
        var themeString = await _settingsService.GetThemeAsync();
        return ParseTheme(themeString);
    }

    public string GetThemeDisplayName(AppTheme theme) => theme switch
    {
        AppTheme.System => "System Default",
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        AppTheme.SolarizedDark => "Solarized Dark",
        _ => "Unknown"
    };

    private AppTheme ParseTheme(string themeString)
    {
        return themeString?.ToLowerInvariant() switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            "solarized-dark" => AppTheme.SolarizedDark,
            _ => AppTheme.System
        };
    }

    private async Task ApplyThemeAsync(AppTheme theme)
    {
        await Task.CompletedTask; // Make async for potential future use

        if (App.Current is not App app || app.Window is not Window window)
            return;

        if (window.Content is not FrameworkElement rootElement)
            return;

        // Remove Solarized dictionary if switching away from it
        if (theme != AppTheme.SolarizedDark && _solarizedDictionary != null)
        {
            Application.Current.Resources.MergedDictionaries.Remove(_solarizedDictionary);
            _solarizedDictionary = null;
        }

        // Apply base theme
        var elementTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            AppTheme.SolarizedDark => ElementTheme.Dark, // Solarized Dark uses dark base
            _ => ElementTheme.Default
        };

        rootElement.RequestedTheme = elementTheme;

        // Apply Solarized theme overlay
        if (theme == AppTheme.SolarizedDark)
        {
            if (_solarizedDictionary == null)
            {
                _solarizedDictionary = new ResourceDictionary
                {
                    Source = new Uri("ms-appx:///Themes/SolarizedDarkTheme.xaml")
                };
            }

            if (!Application.Current.Resources.MergedDictionaries.Contains(_solarizedDictionary))
            {
                Application.Current.Resources.MergedDictionaries.Add(_solarizedDictionary);
            }
        }
    }
}
