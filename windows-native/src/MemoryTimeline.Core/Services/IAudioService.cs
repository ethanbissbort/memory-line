namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for audio recording and playback.
/// </summary>
public interface IAudioService
{
    Task StartRecordingAsync();
    Task PauseRecordingAsync();
    Task StopRecordingAsync();
    Task<string> SaveRecordingAsync();
}

/// <summary>
/// Audio service implementation using Windows MediaCapture API.
/// </summary>
public class AudioService : IAudioService
{
    public Task StartRecordingAsync()
    {
        // TODO: Implement using Windows.Media.Capture.MediaCapture
        throw new NotImplementedException();
    }

    public Task PauseRecordingAsync()
    {
        // TODO: Implement
        throw new NotImplementedException();
    }

    public Task StopRecordingAsync()
    {
        // TODO: Implement
        throw new NotImplementedException();
    }

    public Task<string> SaveRecordingAsync()
    {
        // TODO: Implement
        throw new NotImplementedException();
    }
}
