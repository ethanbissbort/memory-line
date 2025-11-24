using MemoryTimeline.Core.DTOs;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for audio recording operations.
/// </summary>
public interface IAudioRecordingService
{
    /// <summary>
    /// Gets available audio input devices.
    /// </summary>
    Task<IEnumerable<AudioDeviceDto>> GetAvailableDevicesAsync();

    /// <summary>
    /// Starts audio recording with specified settings.
    /// </summary>
    Task<string> StartRecordingAsync(AudioRecordingSettings settings);

    /// <summary>
    /// Stops the current recording and saves the file.
    /// </summary>
    Task<AudioRecordingDto> StopRecordingAsync();

    /// <summary>
    /// Pauses the current recording.
    /// </summary>
    Task PauseRecordingAsync();

    /// <summary>
    /// Resumes a paused recording.
    /// </summary>
    Task ResumeRecordingAsync();

    /// <summary>
    /// Cancels the current recording without saving.
    /// </summary>
    Task CancelRecordingAsync();

    /// <summary>
    /// Gets the current recording state.
    /// </summary>
    AudioRecordingState GetRecordingState();

    /// <summary>
    /// Gets the current recording duration in seconds.
    /// </summary>
    double GetRecordingDuration();

    /// <summary>
    /// Event raised when recording state changes.
    /// </summary>
    event EventHandler<AudioRecordingStateChangedEventArgs>? RecordingStateChanged;

    /// <summary>
    /// Event raised when audio level changes during recording.
    /// </summary>
    event EventHandler<AudioLevelChangedEventArgs>? AudioLevelChanged;
}

/// <summary>
/// Audio recording state enumeration.
/// </summary>
public enum AudioRecordingState
{
    Idle,
    Recording,
    Paused,
    Stopping
}

/// <summary>
/// Event args for recording state change.
/// </summary>
public class AudioRecordingStateChangedEventArgs : EventArgs
{
    public AudioRecordingState OldState { get; set; }
    public AudioRecordingState NewState { get; set; }
}

/// <summary>
/// Event args for audio level change.
/// </summary>
public class AudioLevelChangedEventArgs : EventArgs
{
    public double Level { get; set; } // 0.0 to 1.0
}
