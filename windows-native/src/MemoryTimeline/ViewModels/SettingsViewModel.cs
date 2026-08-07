using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Services;
using MemoryTimeline.Sync;
using MemoryTimeline.SyncContracts;
using System.Reflection;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for settings page.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly IExportService _exportService;
    private readonly IImportService _importService;
    private readonly ISyncClient _syncClient;
    private readonly ISyncSettingsStore _syncSettingsStore;
    private readonly ISyncBackgroundWorker _syncBackgroundWorker;
    private readonly ILogger<SettingsViewModel> _logger;

    // Captured on construction (UI thread) so sync worker events arriving on
    // background threads can marshal UI mutations back onto the dispatcher.
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _disposed;

    // True while persisted sync values are being loaded into the observable
    // properties, so the persist-on-change handlers do not write them back.
    private bool _suppressSyncPersistence;

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
    private string _embeddingApiKey = string.Empty;

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
    // Only Anthropic is implemented (AnthropicLlmService is the sole ILlmService).
    // Do not list unimplemented providers here: selecting one used to persist an
    // unsupported provider/model and break all event extraction.
    public List<string> LlmProviderOptions { get; } = new() { "Anthropic" };

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        IExportService exportService,
        IImportService importService,
        ISyncClient syncClient,
        ISyncSettingsStore syncSettingsStore,
        ISyncBackgroundWorker syncBackgroundWorker,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _exportService = exportService;
        _importService = importService;
        _syncClient = syncClient;
        _syncSettingsStore = syncSettingsStore;
        _syncBackgroundWorker = syncBackgroundWorker;
        _logger = logger;

        // Capture the UI dispatcher while we are still on the UI thread.
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // NOTE: the sync worker is a DI singleton while this ViewModel is transient;
        // SettingsPage disposes the VM on navigation away so this subscription does
        // not root the ViewModel for the app lifetime.
        _syncBackgroundWorker.SyncStatusChanged += OnSyncStatusChanged;
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
            // Stored values are canonical lowercase ("month", "anthropic");
            // normalize to the Title-case display values used by the ComboBoxes
            // so the selection is not blank on load.
            var storedZoomLevel = await _settingsService.GetDefaultZoomLevelAsync();
            SelectedZoomLevel = ToDisplayOption(storedZoomLevel, ZoomLevelOptions, "Month");

            var storedProvider = await _settingsService.GetLlmProviderAsync();
            LlmProvider = ToDisplayOption(storedProvider, LlmProviderOptions, "Anthropic");

            LlmModel = await _settingsService.GetLlmModelAsync();

            // Load audio settings
            var sampleRate = await _settingsService.GetSettingAsync<int>("AudioSampleRate", 16000);
            var bitsPerSample = await _settingsService.GetSettingAsync<int>("AudioBitsPerSample", 16);
            AudioSampleRate = sampleRate;
            AudioBitsPerSample = bitsPerSample;

            // Load API key (masked)
            var apiKey = await _settingsService.GetSettingAsync<string>(SettingKeys.AnthropicApiKey, string.Empty);
            ApiKey = !string.IsNullOrEmpty(apiKey) ? "••••••••" : string.Empty;

            // Load embedding API key (masked)
            var embeddingApiKey = await _settingsService.GetSettingAsync<string>(SettingKeys.EmbeddingApiKey, string.Empty);
            EmbeddingApiKey = !string.IsNullOrEmpty(embeddingApiKey) ? "••••••••" : string.Empty;

            // Load sync configuration and pairing state
            await LoadSyncSettingsAsync();

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

            // Save zoom level (canonical lowercase to match readers/seeds, e.g. "month")
            await _settingsService.SetSettingAsync(SettingKeys.DefaultZoomLevel, SelectedZoomLevel.ToLowerInvariant());

            // Save LLM settings (canonical lowercase provider, e.g. "anthropic")
            await _settingsService.SetSettingAsync(SettingKeys.LlmProvider, LlmProvider.ToLowerInvariant());
            await _settingsService.SetSettingAsync(SettingKeys.LlmModel, LlmModel);

            // Save audio settings
            await _settingsService.SetSettingAsync("AudioSampleRate", AudioSampleRate);
            await _settingsService.SetSettingAsync("AudioBitsPerSample", AudioBitsPerSample);

            // Save API key (only if changed)
            if (!string.IsNullOrEmpty(ApiKey) && ApiKey != "••••••••")
            {
                await _settingsService.SetSettingAsync(SettingKeys.AnthropicApiKey, ApiKey);
            }

            // Save embedding API key (only if changed)
            if (!string.IsNullOrEmpty(EmbeddingApiKey) && EmbeddingApiKey != "••••••••")
            {
                await _settingsService.SetSettingAsync(SettingKeys.EmbeddingApiKey, EmbeddingApiKey);
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
            EmbeddingApiKey = string.Empty;

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

    #region Sync

    [ObservableProperty]
    private bool _syncEnabled;

    [ObservableProperty]
    private string _syncServerUrl = string.Empty;

    // One-time secret exchanged for device tokens during pairing. NEVER persisted
    // and never logged; cleared as soon as pairing succeeds.
    [ObservableProperty]
    private string _syncPairingCode = string.Empty;

    [ObservableProperty]
    private bool _syncAutoProcess;

    /// <summary>Derived pairing state: true when a device identity is stored.</summary>
    [ObservableProperty]
    private bool _isSyncPaired;

    [ObservableProperty]
    private string _syncDeviceDisplayName = string.Empty;

    [ObservableProperty]
    private string _syncStatusText = "Not paired";

    /// <summary>
    /// Loads persisted sync configuration and pairing state. Persist-on-change is
    /// suppressed while loading so reads never write values back. Exceptions bubble
    /// to <see cref="InitializeAsync"/>, which logs and surfaces the failure.
    /// </summary>
    private async Task LoadSyncSettingsAsync()
    {
        _suppressSyncPersistence = true;
        try
        {
            SyncEnabled = await _settingsService.GetSettingAsync<bool>(SettingKeys.SyncEnabled, false);
            SyncServerUrl = await _settingsService.GetSettingAsync<string>(SettingKeys.SyncServerUrl, string.Empty) ?? string.Empty;
            SyncAutoProcess = await _settingsService.GetSettingAsync<bool>(SettingKeys.SyncAutoProcess, false);
            SyncDeviceDisplayName = await _settingsService.GetSettingAsync<string>(SettingKeys.SyncDeviceDisplayName, string.Empty) ?? string.Empty;

            var deviceId = await _syncSettingsStore.GetDeviceIdAsync();
            IsSyncPaired = !string.IsNullOrEmpty(deviceId);
            SyncStatusText = !IsSyncPaired
                ? "Not paired"
                : string.IsNullOrEmpty(SyncDeviceDisplayName)
                    ? "Paired"
                    : $"Paired as {SyncDeviceDisplayName}";
        }
        finally
        {
            _suppressSyncPersistence = false;
        }
    }

    /// <summary>
    /// Registers this machine with the sync service using the entered server URL and
    /// one-time pairing code, then persists the returned identity and tokens.
    /// </summary>
    [RelayCommand]
    private async Task PairDeviceAsync()
    {
        var serverUrl = SyncServerUrl.Trim();
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri) ||
            (serverUri.Scheme != Uri.UriSchemeHttps && serverUri.Scheme != Uri.UriSchemeHttp))
        {
            SyncStatusText = "Enter a valid server URL (http:// or https://)";
            StatusMessage = "Sync pairing failed: invalid server URL";
            return;
        }

        if (string.IsNullOrWhiteSpace(SyncPairingCode))
        {
            SyncStatusText = "Enter the pairing code shown by the sync server";
            StatusMessage = "Sync pairing failed: missing pairing code";
            return;
        }

        try
        {
            SyncStatusText = "Pairing...";

            var registration = await _syncClient.RegisterDeviceAsync(serverUrl, new DeviceRegisterRequest
            {
                PairingCode = SyncPairingCode.Trim(),
                Platform = "windows",
                DisplayName = Environment.MachineName,
                AppVersion = AppVersion
            });

            await _syncSettingsStore.SaveRegistrationAsync(serverUrl, registration);
            await _settingsService.SetSettingAsync(SettingKeys.SyncDeviceDisplayName, Environment.MachineName);

            // The pairing code is single-use; drop it from the UI immediately.
            SyncPairingCode = string.Empty;
            SyncServerUrl = serverUrl;
            SyncDeviceDisplayName = Environment.MachineName;
            IsSyncPaired = true;
            SyncStatusText = $"Paired as {Environment.MachineName}";
            StatusMessage = "Sync device paired";
            _logger.LogInformation("Sync device paired successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync device pairing failed");
            SyncStatusText = $"Pairing failed: {ex.Message}";
            StatusMessage = "Sync pairing failed";
        }
    }

    /// <summary>
    /// Revokes this device on the server (best-effort — the server may be
    /// unreachable) and always clears the local registration.
    /// </summary>
    [RelayCommand]
    private async Task UnpairDeviceAsync()
    {
        try
        {
            await _syncClient.RevokeDeviceAsync();
        }
        catch (Exception ex)
        {
            // Best-effort: an unreachable/already-revoked server must not block a
            // local unpair. Logged and surfaced below via the status line.
            _logger.LogWarning(ex, "Sync device revoke failed; clearing local registration anyway");
        }

        try
        {
            await _syncSettingsStore.ClearRegistrationAsync();
            IsSyncPaired = false;
            SyncDeviceDisplayName = string.Empty;
            SyncStatusText = "Not paired";
            StatusMessage = "Sync device unpaired";
            _logger.LogInformation("Sync device unpaired");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Clearing sync registration failed");
            SyncStatusText = $"Unpair failed: {ex.Message}";
            StatusMessage = "Sync unpair failed";
        }
    }

    /// <summary>Runs one sync cycle immediately and surfaces the outcome.</summary>
    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (!IsSyncPaired)
        {
            SyncStatusText = "Pair this device before syncing";
            return;
        }

        try
        {
            SyncStatusText = "Syncing...";
            var result = await _syncBackgroundWorker.SyncNowAsync();

            if (!string.IsNullOrEmpty(result.Error))
            {
                SyncStatusText = $"Sync failed: {result.Error}";
            }
            else if (!result.Ran)
            {
                SyncStatusText = "Sync did not run — enable sync and pair this device first";
            }
            else
            {
                SyncStatusText = $"Sync complete: {result.ChangesApplied} applied, {result.OutboxPublished} published";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            SyncStatusText = $"Sync failed: {ex.Message}";
        }
    }

    partial void OnSyncEnabledChanged(bool value)
    {
        if (_suppressSyncPersistence) return;
        _ = PersistSyncSettingAsync(SettingKeys.SyncEnabled, value);
    }

    partial void OnSyncServerUrlChanged(string value)
    {
        if (_suppressSyncPersistence) return;
        _ = PersistSyncSettingAsync(SettingKeys.SyncServerUrl, value);
    }

    partial void OnSyncAutoProcessChanged(bool value)
    {
        if (_suppressSyncPersistence) return;
        _ = PersistSyncSettingAsync(SettingKeys.SyncAutoProcess, value);
    }

    /// <summary>
    /// Persists one sync setting immediately (sync settings apply on change, not on
    /// the page's Save button). Started fire-and-forget from property-changed
    /// handlers on the UI thread; failures are logged and surfaced in the status line.
    /// </summary>
    private async Task PersistSyncSettingAsync<T>(string key, T value)
    {
        try
        {
            await _settingsService.SetSettingAsync(key, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving sync setting {Key}", key);
            SyncStatusText = "Error saving sync settings";
        }
    }

    private void OnSyncStatusChanged(object? sender, SyncStatusChangedEventArgs e)
    {
        RunOnUi(() => SyncStatusText = e.StatusText);
    }

    /// <summary>
    /// Marshals the supplied action onto the UI dispatcher thread. Sync worker
    /// events arrive on background threads; mutating bound state off the UI thread
    /// corrupts/crashes the WinUI binding layer.
    /// </summary>
    private void RunOnUi(Action action)
    {
        if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
    }

    /// <summary>
    /// Releases the subscription to the singleton sync worker's status event so a
    /// navigated-away Settings page is not rooted for the app lifetime.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _syncBackgroundWorker.SyncStatusChanged -= OnSyncStatusChanged;
    }

    #endregion

    /// <summary>
    /// Maps a canonical (lowercase) stored value to the matching Title-case
    /// display option so ComboBox selection is never blank on load.
    /// A stored value that is not a supported option normalizes to the default
    /// (e.g. a legacy "openai"/"local" llm_provider becomes "Anthropic"), so an
    /// unsupported value can never round-trip back into settings on save.
    /// </summary>
    private static string ToDisplayOption(string? storedValue, List<string> options, string defaultOption)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return defaultOption;
        }

        var match = options.FirstOrDefault(o =>
            string.Equals(o, storedValue, StringComparison.OrdinalIgnoreCase));

        return match ?? defaultOption;
    }

    partial void OnSelectedThemeChanged(string value)
    {
        StatusMessage = $"Theme changed to {value}";
    }

}
