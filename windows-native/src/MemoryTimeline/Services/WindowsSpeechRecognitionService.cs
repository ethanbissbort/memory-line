using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;

namespace MemoryTimeline.Services;

/// <summary>
/// Fallback speech-to-text service stub for Windows Speech Recognition.
/// <para>
/// <see cref="Windows.Media.SpeechRecognition.SpeechRecognizer"/> can only recognize
/// speech from the LIVE microphone — it has no API to transcribe an audio file.
/// The previous implementation loaded the WAV file, ignored it, and then opened the
/// microphone, "transcribing" ambient room audio instead of the recording.
/// </para>
/// <para>
/// This class is now an honest fallback: it fails fast without ever opening the
/// microphone. File-based transcription is provided by
/// <see cref="WhisperSpeechToTextService"/>, which is the registered
/// <see cref="ISpeechToTextService"/>.
/// </para>
/// </summary>
public class WindowsSpeechRecognitionService : ISpeechToTextService, IDisposable
{
    private const string NotSupportedMessage =
        "Windows speech recognition cannot transcribe audio files — the Whisper engine is used instead";

    private readonly ILogger<WindowsSpeechRecognitionService> _logger;

    public string EngineName => "Windows Speech Recognition";
    public bool SupportsStreaming => false;
    public bool RequiresInternet => false;

    public WindowsSpeechRecognitionService(ILogger<WindowsSpeechRecognitionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Transcribes an audio file to text. Not supported by this engine.
    /// </summary>
    public Task<TranscriptionResult> TranscribeAsync(string audioFilePath)
    {
        return TranscribeAsync(audioFilePath, null);
    }

    /// <summary>
    /// Transcribes an audio file to text with progress reporting. Not supported by
    /// this engine — returns a failure result without opening the microphone.
    /// </summary>
    public Task<TranscriptionResult> TranscribeAsync(string audioFilePath, IProgress<double>? progress)
    {
        _logger.LogWarning(
            "Windows Speech Recognition cannot transcribe audio files (requested: {FilePath}). " +
            "Returning failure result; the Whisper engine handles file transcription.",
            audioFilePath);

        progress?.Report(1.0);

        return Task.FromResult(new TranscriptionResult
        {
            Success = false,
            ErrorMessage = NotSupportedMessage
        });
    }

    public void Dispose()
    {
        // Nothing to dispose — this stub never acquires the microphone or any
        // native recognizer resources. Kept for compatibility with existing
        // DI registrations that expect IDisposable.
        GC.SuppressFinalize(this);
    }
}
