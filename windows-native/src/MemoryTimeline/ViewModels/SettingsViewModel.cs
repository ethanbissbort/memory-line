using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly IEventService _eventService;
    private readonly ILlmUsageTracker _llmUsageTracker;
    private readonly IBackupService _backupService;
    private readonly IMediaService _mediaService;
    private readonly ILogger<SettingsViewModel> _logger;

    // Embedding provider persisted at load time; a differing selection shows
    // the re-embed warning until "Re-embed all events" completes.
    private string _loadedEmbeddingProviderKey = EmbeddingProviderKeys.Local;
    private CancellationTokenSource? _reEmbedCts;

    // Suppresses persist-on-change while InitializeAsync loads stored values.
    private bool _isLoadingBackupSettings;

    [ObservableProperty]
    private string _selectedTheme = "System";

    [ObservableProperty]
    private string _selectedZoomLevel = "Month";

    [ObservableProperty]
    private string _llmProvider = "Anthropic";

    [ObservableProperty]
    private string _llmModel = "claude-3-5-sonnet-20241022";

    [ObservableProperty]
    private string _llmBaseUrl = string.Empty;

    [ObservableProperty]
    private string _selectedEmbeddingProvider = "Local (on-device)";

    [ObservableProperty]
    private bool _showReEmbedWarning;

    [ObservableProperty]
    private bool _isReEmbedding;

    [ObservableProperty]
    private string _reEmbedStatusMessage = string.Empty;

    [ObservableProperty]
    private string _llmUsageSummary = "No LLM calls this session";

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

    // ---- Backup & restore (F12, Data Management card) ----

    [ObservableProperty]
    private string _backupDestination = string.Empty;

    [ObservableProperty]
    private string _selectedBackupSchedule = "Off";

    [ObservableProperty]
    private bool _includeMediaInBackup = true;

    // Session-only by design: the only persisted include toggle is
    // backup_include_media (which also governs audio for SCHEDULED backups);
    // this checkbox refines a manual "Back Up Now" run.
    [ObservableProperty]
    private bool _includeAudioInBackup = true;

    [ObservableProperty]
    private string _lastBackupDisplay = "Last backup: never";

    [ObservableProperty]
    private bool _isBackingUp;

    [ObservableProperty]
    private int _backupProgress;

    [ObservableProperty]
    private string _backupStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isRestoring;

    public List<string> BackupScheduleOptions { get; } = new() { "Off", "Daily", "Weekly" };

    // About information
    public string AppName => "Memory Timeline";
    public string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    public string BuildDate => File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd");

    // Available options
    public List<string> ThemeOptions { get; } = new() { "System", "Light", "Dark", "Solarized Dark" };
    public List<string> ZoomLevelOptions { get; } = new() { "Year", "Month", "Week", "Day" };
    // Display values lowercase to the canonical stored keys on save
    // ("anthropic" / "openai-compatible" — see LlmProviderKeys). Only list
    // providers RoutingLlmService actually implements: persisting an
    // unsupported provider used to break all event extraction.
    public List<string> LlmProviderOptions { get; } = new() { "Anthropic", "OpenAI-compatible" };
    public List<string> EmbeddingProviderOptions { get; } = new() { "Local (on-device)", "OpenAI (cloud)" };

    /// <summary>
    /// Plain-language privacy summary: exactly what each provider sends where.
    /// </summary>
    public string PrivacySummary =>
        "Privacy: Anthropic (LLM) receives your transcripts and event text for extraction and narration. " +
        "OpenAI (embeddings) receives event titles and descriptions to compute similarity vectors. " +
        "Local (on-device) embeddings send nothing — no text leaves this machine. " +
        "An OpenAI-compatible endpoint sends text to whatever server the base URL points at (e.g. Ollama on localhost stays local).";

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        IExportService exportService,
        IImportService importService,
        IEventService eventService,
        ILlmUsageTracker llmUsageTracker,
        IBackupService backupService,
        IMediaService mediaService,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _exportService = exportService;
        _importService = importService;
        _eventService = eventService;
        _llmUsageTracker = llmUsageTracker;
        _backupService = backupService;
        _mediaService = mediaService;
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
            // Stored values are canonical lowercase ("month", "anthropic");
            // normalize to the Title-case display values used by the ComboBoxes
            // so the selection is not blank on load.
            var storedZoomLevel = await _settingsService.GetDefaultZoomLevelAsync();
            SelectedZoomLevel = ToDisplayOption(storedZoomLevel, ZoomLevelOptions, "Month");

            // Normalize aliases ("ollama", "openai_compatible") through the
            // canonical keys before mapping to a display option.
            var storedProvider = LlmProviderKeys.Normalize(await _settingsService.GetLlmProviderAsync());
            LlmProvider = storedProvider == LlmProviderKeys.OpenAiCompatible
                ? "OpenAI-compatible"
                : "Anthropic";

            LlmModel = await _settingsService.GetLlmModelAsync();
            LlmBaseUrl = await _settingsService.GetSettingAsync<string>(SettingKeys.LlmBaseUrl, string.Empty) ?? string.Empty;

            // Embedding provider ('local' is the seeded default; unknown stored
            // values normalize to it)
            _loadedEmbeddingProviderKey = EmbeddingProviderKeys.Normalize(
                await _settingsService.GetSettingAsync<string>(SettingKeys.EmbeddingProvider, EmbeddingProviderKeys.Local));
            SelectedEmbeddingProvider = _loadedEmbeddingProviderKey == EmbeddingProviderKeys.OpenAi
                ? "OpenAI (cloud)"
                : "Local (on-device)";
            ShowReEmbedWarning = false;

            UpdateLlmUsageSummary();

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

            // Load backup settings (F12). The loading flag suppresses the
            // persist-on-change handlers while stored values stream in.
            _isLoadingBackupSettings = true;
            try
            {
                BackupDestination = await _settingsService.GetSettingAsync<string>(
                    SettingKeys.BackupDestination, string.Empty) ?? string.Empty;

                var storedSchedule = await _settingsService.GetSettingAsync<string>(
                    SettingKeys.BackupSchedule, "off");
                SelectedBackupSchedule = ToDisplayOption(storedSchedule, BackupScheduleOptions, "Off");

                var includeMedia = await _settingsService.GetSettingAsync<string>(
                    SettingKeys.BackupIncludeMedia, "true");
                IncludeMediaInBackup = !string.Equals(includeMedia, "false", StringComparison.OrdinalIgnoreCase);

                await RefreshLastBackupDisplayAsync();
            }
            finally
            {
                _isLoadingBackupSettings = false;
            }

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

            // Save LLM settings (canonical lowercase provider keys:
            // "anthropic" / "openai-compatible" — see LlmProviderKeys)
            await _settingsService.SetSettingAsync(SettingKeys.LlmProvider, LlmProvider.ToLowerInvariant());
            await _settingsService.SetSettingAsync(SettingKeys.LlmModel, LlmModel);
            await _settingsService.SetSettingAsync(SettingKeys.LlmBaseUrl, LlmBaseUrl?.Trim() ?? string.Empty);

            // Save embedding provider (canonical "local" / "openai"). The
            // re-embed warning intentionally stays visible after saving: the
            // stored embeddings are still from the previous provider until
            // "Re-embed all events" runs.
            await _settingsService.SetSettingAsync(SettingKeys.EmbeddingProvider, ToEmbeddingProviderKey(SelectedEmbeddingProvider));

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
            UpdateLlmUsageSummary();
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
            LlmBaseUrl = string.Empty;
            SelectedEmbeddingProvider = "Local (on-device)";
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

    /// <summary>
    /// Regenerates the embeddings of ALL events with the currently selected
    /// provider (persisting the selection first so the router uses it). The
    /// heavy lifting lives in Core (IEventService.ReEmbedAllAsync), which
    /// deletes stale-dimension rows up front and reports (done, total).
    /// </summary>
    [RelayCommand]
    private async Task ReEmbedAllAsync()
    {
        if (IsReEmbedding) return;

        try
        {
            IsReEmbedding = true;
            _reEmbedCts = new CancellationTokenSource();
            ReEmbedStatusMessage = "Preparing to re-embed...";

            // Persist the selected embedding provider so the routing service
            // generates the new vectors with it (settings apply live).
            await _settingsService.SetSettingAsync(
                SettingKeys.EmbeddingProvider, ToEmbeddingProviderKey(SelectedEmbeddingProvider));
            _loadedEmbeddingProviderKey = ToEmbeddingProviderKey(SelectedEmbeddingProvider);

            // Progress<T> marshals back to this (UI) synchronization context.
            var progress = new Progress<(int done, int total)>(p =>
                ReEmbedStatusMessage = $"Re-embedding events... {p.done}/{p.total}");

            var count = await _eventService.ReEmbedAllAsync(progress, _reEmbedCts.Token);

            ReEmbedStatusMessage = $"Re-embedded {count} event(s) with the current provider";
            StatusMessage = ReEmbedStatusMessage;
            ShowReEmbedWarning = false;
            _logger.LogInformation("Re-embedded {Count} events from Settings", count);
        }
        catch (OperationCanceledException)
        {
            ReEmbedStatusMessage = "Re-embed cancelled — run it again to restore similarity features";
            _logger.LogInformation("Re-embed cancelled from Settings");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error re-embedding events");
            ReEmbedStatusMessage = $"Re-embed failed: {ex.Message}";
            StatusMessage = "Error re-embedding events";
        }
        finally
        {
            IsReEmbedding = false;
            _reEmbedCts?.Dispose();
            _reEmbedCts = null;
        }
    }

    [RelayCommand]
    private void CancelReEmbed()
    {
        _reEmbedCts?.Cancel();
    }

    /// <summary>
    /// Shows the re-embed warning as soon as the selection differs from the
    /// provider whose embeddings are (or were) stored.
    /// </summary>
    partial void OnSelectedEmbeddingProviderChanged(string value)
    {
        ShowReEmbedWarning = ToEmbeddingProviderKey(value) != _loadedEmbeddingProviderKey;
    }

    /// <summary>Maps the display option to the canonical stored key.</summary>
    private static string ToEmbeddingProviderKey(string? displayOption)
    {
        return displayOption != null && displayOption.StartsWith("OpenAI", StringComparison.OrdinalIgnoreCase)
            ? EmbeddingProviderKeys.OpenAi
            : EmbeddingProviderKeys.Local;
    }

    private void UpdateLlmUsageSummary()
    {
        var calls = _llmUsageTracker.CallCount;
        LlmUsageSummary = calls == 0
            ? "No LLM calls this session"
            : $"{calls} LLM call(s) this session, ~{_llmUsageTracker.EstimatedTokens:N0} tokens";
    }

    /// <summary>
    /// Real cache/orphan cleanup (replaces the old placebo stub): deletes
    /// orphaned thumbnails, orphaned managed media files, and *.tmp leftovers
    /// in the media tree, reporting the ACTUAL bytes freed. Never claims
    /// success without doing the work.
    /// </summary>
    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        try
        {
            StatusMessage = "Scanning for orphaned files...";
            var result = await _mediaService.CleanupOrphansAsync();

            StatusMessage = result.FilesDeleted == 0
                ? "Nothing to clean"
                : $"Freed {FormatBytes(result.BytesFreed)} ({result.FilesDeleted} file(s): " +
                  $"{result.MediaFilesDeleted} media, {result.ThumbnailsDeleted} thumbnails, {result.TempFilesDeleted} temp)";
            _logger.LogInformation(
                "Clear Cache deleted {Files} file(s), freeing {Bytes} bytes", result.FilesDeleted, result.BytesFreed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
            StatusMessage = $"Error clearing cache: {ex.Message}";
        }
    }

    #region Backup & Restore Commands (F12)

    /// <summary>
    /// Manual backup: pick a .mtbak destination (any writable location,
    /// including network shares), then write the archive with progress.
    /// A successful manual backup also stamps last_backup_at, postponing the
    /// next scheduled run.
    /// </summary>
    [RelayCommand]
    private async Task BackUpNowAsync()
    {
        if (IsBackingUp) return;

        try
        {
            IsBackingUp = true;
            BackupProgress = 0;
            BackupStatusMessage = "Selecting backup location...";

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"MemoryTimeline-{DateTime.Now:yyyyMMdd-HHmmss}"
            };
            savePicker.FileTypeChoices.Add("Memory Timeline Backup", new List<string> { ".mtbak" });

            var hwnd = WindowNative.GetWindowHandle(App.Current.Window);
            InitializeWithWindow.Initialize(savePicker, hwnd);

            var file = await savePicker.PickSaveFileAsync();
            if (file == null)
            {
                BackupStatusMessage = "Backup cancelled";
                return;
            }

            BackupStatusMessage = "Backing up...";
            var progress = new Progress<int>(p => BackupProgress = p);
            var options = new BackupOptions
            {
                IncludeMedia = IncludeMediaInBackup,
                IncludeAudio = IncludeAudioInBackup
            };

            var result = await _backupService.CreateBackupAsync(file.Path, options, progress);

            await _settingsService.SetSettingAsync(
                SettingKeys.LastBackupAt,
                DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            await RefreshLastBackupDisplayAsync();

            BackupStatusMessage =
                $"Backup complete: {result.EventCount} events, {FormatBytes(result.SizeBytes)} — {result.FilePath}";
            _logger.LogInformation("Manual backup written to {Path}", result.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual backup failed");
            BackupStatusMessage = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    /// <summary>
    /// Restore flow: pick a .mtbak, preview its manifest (zero writes), show
    /// an explicit confirmation dialog whose primary button is the confirm
    /// phrase "Replace current archive", then restore and recommend a restart.
    /// </summary>
    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (IsRestoring || IsBackingUp) return;

        try
        {
            BackupStatusMessage = "Selecting backup to restore...";

            var openPicker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            openPicker.FileTypeFilter.Add(".mtbak");

            var hwnd = WindowNative.GetWindowHandle(App.Current.Window);
            InitializeWithWindow.Initialize(openPicker, hwnd);

            var file = await openPicker.PickSingleFileAsync();
            if (file == null)
            {
                BackupStatusMessage = "Restore cancelled";
                return;
            }

            BackupStatusMessage = "Reading backup...";
            var preview = await _backupService.PreviewRestoreAsync(file.Path);

            var xamlRoot = App.Current.Window?.Content?.XamlRoot;
            if (xamlRoot == null)
            {
                BackupStatusMessage = "Restore failed: no window available for confirmation";
                return;
            }

            var detail =
                $"Backup created: {preview.CreatedAtUtc.ToLocalTime():g}\n" +
                $"Events: {preview.EventCount}\n" +
                $"Media files: {preview.MediaFileCount}\n" +
                $"Audio files: {preview.AudioFileCount}\n" +
                $"Size: {FormatBytes(preview.SizeBytes)}\n\n" +
                "Restoring REPLACES your current archive (database" +
                (preview.MediaFileCount > 0 || preview.AudioFileCount > 0 ? ", media and audio files" : "") +
                "). A safety backup of the current database is written first.";

            var confirmDialog = new ContentDialog
            {
                Title = "Restore from backup?",
                Content = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "Replace current archive",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            var choice = await confirmDialog.ShowAsync();
            if (choice != ContentDialogResult.Primary)
            {
                BackupStatusMessage = "Restore cancelled";
                return;
            }

            IsRestoring = true;
            BackupStatusMessage = "Restoring archive... (safety backup, database, files)";

            await _backupService.RestoreAsync(file.Path, RestoreMode.Replace);

            BackupStatusMessage = "Restore complete — restart Memory Timeline to load the restored archive.";
            StatusMessage = "Restore complete — please restart the app";
            _logger.LogWarning("Archive restored from {Path}; restart recommended", file.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed");
            BackupStatusMessage = $"Restore failed: {ex.Message}";
        }
        finally
        {
            IsRestoring = false;
        }
    }

    /// <summary>Folder picker persisting backup_destination immediately.</summary>
    [RelayCommand]
    private async Task PickBackupDestinationAsync()
    {
        try
        {
            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(App.Current.Window);
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder == null)
            {
                return;
            }

            BackupDestination = folder.Path;
            await _settingsService.SetSettingAsync(SettingKeys.BackupDestination, folder.Path);
            BackupStatusMessage = $"Backup folder set to {folder.Path}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error picking backup destination");
            BackupStatusMessage = $"Could not set backup folder: {ex.Message}";
        }
    }

    /// <summary>Opens the configured backup folder in File Explorer.</summary>
    [RelayCommand]
    private async Task OpenBackupFolderAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(BackupDestination))
            {
                BackupStatusMessage = "No backup folder configured yet — choose one first";
                return;
            }

            if (!Directory.Exists(BackupDestination))
            {
                BackupStatusMessage = $"The backup folder no longer exists: {BackupDestination}";
                return;
            }

            var opened = await Windows.System.Launcher.LaunchFolderPathAsync(BackupDestination);
            if (!opened)
            {
                BackupStatusMessage = "Windows could not open the backup folder";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening backup folder");
            BackupStatusMessage = $"Could not open backup folder: {ex.Message}";
        }
    }

    /// <summary>Schedule changes persist immediately (canonical lowercase).</summary>
    partial void OnSelectedBackupScheduleChanged(string value)
    {
        if (_isLoadingBackupSettings) return;
        _ = PersistBackupSettingAsync(SettingKeys.BackupSchedule, (value ?? "Off").ToLowerInvariant());
    }

    /// <summary>Include-media changes persist immediately.</summary>
    partial void OnIncludeMediaInBackupChanged(bool value)
    {
        if (_isLoadingBackupSettings) return;
        _ = PersistBackupSettingAsync(SettingKeys.BackupIncludeMedia, value ? "true" : "false");
    }

    private async Task PersistBackupSettingAsync(string key, string value)
    {
        try
        {
            await _settingsService.SetSettingAsync(key, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting setting {Key}", key);
            BackupStatusMessage = $"Could not save setting: {ex.Message}";
        }
    }

    private async Task RefreshLastBackupDisplayAsync()
    {
        var lastBackupRaw = await _settingsService.GetSettingAsync<string>(SettingKeys.LastBackupAt, string.Empty);
        if (DateTime.TryParse(
                lastBackupRaw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var lastBackup))
        {
            LastBackupDisplay = $"Last backup: {lastBackup.ToLocalTime():g}";
        }
        else
        {
            // Absent row = never backed up (last_backup_at is deliberately unseeded).
            LastBackupDisplay = "Last backup: never";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    #endregion

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

    /// <summary>
    /// Maps a canonical (lowercase) stored value to the matching Title-case
    /// display option so ComboBox selection is never blank on load.
    /// A stored value that is not a supported option normalizes to the default,
    /// so an unsupported value can never round-trip back into settings on save.
    /// (LLM/embedding providers use their dedicated key normalizers instead.)
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
