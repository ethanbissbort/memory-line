using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using System.Reflection;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for settings page.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private string _selectedTheme = "System";

    [ObservableProperty]
    private string _selectedZoomLevel = "Month";

    [ObservableProperty]
    private string _llmProvider = "Anthropic";

    [ObservableProperty]
    private string _llmModel = "claude-3-5-sonnet-20241022";

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private int _audioSampleRate = 16000;

    [ObservableProperty]
    private int _audioBitsPerSample = 16;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    // About information
    public string AppName => "Memory Timeline";
    public string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    public string BuildDate => File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd");

    // Available options
    public List<string> ThemeOptions { get; } = new() { "Light", "Dark", "System" };
    public List<string> ZoomLevelOptions { get; } = new() { "Year", "Month", "Week", "Day" };
    public List<string> LlmProviderOptions { get; } = new() { "Anthropic", "OpenAI", "Local" };

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the settings view.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            // Load current settings
            SelectedTheme = await _settingsService.GetThemeAsync();
            SelectedZoomLevel = await _settingsService.GetDefaultZoomLevelAsync();
            LlmProvider = await _settingsService.GetLlmProviderAsync();
            LlmModel = await _settingsService.GetLlmModelAsync();

            // Load audio settings
            var sampleRate = await _settingsService.GetSettingAsync<int>("AudioSampleRate", 16000);
            var bitsPerSample = await _settingsService.GetSettingAsync<int>("AudioBitsPerSample", 16);
            AudioSampleRate = sampleRate;
            AudioBitsPerSample = bitsPerSample;

            // Load API key (masked)
            var apiKey = await _settingsService.GetSettingAsync<string>("ApiKey", string.Empty);
            ApiKey = !string.IsNullOrEmpty(apiKey) ? "••••••••" : string.Empty;

            StatusMessage = "Settings loaded";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings");
            StatusMessage = "Error loading settings";
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (IsSaving) return;

        try
        {
            IsSaving = true;
            StatusMessage = "Saving settings...";

            // Save theme
            await _settingsService.SetThemeAsync(SelectedTheme);
            await _themeService.SetThemeAsync(SelectedTheme switch
            {
                "Light" => Services.AppTheme.Light,
                "Dark" => Services.AppTheme.Dark,
                _ => Services.AppTheme.System
            });

            // Save zoom level
            await _settingsService.SetSettingAsync("DefaultZoomLevel", SelectedZoomLevel);

            // Save LLM settings
            await _settingsService.SetSettingAsync("LlmProvider", LlmProvider);
            await _settingsService.SetSettingAsync("LlmModel", LlmModel);

            // Save audio settings
            await _settingsService.SetSettingAsync("AudioSampleRate", AudioSampleRate);
            await _settingsService.SetSettingAsync("AudioBitsPerSample", AudioBitsPerSample);

            // Save API key (only if changed)
            if (!string.IsNullOrEmpty(ApiKey) && ApiKey != "••••••••")
            {
                await _settingsService.SetSettingAsync("ApiKey", ApiKey);
            }

            StatusMessage = "Settings saved successfully";
            _logger.LogInformation("Settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
            StatusMessage = "Error saving settings";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        try
        {
            // Reset to defaults
            SelectedTheme = "System";
            SelectedZoomLevel = "Month";
            LlmProvider = "Anthropic";
            LlmModel = "claude-3-5-sonnet-20241022";
            AudioSampleRate = 16000;
            AudioBitsPerSample = 16;
            ApiKey = string.Empty;

            await SaveSettingsAsync();
            StatusMessage = "Settings reset to defaults";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting settings");
            StatusMessage = "Error resetting settings";
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        try
        {
            // TODO: Implement cache clearing logic
            StatusMessage = "Cache cleared (placeholder)";
            _logger.LogInformation("Cache cleared");
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
            StatusMessage = "Error clearing cache";
        }
    }

    partial void OnSelectedThemeChanged(string value)
    {
        StatusMessage = $"Theme changed to {value}";
    }

    partial void OnLlmProviderChanged(string value)
    {
        // Update model options based on provider
        if (value == "OpenAI")
        {
            LlmModel = "gpt-4-turbo-preview";
        }
        else if (value == "Anthropic")
        {
            LlmModel = "claude-3-5-sonnet-20241022";
        }
    }
}
