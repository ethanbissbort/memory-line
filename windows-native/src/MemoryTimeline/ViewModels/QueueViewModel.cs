using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Services;
using System.Collections.ObjectModel;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for the recording queue page.
/// </summary>
public partial class QueueViewModel : ObservableObject, IDisposable
{
    private readonly IQueueService _queueService;
    private readonly IAudioRecordingService _recordingService;
    private readonly IAudioPlaybackService _playbackService;
    private readonly ILogger<QueueViewModel> _logger;

    // Captured on construction (UI thread) so background service-event and timer
    // callbacks can marshal UI/observable mutations back onto the dispatcher.
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<AudioRecordingDto> _queueItems = new();

    [ObservableProperty]
    private AudioRecordingDto? _selectedItem;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private double _recordingDuration;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private int _processingCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _failedCount;

    // Computed properties for UI bindings
    public bool CanStartRecording => !IsRecording && !IsPaused;
    public bool CanPauseRecording => IsRecording;
    public bool CanResumeRecording => IsPaused;
    public bool CanStopRecording => IsRecording || IsPaused;
    public bool CanCancelRecording => IsRecording || IsPaused;
    /// <summary>True while a recording session exists, whether actively capturing or paused.</summary>
    public bool IsRecordingActive => IsRecording || IsPaused;
    public bool IsIdle => !IsRecording && !IsPaused && !IsProcessing;
    public bool HasPendingItems => PendingCount > 0;
    public bool IsQueueEmpty => QueueItems.Count == 0;
    public string FormattedRecordingDuration => TimeSpan.FromSeconds(_recordingDuration).ToString(@"mm\:ss");
    public string StatusMessage => StatusText;
    public bool CanStopPlayback => IsPlaying;

    // Commands for queue operations
    public IRelayCommand ProcessQueueCommand => ProcessAllCommand;

    // Playback properties
    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _playbackPosition;

    [ObservableProperty]
    private double _playbackDuration;

    private System.Threading.Timer? _recordingTimer;

    public QueueViewModel(
        IQueueService queueService,
        IAudioRecordingService recordingService,
        IAudioPlaybackService playbackService,
        ILogger<QueueViewModel> logger)
    {
        _queueService = queueService;
        _recordingService = recordingService;
        _playbackService = playbackService;
        _logger = logger;

        // Capture the UI dispatcher while we are still on the UI thread.
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Subscribe to service events. NOTE: the audio recording/playback services
        // are DI singletons while this ViewModel is transient, so these subscriptions
        // root the ViewModel for the lifetime of the app unless Dispose() is called.
        _queueService.QueueItemStatusChanged += OnQueueItemStatusChanged;
        _queueService.ProcessingProgressChanged += OnProcessingProgressChanged;
        _recordingService.RecordingStateChanged += OnRecordingStateChanged;
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
        _playbackService.PositionChanged += OnPlaybackPositionChanged;
    }

    /// <summary>
    /// Marshals the supplied action onto the UI dispatcher thread. Service events and
    /// timer callbacks arrive on background threads; mutating observable state or
    /// ObservableCollections off the UI thread corrupts/crashes the WinUI binding layer.
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
    /// Initializes the queue view.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            // Re-attach to any in-flight recording. The recording service is a
            // singleton that outlives this transient ViewModel, so navigating
            // away and back must resume tracking instead of orphaning a hot mic.
            var recordingState = _recordingService.GetRecordingState();
            IsRecording = recordingState == AudioRecordingState.Recording;
            IsPaused = recordingState == AudioRecordingState.Paused;

            if (IsRecording || IsPaused)
            {
                RecordingDuration = _recordingService.GetRecordingDuration();
            }

            if (IsRecording)
            {
                StartRecordingTimer();
            }

            await RefreshQueueAsync();

