using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Services;
using System.Reflection;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for settings page.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly IExportService _exportService;
    private readonly IImportService _importService;
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

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private int _exportProgress;

    [ObservableProperty]
    private string _exportStatusMessage = string.Empty;

    [ObservableProperty]
    private string _importStatusMessage = string.Empty;

    // About information
    public string AppName => "Memory Timeline";
    public string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    public string BuildDate => File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd");

    // Available options
    public List<string> ThemeOptions { get; } = new() { "System", "Light", "Dark", "Solarized Dark" };
    public List<string> ZoomLevelOptions { get; } = new() { "Year", "Month", "Week", "Day" };
    public List<string> LlmProviderOptions { get; } = new() { "Anthropic", "OpenAI", "Local" };

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        IExportService exportService,
        IImportService importService,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _exportService = exportService;
        _importService = importService;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the settings view.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            // Load current settings - convert stored theme to display format
            var storedTheme = await _settingsService.GetThemeAsync();
            SelectedTheme = storedTheme?.ToLowerInvariant() switch
            {
                "light" => "Light",
                "dark" => "Dark",
                "solarized-dark" => "Solarized Dark",
                _ => "System"
            };
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

            // Save theme - convert display format to storage format
            var themeToStore = SelectedTheme switch
            {
                "Light" => "light",
                "Dark" => "dark",
                "Solarized Dark" => "solarized-dark",
                _ => "system"
            };
            await _settingsService.SetThemeAsync(themeToStore);
            await _themeService.SetThemeAsync(SelectedTheme switch
            {
                "Light" => AppTheme.Light,
                "Dark" => AppTheme.Dark,
                "Solarized Dark" => AppTheme.SolarizedDark,
                _ => AppTheme.System
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
            SelectedTheme = "Dark";
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

    #region Export/Import Commands

    [RelayCommand]
    private async Task ExportToJsonAsync()
    {
        try
        {
            IsExporting = true;
            ExportStatusMessage = "Selecting export location...";

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"MemoryTimeline_Export_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            savePicker.FileTypeChoices.Add("JSON File", new List<string> { ".json" });

            // Get the main window handle for WinUI 3
            var hwnd = WindowNative.GetWindowHandle(App.Current.Window);
            InitializeWithWindow.Initialize(savePicker, hwnd);

            var file = await savePicker.PickSaveFileAsync();
            if (file == null)
            {
                ExportStatusMessage = "Export cancelled";
                return;
            }

            ExportStatusMessage = "Exporting to JSON...";
            var progress = new Progress<int>(p => ExportProgress = p);

            await _exportService.ExportToJsonAsync(file.Path, progress: progress);

            ExportStatusMessage = $"Export complete: {file.Path}";
            _logger.LogInformation("Exported timeline to JSON: {Path}", file.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to JSON");
            ExportStatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task ExportToCsvAsync()
    {
        try
        {
            IsExporting = true;
            ExportStatusMessage = "Selecting export location...";

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"MemoryTimeline_Export_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            savePicker.FileTypeChoices.Add("CSV File", new List<string> { ".csv" });

            var hwnd = WindowNative.GetWindowHandle(App.Current.Window);
            InitializeWithWindow.Initialize(savePicker, hwnd);

            var file = await savePicker.PickSaveFileAsync();
            if (file == null)
            {
                ExportStatusMessage = "Export cancelled";
                return;
            }

            ExportStatusMessage = "Exporting to CSV...";
            var progress = new Progress<int>(p => ExportProgress = p);

            await _exportService.ExportToCsvAsync(file.Path, progress: progress);

            ExportStatusMessage = $"Export complete: {file.Path}";
            _logger.LogInformation("Exported timeline to CSV: {Path}", file.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to CSV");
            ExportStatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task ExportToMarkdownAsync()
    {
        try
        {
            IsExporting = true;
            ExportStatusMessage = "Selecting export location...";

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"MemoryTimeline_Export_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            savePicker.FileTypeChoices.Add("Markdown File", new List<string> { ".md" });

            var hwnd = WindowNative.GetWindowHandle(App.Current.Window);
            InitializeWithWindow.Initialize(savePicker, hwnd);

            var file = await savePicker.PickSaveFileAsync();
            if (file == null)
            {
                ExportStatusMessage = "Export cancelled";
                return;
            }

            ExportStatusMessage = "Exporting to Markdown...";
            var progress = new Progress<int>(p => ExportProgress = p);

            await _exportService.ExportToMarkdownAsync(file.Path, progress: progress);

            ExportStatusMessage = $"Export complete: {file.Path}";
            _logger.LogInformation("Exported timeline to Markdown: {Path}", file.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to Markdown");
            ExportStatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task ImportFromJsonAsync()
    {
        try
        {
            IsImporting = true;
            ImportStatusMessage = "Selecting file to import...";

            var openPicker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            openPicker.FileTypeFilter.Add(".json");

            var hwnd = WindowNative.GetWindowHandle(App.Current.Window);
            InitializeWithWindow.Initialize(openPicker, hwnd);

            var file = await openPicker.PickSingleFileAsync();
            if (file == null)
            {
                ImportStatusMessage = "Import cancelled";
                return;
            }

            ImportStatusMessage = "Validating import file...";
            var validation = await _importService.ValidateImportFileAsync(file.Path);

            if (!validation.IsValid)
            {
                ImportStatusMessage = $"Invalid file: {string.Join(", ", validation.Issues)}";
                return;
            }

            ImportStatusMessage = $"Importing {validation.EventCount} events...";
            var progress = new Progress<(int, string)>(p => ImportStatusMessage = p.Item2);

            var options = new ImportOptions
            {
                SkipDuplicates = true,
                UpdateExisting = false,
                CreateBackup = true
            };

            var result = await _importService.ImportFromJsonAsync(file.Path, options, progress);

            if (result.Success)
            {
                ImportStatusMessage = $"Import complete: {result.EventsImported} imported, {result.EventsSkipped} skipped";
                _logger.LogInformation("Imported {Count} events from JSON", result.EventsImported);
            }
            else
            {
                ImportStatusMessage = $"Import failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing from JSON");
            ImportStatusMessage = $"Import error: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    #endregion

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
