using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryTimeline.Core.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for the narrative-generation dialog (Controls/NarrativeDialog).
/// Transient: each dialog opening gets a fresh instance, so cancellation
/// state never leaks between runs. The hosting dialog calls
/// <see cref="Initialize"/> with the scope request and
/// <see cref="CancelPendingGeneration"/> when it closes.
/// </summary>
public partial class NarrativeDialogViewModel : ObservableObject
{
    private readonly INarrativeService _narrativeService;
    private readonly IExportService _exportService;
    private readonly ILogger<NarrativeDialogViewModel> _logger;

    private NarrativeRequest? _request;
    private Narrative? _narrative;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _scopeDisplayName = string.Empty;

    /// <summary>Selected voice index: 0 Reflective, 1 Factual, 2 Warm.</summary>
    [ObservableProperty]
    private int _voiceIndex;

    /// <summary>Target words for the whole narrative (NumberBox binds double).</summary>
    [ObservableProperty]
    private double _targetWords = 1200;

    [ObservableProperty]
    private bool _includeCitations = true;

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private string _stageText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _narrativeTitle = string.Empty;

    [ObservableProperty]
    private ObservableCollection<NarrativeSection> _sections = new();

    [ObservableProperty]
    private bool _hasNarrative;

    /// <summary>Label for the single generate/regenerate button.</summary>
    public string GenerateButtonText => HasNarrative ? "Regenerate" : "Generate";

    public NarrativeDialogViewModel(
        INarrativeService narrativeService,
        IExportService exportService,
        ILogger<NarrativeDialogViewModel> logger)
    {
        _narrativeService = narrativeService;
        _exportService = exportService;
        _logger = logger;
    }

    /// <summary>
    /// Seeds the dialog with its scope. Voice/target-words/citations are
    /// edited through the bound controls before generating.
    /// </summary>
    public void Initialize(NarrativeRequest request, string scopeDisplayName)
    {
        _request = request;
        ScopeDisplayName = scopeDisplayName;
        VoiceIndex = (int)request.Voice;
        TargetWords = request.TargetWords;
        IncludeCitations = request.IncludeCitations;
        GenerateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Cancels any in-flight generation (called when the dialog closes).
    /// </summary>
    public void CancelPendingGeneration()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down; nothing to cancel.
        }
    }

    partial void OnIsGeneratingChanged(bool value)
    {
        GenerateCommand.NotifyCanExecuteChanged();
        ExportMarkdownCommand.NotifyCanExecuteChanged();
        ExportHtmlCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasNarrativeChanged(bool value)
    {
        OnPropertyChanged(nameof(GenerateButtonText));
        ExportMarkdownCommand.NotifyCanExecuteChanged();
        ExportHtmlCommand.NotifyCanExecuteChanged();
    }

    private bool CanGenerate() => !IsGenerating && _request != null;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        var request = _request;
        if (request == null)
        {
            return;
        }

        ErrorMessage = null;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsGenerating = true;
        StageText = "Starting…";

        try
        {
            request.Voice = VoiceIndex switch
            {
                1 => NarrativeVoice.Factual,
                2 => NarrativeVoice.Warm,
                _ => NarrativeVoice.Reflective
            };
            request.TargetWords = double.IsNaN(TargetWords)
                ? 1200
                : (int)Math.Clamp(TargetWords, 150, 8000);
            request.IncludeCitations = IncludeCitations;

            // Progress is constructed on the UI thread, so reports marshal
            // back through the synchronization context automatically.
            var progress = new Progress<(int pct, string stage)>(p => StageText = p.stage);

            var narrative = await _narrativeService.GenerateAsync(request, progress, ct);

            _narrative = narrative;
            NarrativeTitle = narrative.Title;
            Sections.Clear();
            foreach (var section in narrative.Sections)
            {
                Sections.Add(section);
            }

            HasNarrative = narrative.Sections.Count > 0;
            StageText = narrative.Sections.Count == 0
                ? $"No memories found for {narrative.ScopeLabel}."
                : $"Story ready — {narrative.Sections.Count} section(s) from {narrative.SourceEventIds.Count} memories.";
        }
        catch (OperationCanceledException)
        {
            // User closed the dialog or re-triggered generation: not an error.
            StageText = "Generation cancelled.";
            _logger.LogInformation("Narrative generation cancelled for scope {Scope}", request.Scope);
        }
        catch (ConfigurationException ex)
        {
            _logger.LogWarning(ex, "Narrative generation blocked by missing configuration");
            StageText = string.Empty;
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating narrative for scope {Scope}", request.Scope);
            StageText = string.Empty;
            ErrorMessage = $"Could not generate the story: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private bool CanExport() => !IsGenerating && _narrative != null && _narrative.Sections.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportMarkdownAsync()
    {
        await ExportAsync(NarrativeFormat.Markdown);
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportHtmlAsync()
    {
        await ExportAsync(NarrativeFormat.Html);
    }

    private async Task ExportAsync(NarrativeFormat format)
    {
        var narrative = _narrative;
        if (narrative == null || narrative.Sections.Count == 0)
        {
            return;
        }

        try
        {
            ErrorMessage = null;

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = BuildSuggestedFileName(narrative)
            };

            if (format == NarrativeFormat.Html)
            {
                savePicker.FileTypeChoices.Add("HTML File", new List<string> { ".html" });
            }
            else
            {
                savePicker.FileTypeChoices.Add("Markdown File", new List<string> { ".md" });
            }

            // Unpackaged WinUI 3: pickers must be initialized with the window handle.
            var hwnd = WindowNative.GetWindowHandle(App.Current.Window);
            InitializeWithWindow.Initialize(savePicker, hwnd);

            var file = await savePicker.PickSaveFileAsync();
            if (file == null)
            {
                StageText = "Export cancelled";
                return;
            }

            await _exportService.ExportNarrativeAsync(narrative, file.Path, format);

            StageText = $"Exported: {file.Path}";
            _logger.LogInformation("Exported narrative as {Format}: {Path}", format, file.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting narrative as {Format}", format);
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    private static string BuildSuggestedFileName(Narrative narrative)
    {
        var baseName = string.IsNullOrWhiteSpace(narrative.Title)
            ? "MemoryTimeline_Story"
            : narrative.Title;

        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(baseName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();

        if (safe.Length == 0)
        {
            safe = "MemoryTimeline_Story";
        }

        if (safe.Length > 60)
        {
            safe = safe[..60].TrimEnd();
        }

        return safe;
    }
}
