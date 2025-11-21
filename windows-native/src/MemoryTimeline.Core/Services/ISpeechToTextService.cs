namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for speech-to-text conversion.
/// </summary>
public interface ISpeechToTextService
{
    Task<string> TranscribeAsync(string audioFilePath);
}

/// <summary>
/// Speech-to-text service supporting multiple engines.
/// </summary>
public class SpeechToTextService : ISpeechToTextService
{
    public Task<string> TranscribeAsync(string audioFilePath)
    {
        // TODO: Implement with Windows Speech Recognition or ONNX Whisper
        throw new NotImplementedException();
    }
}
