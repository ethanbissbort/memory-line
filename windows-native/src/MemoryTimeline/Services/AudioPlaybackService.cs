using Microsoft.Extensions.Logging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace MemoryTimeline.Services;

/// <summary>
/// Service interface for audio playback operations.
/// </summary>
public interface IAudioPlaybackService
{
    /// <summary>
    /// Loads an audio file for playback.
    /// </summary>
    Task LoadAudioAsync(string filePath);

    /// <summary>
    /// Starts or resumes playback.
    /// </summary>
    void Play();

    /// <summary>
    /// Pauses playback.
    /// </summary>
    void Pause();

    /// <summary>
    /// Stops playback and resets position.
    /// </summary>
    void Stop();

    /// <summary>
    /// Seeks to a specific position in seconds.
    /// </summary>
    void Seek(double seconds);

    /// <summary>
    /// Gets the current playback position in seconds.
    /// </summary>
    double GetPosition();

    /// <summary>
    /// Gets the total duration in seconds.
    /// </summary>
    double GetDuration();

    /// <summary>
    /// Gets or sets the playback volume (0.0 to 1.0).
    /// </summary>
    double Volume { get; set; }

    /// <summary>
    /// Gets the current playback state.
    /// </summary>
    AudioPlaybackState GetPlaybackState();

    /// <summary>
    /// Event raised when playback state changes.
    /// </summary>
    event EventHandler<AudioPlaybackStateChangedEventArgs>? PlaybackStateChanged;

    /// <summary>
    /// Event raised when playback position changes.
    /// </summary>
    event EventHandler<AudioPositionChangedEventArgs>? PositionChanged;
}

/// <summary>
/// Audio playback state enumeration.
/// </summary>
public enum AudioPlaybackState
{
    None,
    Opening,
    Playing,
    Paused,
    Stopped
}

/// <summary>
/// Event args for playback state change.
/// </summary>
public class AudioPlaybackStateChangedEventArgs : EventArgs
{
    public AudioPlaybackState OldState { get; set; }
    public AudioPlaybackState NewState { get; set; }
}

/// <summary>
/// Event args for playback position change.
/// </summary>
public class AudioPositionChangedEventArgs : EventArgs
{
    public double Position { get; set; }
    public double Duration { get; set; }
}

/// <summary>
/// Windows-specific audio playback service implementation using MediaPlayer API.
/// </summary>
public class AudioPlaybackService : IAudioPlaybackService, IDisposable
{
    private readonly ILogger<AudioPlaybackService> _logger;
    private MediaPlayer? _mediaPlayer;
    private AudioPlaybackState _playbackState = AudioPlaybackState.None;
    private System.Threading.Timer? _positionTimer;

    public event EventHandler<AudioPlaybackStateChangedEventArgs>? PlaybackStateChanged;
    public event EventHandler<AudioPositionChangedEventArgs>? PositionChanged;

    public AudioPlaybackService(ILogger<AudioPlaybackService> logger)
    {
        _logger = logger;
        InitializeMediaPlayer();
    }

