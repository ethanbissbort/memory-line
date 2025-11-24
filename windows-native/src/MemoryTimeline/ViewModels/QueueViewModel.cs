using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Services;
using System.Collections.ObjectModel;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for the recording queue page.
/// </summary>
public partial class QueueViewModel : ObservableObject
{
    private readonly IQueueService _queueService;
    private readonly IAudioRecordingService _recordingService;
    private readonly IAudioPlaybackService _playbackService;
    private readonly ILogger<QueueViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<AudioRecordingDto> _queueItems = new();

    [ObservableProperty]
    private AudioRecordingDto? _selectedItem;

    [ObservableProperty]
    private bool _isRecording;

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

        // Subscribe to service events
        _queueService.QueueItemStatusChanged += OnQueueItemStatusChanged;
        _recordingService.RecordingStateChanged += OnRecordingStateChanged;
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
        _playbackService.PositionChanged += OnPlaybackPositionChanged;
    }

    /// <summary>
    /// Initializes the queue view.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            await RefreshQueueAsync();
            StatusText = $"{QueueItems.Count} recordings in queue";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing queue");
            StatusText = "Error loading queue";
        }
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
            StatusText = "Recording...";

            // Start timer to update duration
            _recordingTimer = new System.Threading.Timer(
                _ => RecordingDuration = _recordingService.GetRecordingDuration(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(100));

            _logger.LogInformation("Recording started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting recording");
            StatusText = "Error starting recording";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private async Task StopRecordingAsync()
    {
        try
        {
            _recordingTimer?.Dispose();
            _recordingTimer = null;

            var recording = await _recordingService.StopRecordingAsync();
            IsRecording = false;
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

    private bool CanStopRecording() => IsRecording;

    [RelayCommand(CanExecute = nameof(CanPauseRecording))]
    private async Task PauseRecordingAsync()
    {
        try
        {
            await _recordingService.PauseRecordingAsync();
            _recordingTimer?.Dispose();
            StatusText = "Recording paused";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing recording");
        }
    }

    private bool CanPauseRecording() => IsRecording;

    [RelayCommand(CanExecute = nameof(CanCancelRecording))]
    private async Task CancelRecordingAsync()
    {
        try
        {
            _recordingTimer?.Dispose();
            _recordingTimer = null;

            await _recordingService.CancelRecordingAsync();
            IsRecording = false;
            RecordingDuration = 0;
            StatusText = "Recording cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling recording");
        }
    }

    private bool CanCancelRecording() => IsRecording;

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
                QueueItems.Add(item);
            }

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

    [RelayCommand(CanExecute = nameof(CanStopPlayback))]
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

    private bool CanStopPlayback() => IsPlaying;

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
        // Update UI on status change
        var item = QueueItems.FirstOrDefault(i => i.QueueId == e.QueueId);
        if (item != null)
        {
            item.Status = e.NewStatus;
            item.ErrorMessage = e.ErrorMessage;
        }

        _ = UpdateStatusCountsAsync();
    }

    private void OnRecordingStateChanged(object? sender, AudioRecordingStateChangedEventArgs e)
    {
        IsRecording = e.NewState == AudioRecordingState.Recording;

        // Update command CanExecute states
        StopRecordingCommand.NotifyCanExecuteChanged();
        PauseRecordingCommand.NotifyCanExecuteChanged();
        CancelRecordingCommand.NotifyCanExecuteChanged();
    }

    private void OnPlaybackStateChanged(object? sender, AudioPlaybackStateChangedEventArgs e)
    {
        IsPlaying = e.NewState == AudioPlaybackState.Playing;
        StopPlaybackCommand.NotifyCanExecuteChanged();
    }

    private void OnPlaybackPositionChanged(object? sender, AudioPositionChangedEventArgs e)
    {
        PlaybackPosition = e.Position;
        PlaybackDuration = e.Duration;
    }

    #endregion
}
