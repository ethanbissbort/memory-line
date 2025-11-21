using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Services;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Devices.Enumeration;
using System.Diagnostics;

namespace MemoryTimeline.Services;

/// <summary>
/// Windows-specific audio recording service implementation using MediaCapture API.
/// </summary>
public class AudioRecordingService : IAudioRecordingService, IDisposable
{
    private readonly ILogger<AudioRecordingService> _logger;
    private MediaCapture? _mediaCapture;
    private StorageFile? _recordingFile;
    private AudioRecordingState _recordingState = AudioRecordingState.Idle;
    private readonly Stopwatch _recordingTimer = new();
    private AudioRecordingSettings? _currentSettings;

    public event EventHandler<AudioRecordingStateChangedEventArgs>? RecordingStateChanged;
    public event EventHandler<AudioLevelChangedEventArgs>? AudioLevelChanged;

    public AudioRecordingService(ILogger<AudioRecordingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets available audio input devices.
    /// </summary>
    public async Task<IEnumerable<AudioDeviceDto>> GetAvailableDevicesAsync()
    {
        try
        {
            var devices = new List<AudioDeviceDto>();

            // Find all audio capture devices
            var deviceInfo = await DeviceInformation.FindAllAsync(
                DeviceClass.AudioCapture);

            foreach (var info in deviceInfo)
            {
                devices.Add(new AudioDeviceDto
                {
                    DeviceId = info.Id,
                    Name = info.Name,
                    IsDefault = info.IsDefault,
                    IsEnabled = info.IsEnabled
                });
            }

            _logger.LogInformation("Found {Count} audio input devices", devices.Count);
            return devices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting audio devices");
            return Enumerable.Empty<AudioDeviceDto>();
        }
    }

    /// <summary>
    /// Starts audio recording with specified settings.
    /// </summary>
    public async Task<string> StartRecordingAsync(AudioRecordingSettings settings)
    {
        if (_recordingState != AudioRecordingState.Idle)
        {
            throw new InvalidOperationException("Recording already in progress");
        }

        try
        {
            _logger.LogInformation("Starting audio recording with settings: {SampleRate}Hz, {BitsPerSample}bit",
                settings.SampleRate, settings.BitsPerSample);

            _currentSettings = settings;

            // Initialize MediaCapture
            _mediaCapture = new MediaCapture();
            var captureSettings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio
            };

            // Use specific device if provided
            if (!string.IsNullOrEmpty(settings.DeviceId))
            {
                captureSettings.AudioDeviceId = settings.DeviceId;
            }

            await _mediaCapture.InitializeAsync(captureSettings);

            // Create output file
            var fileName = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            var audioFolder = await GetAudioFolderAsync();
            _recordingFile = await audioFolder.CreateFileAsync(fileName,
                CreationCollisionOption.GenerateUniqueName);

            // Configure audio encoding
            var encodingProfile = CreateEncodingProfile(settings);

            // Start recording
            await _mediaCapture.StartRecordToStorageFileAsync(encodingProfile, _recordingFile);

            _recordingTimer.Restart();
            SetRecordingState(AudioRecordingState.Recording);

            _logger.LogInformation("Recording started successfully to file: {FileName}", fileName);
            return _recordingFile.Path;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting audio recording");
            await CleanupMediaCaptureAsync();
            throw;
        }
    }

    /// <summary>
    /// Stops the current recording and saves the file.
    /// </summary>
    public async Task<AudioRecordingDto> StopRecordingAsync()
    {
        if (_recordingState != AudioRecordingState.Recording && _recordingState != AudioRecordingState.Paused)
        {
            throw new InvalidOperationException("No recording in progress");
        }

        try
        {
            SetRecordingState(AudioRecordingState.Stopping);

            // Stop recording
            if (_mediaCapture != null)
            {
                await _mediaCapture.StopRecordAsync();
            }

            _recordingTimer.Stop();

            // Get file info
            var properties = await _recordingFile!.GetBasicPropertiesAsync();
            var duration = _recordingTimer.Elapsed.TotalSeconds;

            _logger.LogInformation("Recording stopped. Duration: {Duration}s, Size: {Size} bytes",
                duration, properties.Size);

            var dto = new AudioRecordingDto
            {
                QueueId = Guid.NewGuid().ToString(),
                AudioFilePath = _recordingFile.Path,
                Status = Data.Models.QueueStatus.Pending,
                DurationSeconds = duration,
                FileSizeBytes = (long)properties.Size,
                CreatedAt = DateTime.UtcNow
            };

            await CleanupMediaCaptureAsync();
            SetRecordingState(AudioRecordingState.Idle);

            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping audio recording");
            await CleanupMediaCaptureAsync();
            SetRecordingState(AudioRecordingState.Idle);
            throw;
        }
    }