    /// <summary>
    /// Gets or sets the playback volume (0.0 to 1.0).
    /// </summary>
    public double Volume
    {
        get => _mediaPlayer?.Volume ?? 1.0;
        set
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = Math.Clamp(value, 0.0, 1.0);
            }
        }
    }

    /// <summary>
    /// Loads an audio file for playback.
    /// </summary>
    public async Task LoadAudioAsync(string filePath)
    {
        try
        {
            _logger.LogInformation("Loading audio file: {FilePath}", filePath);

            SetPlaybackState(AudioPlaybackState.Opening);

            var file = await StorageFile.GetFileFromPathAsync(filePath);
            var mediaSource = MediaSource.CreateFromStorageFile(file);
            _mediaPlayer!.Source = mediaSource;

            _logger.LogInformation("Audio file loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading audio file");
            SetPlaybackState(AudioPlaybackState.None);
            throw;
        }
    }

    /// <summary>
    /// Starts or resumes playback.
    /// </summary>
    public void Play()
    {
        try
        {
            _mediaPlayer?.Play();
            SetPlaybackState(AudioPlaybackState.Playing);
            StartPositionTimer();
            _logger.LogInformation("Playback started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting playback");
        }
    }

    /// <summary>
    /// Pauses playback.
    /// </summary>
    public void Pause()
    {
        try
        {
            _mediaPlayer?.Pause();
            SetPlaybackState(AudioPlaybackState.Paused);
            StopPositionTimer();
            _logger.LogInformation("Playback paused");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing playback");
        }
    }

    /// <summary>
    /// Stops playback and resets position.
    /// </summary>
    public void Stop()
    {
        try
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Pause();
                _mediaPlayer.PlaybackSession.Position = TimeSpan.Zero;
            }
            SetPlaybackState(AudioPlaybackState.Stopped);
            StopPositionTimer();
            _logger.LogInformation("Playback stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping playback");
        }
    }

    /// <summary>
    /// Seeks to a specific position in seconds.
    /// </summary>
    public void Seek(double seconds)
    {
        try
        {
            if (_mediaPlayer?.PlaybackSession != null)
            {
                _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(seconds);
                _logger.LogDebug("Seeked to position: {Position}s", seconds);
                RaisePositionChanged();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeking playback position");
        }
    }

    /// <summary>
    /// Gets the current playback position in seconds.
    /// </summary>
    public double GetPosition()
    {
        return _mediaPlayer?.PlaybackSession?.Position.TotalSeconds ?? 0.0;
    }

    /// <summary>
    /// Gets the total duration in seconds.
    /// </summary>
    public double GetDuration()
    {
        return _mediaPlayer?.PlaybackSession?.NaturalDuration.TotalSeconds ?? 0.0;
    }

    /// <summary>
    /// Gets the current playback state.
    /// </summary>
    public AudioPlaybackState GetPlaybackState()
    {
        return _playbackState;
    }

    #region Private Methods

    private void InitializeMediaPlayer()
    {
        _mediaPlayer = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
            Volume = 1.0
        };

        // Subscribe to MediaPlayer events
        _mediaPlayer.MediaOpened += OnMediaOpened;
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.MediaFailed += OnMediaFailed;
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        _logger.LogInformation("Media opened. Duration: {Duration}s", GetDuration());
        SetPlaybackState(AudioPlaybackState.Stopped);
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        _logger.LogInformation("Media playback ended");
        SetPlaybackState(AudioPlaybackState.Stopped);
        StopPositionTimer();
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _logger.LogError("Media playback failed: {Error}", args.ErrorMessage);
        SetPlaybackState(AudioPlaybackState.None);
        StopPositionTimer();
    }

    private void SetPlaybackState(AudioPlaybackState newState)
    {
        var oldState = _playbackState;
        _playbackState = newState;

        PlaybackStateChanged?.Invoke(this, new AudioPlaybackStateChangedEventArgs
        {
            OldState = oldState,
            NewState = newState
        });
    }

    private void StartPositionTimer()
    {
        _positionTimer?.Dispose();
        _positionTimer = new System.Threading.Timer(
            _ => RaisePositionChanged(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(100)); // Update position 10 times per second
    }

    private void StopPositionTimer()
    {
        _positionTimer?.Dispose();
        _positionTimer = null;
    }

    private void RaisePositionChanged()
    {
        PositionChanged?.Invoke(this, new AudioPositionChangedEventArgs
        {
            Position = GetPosition(),
            Duration = GetDuration()
        });
    }

    #endregion

    public void Dispose()
    {
        StopPositionTimer();
        if (_mediaPlayer != null)
        {
            _mediaPlayer.MediaOpened -= OnMediaOpened;
            _mediaPlayer.MediaEnded -= OnMediaEnded;
            _mediaPlayer.MediaFailed -= OnMediaFailed;
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }
        GC.SuppressFinalize(this);
    }
}
