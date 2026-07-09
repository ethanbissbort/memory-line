using System.Text;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using Whisper.net;
using Whisper.net.Ggml;

namespace MemoryTimeline.Services;

/// <summary>
/// File-based speech-to-text service backed by Whisper.net (local ggml Whisper model).
/// <para>
/// The recorder produces 16 kHz / 16-bit / mono WAV files, which is exactly the input
/// Whisper.net expects, so recordings can be streamed straight into the processor.
/// </para>
/// <para>
/// The ggml "base" model is downloaded on first use into
/// %LocalAppData%\MemoryTimeline\Models\ggml-base.bin (no package identity required —
/// the app runs unpackaged). The expensive <see cref="WhisperFactory"/> (model load)
/// is created once and cached; a fresh, cheap <see cref="WhisperProcessor"/> is built
/// per transcription, so this service is safe to register as a DI singleton.
/// </para>
/// </summary>
public class WhisperSpeechToTextService : ISpeechToTextService, IDisposable
{
    private readonly ILogger<WhisperSpeechToTextService> _logger;
    private readonly string _modelPath;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private WhisperFactory? _factory;
    private bool _disposed;

    public string EngineName => "Whisper (local, Whisper.net)";
    public bool SupportsStreaming => false;
    public bool RequiresInternet => false; // offline after the one-time model download

    public WhisperSpeechToTextService(ILogger<WhisperSpeechToTextService> logger)
    {
        _logger = logger;
        _modelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MemoryTimeline",
            "Models",
            "ggml-base.bin");
    }

    /// <summary>
    /// Transcribes an audio file to text.
    /// </summary>
    public Task<TranscriptionResult> TranscribeAsync(string audioFilePath)
    {
        return TranscribeAsync(audioFilePath, null);
    }

    /// <summary>
    /// Transcribes an audio file to text with progress reporting.
    /// Never throws — all failures are returned as an unsuccessful
    /// <see cref="TranscriptionResult"/> with a user-readable message.
    /// </summary>
    public async Task<TranscriptionResult> TranscribeAsync(string audioFilePath, IProgress<double>? progress)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = new TranscriptionResult();

        try
        {
            progress?.Report(0.0);

            if (string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath))
            {
                result.Success = false;
                result.ErrorMessage = $"Audio file not found: {audioFilePath}";
                result.ProcessingDurationSeconds = stopwatch.Elapsed.TotalSeconds;
                _logger.LogWarning("Whisper transcription requested for missing file: {FilePath}", audioFilePath);
                return result;
            }

            // Ensure the model is present and the factory is loaded (one-time cost).
            var factory = await GetOrCreateFactoryAsync();
            progress?.Report(0.1);

            _logger.LogDebug("Starting Whisper transcription of {FilePath}", audioFilePath);

            // The factory is cached, but processors are cheap and NOT reused across
            // calls, so concurrent transcriptions each get their own processor.
            await using var processor = factory.CreateBuilder()
                .WithLanguage("auto")
                .Build();

            await using var fileStream = File.OpenRead(audioFilePath);

            // Estimate the audio duration from the file size (16 kHz * 16-bit * mono
            // = 32,000 bytes/sec after the 44-byte WAV header) to drive progress.
            var estimatedAudioSeconds = Math.Max((fileStream.Length - 44) / 32000.0, 0.1);

            var textBuilder = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(fileStream, CancellationToken.None))
            {
                textBuilder.Append(segment.Text);

                if (progress != null)
                {
                    var fraction = 0.1 + 0.85 * Math.Min(segment.End.TotalSeconds / estimatedAudioSeconds, 1.0);
                    progress.Report(Math.Min(fraction, 0.95));
                }
            }

            stopwatch.Stop();

            result.Text = textBuilder.ToString().Trim();
            result.Success = true;
            result.ProcessingDurationSeconds = stopwatch.Elapsed.TotalSeconds;
            progress?.Report(1.0);

            _logger.LogDebug(
                "Whisper transcription completed in {Duration:F1}s, {Length} characters",
                result.ProcessingDurationSeconds, result.Text.Length);

            return result;
        }
        catch (ModelDownloadFailedException ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ProcessingDurationSeconds = stopwatch.Elapsed.TotalSeconds;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Whisper transcription of {FilePath}", audioFilePath);

            stopwatch.Stop();
            result.Success = false;
            result.ErrorMessage = $"Transcription failed: {ex.Message}";
            result.ProcessingDurationSeconds = stopwatch.Elapsed.TotalSeconds;
            return result;
        }
    }

    /// <summary>
    /// Returns the cached <see cref="WhisperFactory"/>, downloading the ggml model and
    /// loading it on first use. Serialized with a semaphore so concurrent transcriptions
    /// never double-download or double-load the model.
    /// </summary>
    private async Task<WhisperFactory> GetOrCreateFactoryAsync()
    {
        if (_factory != null)
        {
            return _factory;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_factory != null)
            {
                return _factory;
            }

            ObjectDisposedException.ThrowIf(_disposed, this);

            await EnsureModelDownloadedAsync();

            _logger.LogInformation("Loading Whisper model from {ModelPath}", _modelPath);
            _factory = WhisperFactory.FromPath(_modelPath);
            _logger.LogInformation("Whisper model loaded and cached");

            return _factory;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Downloads the ggml base model to local app data if it is not already present.
    /// Must be called while holding <see cref="_initLock"/>.
    /// </summary>
    private async Task EnsureModelDownloadedAsync()
    {
        if (File.Exists(_modelPath))
        {
            return;
        }

        _logger.LogInformation("Whisper model not found; downloading ggml base model to {ModelPath}", _modelPath);

        var tempPath = _modelPath + ".download";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);

            await using (var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.Base))
            await using (var fileStream = File.Create(tempPath))
            {
                await modelStream.CopyToAsync(fileStream);
            }

            // Write to a temp file first, then move into place, so a failed/partial
            // download is never mistaken for a valid model on the next run.
            File.Move(tempPath, _modelPath, overwrite: true);

            _logger.LogInformation("Whisper model downloaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download the Whisper speech model");

            TryDeleteFile(tempPath);

            throw new ModelDownloadFailedException(
                "The Whisper speech model could not be downloaded. Check your internet " +
                "connection and try again — the model is fetched once and then cached locally.",
                ex);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete partial model download at {Path}", path);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _factory?.Dispose();
        _factory = null;
        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Internal signal that the one-time model download failed; converted to a clean
    /// failure <see cref="TranscriptionResult"/> — never surfaced to callers as a throw.
    /// </summary>
    private sealed class ModelDownloadFailedException : Exception
    {
        public ModelDownloadFailedException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