    /// <summary>
    /// Pauses the current recording.
    /// </summary>
    public async Task PauseRecordingAsync()
    {
        if (_recordingState != AudioRecordingState.Recording)
        {
            throw new InvalidOperationException("No active recording to pause");
        }

        try
        {
            if (_mediaCapture != null)
            {
                await _mediaCapture.PauseRecordAsync(Windows.Media.Devices.MediaCapturePauseBehavior.RetainHardwareResources);
            }

            _recordingTimer.Stop();
            SetRecordingState(AudioRecordingState.Paused);

            _logger.LogInformation("Recording paused at {Duration}s", _recordingTimer.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing audio recording");
            throw;
        }
    }

    /// <summary>
    /// Resumes a paused recording.
    /// </summary>
    public async Task ResumeRecordingAsync()
    {
        if (_recordingState != AudioRecordingState.Paused)
        {
            throw new InvalidOperationException("No paused recording to resume");
        }

        try
        {
            if (_mediaCapture != null)
            {
                await _mediaCapture.ResumeRecordAsync();
            }

            _recordingTimer.Start();
            SetRecordingState(AudioRecordingState.Recording);

            _logger.LogInformation("Recording resumed at {Duration}s", _recordingTimer.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming audio recording");
            throw;
        }
    }

    /// <summary>
    /// Cancels the current recording without saving.
    /// </summary>
    public async Task CancelRecordingAsync()
    {
        if (_recordingState == AudioRecordingState.Idle)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Cancelling recording");

            // Stop recording if active
            if (_recordingState == AudioRecordingState.Recording || _recordingState == AudioRecordingState.Paused)
            {
                if (_mediaCapture != null)
                {
                    await _mediaCapture.StopRecordAsync();
                }
            }

            // Delete the recording file
            if (_recordingFile != null)
            {
                try
                {
                    await _recordingFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    _logger.LogInformation("Recording file deleted");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error deleting recording file");
                }
            }

            _recordingTimer.Reset();
            await CleanupMediaCaptureAsync();
            SetRecordingState(AudioRecordingState.Idle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling audio recording");
            await CleanupMediaCaptureAsync();
            SetRecordingState(AudioRecordingState.Idle);
        }
    }

    /// <summary>
    /// Gets the current recording state.
    /// </summary>
    public AudioRecordingState GetRecordingState()
    {
        return _recordingState;
    }

    /// <summary>
    /// Gets the current recording duration in seconds.
    /// </summary>
    public double GetRecordingDuration()
    {
        return _recordingTimer.Elapsed.TotalSeconds;
    }

    #region Private Methods

    private void SetRecordingState(AudioRecordingState newState)
    {
        var oldState = _recordingState;
        _recordingState = newState;

        RecordingStateChanged?.Invoke(this, new AudioRecordingStateChangedEventArgs
        {
            OldState = oldState,
            NewState = newState
        });
    }

    private MediaEncodingProfile CreateEncodingProfile(AudioRecordingSettings settings)
    {
        var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);

        // Configure audio properties
        profile.Audio.BitsPerSample = (uint)settings.BitsPerSample;
        profile.Audio.SampleRate = (uint)settings.SampleRate;
        profile.Audio.ChannelCount = (uint)settings.Channels;

        return profile;
    }

    private async Task<StorageFolder> GetAudioFolderAsync()
    {
        var localFolder = ApplicationData.Current.LocalFolder;
        var audioFolder = await localFolder.CreateFolderAsync("AudioRecordings",
            CreationCollisionOption.OpenIfExists);
        return audioFolder;
    }

    private async Task CleanupMediaCaptureAsync()
    {
        if (_mediaCapture != null)
        {
            try
            {
                _mediaCapture.Dispose();
                _mediaCapture = null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MediaCapture");
            }
        }

        _recordingFile = null;
        _currentSettings = null;

        await Task.CompletedTask;
    }

    #endregion

    public void Dispose()
    {
        _mediaCapture?.Dispose();
        GC.SuppressFinalize(this);
    }
}