            StatusText = IsRecording
                ? "Recording..."
                : IsPaused
                    ? "Recording paused"
                    : $"{QueueItems.Count} recordings in queue";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing queue");
            StatusText = "Error loading queue";
        }
    }

    /// <summary>
    /// (Re)starts the timer that polls the recording duration while capturing.
    /// The timer callback runs on a threadpool thread, so the observable update
    /// is marshalled back onto the UI dispatcher.
    /// </summary>
    private void StartRecordingTimer()
    {
        _recordingTimer?.Dispose();
        _recordingTimer = new System.Threading.Timer(
            _ => RunOnUi(() => RecordingDuration = _recordingService.GetRecordingDuration()),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(100));
    }

    #region Recording Commands

    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        try
        {
            var settings = new AudioRecordingSettings
            {
                SampleRate = 16000,
                BitsPerSample = 16,
                Channels = 1
            };

            await _recordingService.StartRecordingAsync(settings);
            IsRecording = true;
            IsPaused = false;
            StatusText = "Recording...";

            StartRecordingTimer();

            _logger.LogInformation("Recording started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting recording");
            StatusText = "Error starting recording";
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteStopRecording))]
    private async Task StopRecordingAsync()
    {
        try
        {
            _recordingTimer?.Dispose();
            _recordingTimer = null;

            var recording = await _recordingService.StopRecordingAsync();
            IsRecording = false;
            IsPaused = false;
            StatusText = "Recording stopped";

            // Add to queue
            await _queueService.AddToQueueAsync(recording);
            await RefreshQueueAsync();

            _logger.LogInformation("Recording stopped and added to queue");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping recording");
            StatusText = "Error stopping recording";
        }
    }

    // Stop must also work while paused, otherwise pause becomes a one-way trap
    // that leaves the hardware session open until app exit.
    private bool CanExecuteStopRecording() => IsRecording || IsPaused;

    [RelayCommand(CanExecute = nameof(CanExecutePauseRecording))]
    private async Task PauseRecordingAsync()
    {
        try
        {
            await _recordingService.PauseRecordingAsync();

            _recordingTimer?.Dispose();
            _recordingTimer = null;

            // Paused is a distinct state: the session still exists (hardware
            // resources retained), it is just not capturing. Do NOT collapse
            // this into "not recording at all".
            IsRecording = false;
            IsPaused = true;
            StatusText = "Recording paused";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing recording");
            StatusText = "Error pausing recording";
        }
    }

    private bool CanExecutePauseRecording() => IsRecording;

    [RelayCommand(CanExecute = nameof(CanExecuteResumeRecording))]
    private async Task ResumeRecordingAsync()
    {
        try
        {
            await _recordingService.ResumeRecordingAsync();
            IsRecording = true;
            IsPaused = false;
            StatusText = "Recording...";

            StartRecordingTimer();

            _logger.LogInformation("Recording resumed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming recording");
            StatusText = "Error resuming recording";
        }
    }

    private bool CanExecuteResumeRecording() => IsPaused;

    [RelayCommand(CanExecute = nameof(CanExecuteCancelRecording))]
    private async Task CancelRecordingAsync()
    {
        try
        {
            _recordingTimer?.Dispose();
            _recordingTimer = null;

            await _recordingService.CancelRecordingAsync();
            IsRecording = false;
            IsPaused = false;
            RecordingDuration = 0;
            StatusText = "Recording cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling recording");
            StatusText = "Error cancelling recording";
        }
    }

    // Cancel must also work while paused (see CanExecuteStopRecording).
    private bool CanExecuteCancelRecording() => IsRecording || IsPaused;

    #endregion

    #region Queue Management Commands

    [RelayCommand]
    private async Task RefreshQueueAsync()
    {
        try
        {
            var items = await _queueService.GetAllQueueItemsAsync();

            QueueItems.Clear();
            foreach (var item in items)
            {
                // Initialize commands for each item
                item.PlayCommand = new RelayCommand(() => _ = PlayItemAsync(item));
                item.RetryCommand = new RelayCommand(() => _ = RetryItemAsync(item));
                item.RemoveCommand = new RelayCommand(() => _ = RemoveItemAsync(item));

                QueueItems.Add(item);
            }

            OnPropertyChanged(nameof(IsQueueEmpty));
            await UpdateStatusCountsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing queue");
        }
    }

    [RelayCommand]
    private async Task ProcessNextAsync()
    {
        if (IsProcessing) return;

        try
        {
            IsProcessing = true;
            StatusText = "Processing next item...";

            await _queueService.ProcessNextItemAsync();

            await RefreshQueueAsync();
            StatusText = "Processing complete";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing next item");
            StatusText = "Error processing item";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task ProcessAllAsync()
    {
        if (IsProcessing) return;

        try
        {
            IsProcessing = true;
            StatusText = "Processing all pending items...";

            await _queueService.ProcessAllPendingAsync();

            await RefreshQueueAsync();
            StatusText = "All items processed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing all items");
            StatusText = "Error processing items";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task RemoveItemAsync(AudioRecordingDto? item)
    {
        if (item == null) return;

        try
        {
            await _queueService.RemoveQueueItemAsync(item.QueueId);
            QueueItems.Remove(item);
            OnPropertyChanged(nameof(IsQueueEmpty));
            await UpdateStatusCountsAsync();

            StatusText = "Item removed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing item");
            StatusText = "Error removing item";
        }
    }

    [RelayCommand]
    private async Task RetryItemAsync(AudioRecordingDto? item)
    {
        if (item == null || item.Status != Data.Models.QueueStatus.Failed) return;

        try
        {
            await _queueService.RetryFailedItemAsync(item.QueueId);
            await RefreshQueueAsync();

            StatusText = "Item queued for retry";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrying item");
            StatusText = "Error retrying item";
        }
    }

    [RelayCommand]
    private async Task ClearCompletedAsync()
    {
        try
        {
            await _queueService.ClearCompletedItemsAsync();
            await RefreshQueueAsync();

            StatusText = "Completed items cleared";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing completed items");
            StatusText = "Error clearing items";
        }
    }

    #endregion

    #region Playback Commands

    [RelayCommand]
    private async Task PlayItemAsync(AudioRecordingDto? item)
    {
        if (item == null) return;

        try
        {
            if (IsPlaying && SelectedItem?.QueueId == item.QueueId)
            {
                // Pause current playback
                _playbackService.Pause();
            }
            else
            {
                // Load and play new item
                SelectedItem = item;
                await _playbackService.LoadAudioAsync(item.AudioFilePath);
                _playbackService.Play();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing audio");
            StatusText = "Error playing audio";
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteStopPlayback))]
    private void StopPlayback()
    {
        try
        {
            _playbackService.Stop();
            SelectedItem = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping playback");
        }
    }

    private bool CanExecuteStopPlayback() => IsPlaying;

    #endregion

    #region Property Changed Handlers

    partial void OnIsRecordingChanged(bool value)
    {
        NotifyRecordingStateDependents();
    }

    partial void OnIsPausedChanged(bool value)
    {
        NotifyRecordingStateDependents();
    }

    /// <summary>
    /// Re-evaluates every computed property and command CanExecute that derives from
    /// IsRecording/IsPaused. Called from both partials so state set directly by
    /// commands and state pushed by RecordingStateChanged stay consistent.
    /// </summary>
    private void NotifyRecordingStateDependents()
    {
        OnPropertyChanged(nameof(CanStartRecording));
        OnPropertyChanged(nameof(CanPauseRecording));
        OnPropertyChanged(nameof(CanResumeRecording));
        OnPropertyChanged(nameof(CanStopRecording));
        OnPropertyChanged(nameof(CanCancelRecording));
        OnPropertyChanged(nameof(IsRecordingActive));
        OnPropertyChanged(nameof(IsIdle));

        StopRecordingCommand.NotifyCanExecuteChanged();
        PauseRecordingCommand.NotifyCanExecuteChanged();
        ResumeRecordingCommand.NotifyCanExecuteChanged();
        CancelRecordingCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsProcessingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStopPlayback));
    }

    partial void OnPendingCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasPendingItems));
    }

    partial void OnRecordingDurationChanged(double value)
    {
        OnPropertyChanged(nameof(FormattedRecordingDuration));
    }

    partial void OnStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(StatusMessage));
    }

    #endregion

    #region Private Methods

    private async Task UpdateStatusCountsAsync()
    {
        PendingCount = await _queueService.GetQueueCountByStatusAsync(Data.Models.QueueStatus.Pending);
        ProcessingCount = await _queueService.GetQueueCountByStatusAsync(Data.Models.QueueStatus.Processing);
        CompletedCount = await _queueService.GetQueueCountByStatusAsync(Data.Models.QueueStatus.Completed);
        FailedCount = await _queueService.GetQueueCountByStatusAsync(Data.Models.QueueStatus.Failed);
    }

    private void OnQueueItemStatusChanged(object? sender, QueueItemStatusChangedEventArgs e)
    {
        // Service events arrive on a background thread; marshal all observable mutations.
        RunOnUi(() =>
        {
            var item = QueueItems.FirstOrDefault(i => i.QueueId == e.QueueId);
            if (item != null)
            {
                item.Status = e.NewStatus;
                item.ErrorMessage = e.ErrorMessage;
            }

            _ = UpdateStatusCountsAsync();
        });
    }

    private void OnRecordingStateChanged(object? sender, AudioRecordingStateChangedEventArgs e)
    {
        RunOnUi(() =>
        {
            // Map the full service state: Paused must remain distinct from Idle,
            // otherwise the UI loses track of the still-open recording session.
            IsRecording = e.NewState == AudioRecordingState.Recording;
            IsPaused = e.NewState == AudioRecordingState.Paused;
            // Computed properties and command CanExecute states are refreshed by
            // the OnIsRecordingChanged/OnIsPausedChanged partials.
        });
    }

    private void OnProcessingProgressChanged(object? sender, QueueProcessingProgressEventArgs e)
    {
        // Raised from the queue's background processing; marshal before touching
        // observable state so the per-item ProgressBar bindings update safely.
        RunOnUi(() =>
        {
            var item = QueueItems.FirstOrDefault(i => i.QueueId == e.QueueId);
            if (item != null)
            {
                item.Progress = e.ProgressPercentage;
            }

            if (!string.IsNullOrEmpty(e.StatusMessage))
            {
                StatusText = e.StatusMessage;
            }
        });
    }

    private void OnPlaybackStateChanged(object? sender, AudioPlaybackStateChangedEventArgs e)
    {
        RunOnUi(() =>
        {
            IsPlaying = e.NewState == AudioPlaybackState.Playing;
            StopPlaybackCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnPlaybackPositionChanged(object? sender, AudioPositionChangedEventArgs e)
    {
        // Fires very frequently on a background thread during playback.
        RunOnUi(() =>
        {
            PlaybackPosition = e.Position;
            PlaybackDuration = e.Duration;
        });
    }

    #endregion

    /// <summary>
    /// Unsubscribes from service events and stops the recording timer. Must be called
    /// when the owning page is unloaded/navigated away from, otherwise the singleton
    /// audio services keep this transient ViewModel alive for the life of the app.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _queueService.QueueItemStatusChanged -= OnQueueItemStatusChanged;
        _queueService.ProcessingProgressChanged -= OnProcessingProgressChanged;
        _recordingService.RecordingStateChanged -= OnRecordingStateChanged;
        _playbackService.PlaybackStateChanged -= OnPlaybackStateChanged;
        _playbackService.PositionChanged -= OnPlaybackPositionChanged;

        _recordingTimer?.Dispose();
        _recordingTimer = null;

        GC.SuppressFinalize(this);
    }
}
