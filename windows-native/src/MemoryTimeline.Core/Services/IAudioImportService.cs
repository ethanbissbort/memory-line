using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using Windows.Storage;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for importing audio files and auto-processing.
/// </summary>
public interface IAudioImportService
{
    /// <summary>
    /// Import audio files from specified paths.
    /// </summary>
    Task<AudioImportResult> ImportFilesAsync(
        IEnumerable<string> filePaths,
        AudioImportOptions? options = null,
        IProgress<AudioImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scan a directory for audio files.
    /// </summary>
    Task<List<AudioImportItem>> ScanDirectoryAsync(
        string directoryPath,
        AudioImportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Import audio files from a directory.
    /// </summary>
    Task<AudioImportResult> ImportDirectoryAsync(
        string directoryPath,
        AudioImportOptions? options = null,
        IProgress<AudioImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a file is a supported audio format.
    /// </summary>
    bool IsSupportedFormat(string filePath, AudioImportOptions? options = null);

    /// <summary>
    /// Get audio file metadata.
    /// </summary>
    Task<AudioImportItem> GetFileInfoAsync(string filePath);

    /// <summary>
    /// Event raised when import progress changes.
    /// </summary>
    event EventHandler<AudioImportProgress>? ImportProgressChanged;
}

/// <summary>
/// Audio import service implementation.
/// </summary>
public class AudioImportService : IAudioImportService
{
    private readonly AppDbContext _dbContext;
    private readonly IQueueService _queueService;
    private readonly ISpeechToTextService? _sttService;
    private readonly IEventExtractionService? _extractionService;
    private readonly ILogger<AudioImportService> _logger;

    public event EventHandler<AudioImportProgress>? ImportProgressChanged;

    public AudioImportService(
        AppDbContext dbContext,
        IQueueService queueService,
        ILogger<AudioImportService> logger,
        ISpeechToTextService? sttService = null,
        IEventExtractionService? extractionService = null)
    {
        _dbContext = dbContext;
        _queueService = queueService;
        _sttService = sttService;
        _extractionService = extractionService;
        _logger = logger;
    }

    public async Task<AudioImportResult> ImportFilesAsync(
        IEnumerable<string> filePaths,
        AudioImportOptions? options = null,
        IProgress<AudioImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AudioImportOptions();
        var result = new AudioImportResult();
        var files = filePaths.ToList();
        result.TotalFiles = files.Count;

        _logger.LogInformation("Starting import of {Count} audio files", files.Count);

        for (int i = 0; i < files.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var filePath = files[i];
            var progressReport = new AudioImportProgress
            {
                CurrentItem = i + 1,
                TotalItems = files.Count,
                CurrentFileName = Path.GetFileName(filePath),
                OverallProgress = ((double)i / files.Count) * 100
            };

            try
            {
                // Check format
                if (!IsSupportedFormat(filePath, options))
                {
                    var skipped = new AudioImportItem
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        Status = AudioImportStatus.Skipped,
                        ErrorMessage = "Unsupported format"
                    };
                    result.Items.Add(skipped);
                    result.SkippedDuplicates++;
                    continue;
                }

                // Get file info
                progressReport.CurrentStatus = AudioImportStatus.Copying;
                progressReport.StatusMessage = "Reading file info...";
                ReportProgress(progress, progressReport);

                var importItem = await GetFileInfoAsync(filePath);

                // Check for duplicates
                if (options.SkipDuplicates)
                {
                    var isDuplicate = await CheckForDuplicateAsync(importItem);
                    if (isDuplicate)
                    {
                        importItem.Status = AudioImportStatus.Skipped;
                        importItem.ErrorMessage = "Duplicate file";
                        result.Items.Add(importItem);
                        result.SkippedDuplicates++;
                        continue;
                    }
                }

                // Check file size
                if (importItem.FileSize > options.MaxFileSizeBytes)
                {
                    importItem.Status = AudioImportStatus.Skipped;
                    importItem.ErrorMessage = $"File too large (max {options.MaxFileSizeBytes / 1024 / 1024}MB)";
                    result.Items.Add(importItem);
                    result.SkippedDuplicates++;
                    continue;
                }

                // Copy file to app data folder
                progressReport.StatusMessage = "Copying file...";
                progressReport.CurrentItemProgress = 25;
                ReportProgress(progress, progressReport);

                var destinationPath = await CopyToAppDataAsync(filePath, cancellationToken);

                // Add to queue
                progressReport.CurrentStatus = AudioImportStatus.Queued;
                progressReport.StatusMessage = "Adding to queue...";
                progressReport.CurrentItemProgress = 50;
                ReportProgress(progress, progressReport);

                var queueItem = await AddToQueueAsync(destinationPath, importItem);
                importItem.QueueItemId = queueItem.QueueId;
                importItem.Status = AudioImportStatus.Queued;

                // Auto-process if enabled
                if (options.AutoProcess && _sttService != null)
                {
                    progressReport.CurrentStatus = AudioImportStatus.Transcribing;
                    progressReport.StatusMessage = "Transcribing...";
                    progressReport.CurrentItemProgress = 60;
                    ReportProgress(progress, progressReport);

                    try
                    {
                        await ProcessQueueItemAsync(queueItem, options, progress, progressReport);
                        importItem.Status = AudioImportStatus.Completed;
                        result.QueuedForProcessing++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Auto-processing failed for {File}, will remain in queue", importItem.FileName);
                        importItem.Status = AudioImportStatus.Queued;
                    }
                }
                else
                {
                    result.QueuedForProcessing++;
                }

                // Delete source if requested
                if (options.DeleteSourceAfterImport && importItem.Status == AudioImportStatus.Completed)
                {
                    try
                    {
                        File.Delete(filePath);
                        _logger.LogInformation("Deleted source file: {Path}", filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete source file: {Path}", filePath);
                    }
                }

                result.Items.Add(importItem);
                result.SuccessfulImports++;

                progressReport.CurrentItemProgress = 100;
                progressReport.StatusMessage = "Complete";
                ReportProgress(progress, progressReport);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing file: {Path}", filePath);

                var failed = new AudioImportItem
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Status = AudioImportStatus.Failed,
                    ErrorMessage = ex.Message
                };
                result.Items.Add(failed);
                result.FailedImports++;
                result.Errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        _logger.LogInformation(
            "Import complete: {Success} successful, {Failed} failed, {Skipped} skipped",
            result.SuccessfulImports, result.FailedImports, result.SkippedDuplicates);

        return result;
    }

    public async Task<List<AudioImportItem>> ScanDirectoryAsync(
        string directoryPath,
        AudioImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AudioImportOptions();
        var items = new List<AudioImportItem>();

        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Directory not found: {Path}", directoryPath);
            return items;
        }

        var searchOption = options.RecursiveScan ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var format in options.SupportedFormats)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var files = Directory.GetFiles(directoryPath, $"*{format}", searchOption);
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var item = await GetFileInfoAsync(file);
                    items.Add(item);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error scanning file: {Path}", file);
                }
            }
        }

        _logger.LogInformation("Found {Count} audio files in {Path}", items.Count, directoryPath);
        return items.OrderBy(i => i.FileCreatedAt).ToList();
    }

    public async Task<AudioImportResult> ImportDirectoryAsync(
        string directoryPath,
        AudioImportOptions? options = null,
        IProgress<AudioImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var items = await ScanDirectoryAsync(directoryPath, options, cancellationToken);
        var filePaths = items.Select(i => i.FilePath);
        return await ImportFilesAsync(filePaths, options, progress, cancellationToken);
    }

    public bool IsSupportedFormat(string filePath, AudioImportOptions? options = null)
    {
        options ??= new AudioImportOptions();
        var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
        return !string.IsNullOrEmpty(extension) && options.SupportedFormats.Contains(extension);
    }

    public async Task<AudioImportItem> GetFileInfoAsync(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var item = new AudioImportItem
        {
            FilePath = filePath,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length,
            FileCreatedAt = fileInfo.CreationTimeUtc,
            Format = fileInfo.Extension.ToLowerInvariant()
        };

        // Try to get audio duration using Windows APIs
        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            var properties = await storageFile.Properties.GetMusicPropertiesAsync();
            item.Duration = properties.Duration;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get audio duration for: {Path}", filePath);
        }

        return item;
    }

    private async Task<bool> CheckForDuplicateAsync(AudioImportItem item)
    {
        // Check by file path containing filename and size in queue
        var fileName = item.FileName;
        var existingInQueue = await Task.Run(() =>
            _dbContext.RecordingQueues.Any(q =>
                q.AudioFilePath.Contains(fileName) &&
                q.FileSizeBytes == item.FileSize));

        return existingInQueue;
    }

    private async Task<string> CopyToAppDataAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var appDataFolder = ApplicationData.Current.LocalFolder;
        var recordingsFolder = await appDataFolder.CreateFolderAsync("recordings", CreationCollisionOption.OpenIfExists);

        var fileName = Path.GetFileName(sourcePath);
        var uniqueName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{fileName}";

        var destinationFile = await recordingsFolder.CreateFileAsync(uniqueName, CreationCollisionOption.GenerateUniqueName);

        using var sourceStream = File.OpenRead(sourcePath);
        using var destStream = await destinationFile.OpenStreamForWriteAsync();
        await sourceStream.CopyToAsync(destStream, cancellationToken);

        _logger.LogInformation("Copied audio file to: {Path}", destinationFile.Path);
        return destinationFile.Path;
    }

    private async Task<RecordingQueue> AddToQueueAsync(string filePath, AudioImportItem item)
    {
        var queueItem = new RecordingQueue
        {
            QueueId = Guid.NewGuid().ToString(),
            AudioFilePath = filePath,
            FileSizeBytes = item.FileSize,
            DurationSeconds = item.Duration?.TotalSeconds,
            Status = QueueStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.RecordingQueues.Add(queueItem);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Added to queue: {Id} - {FileName}", queueItem.QueueId, Path.GetFileName(queueItem.AudioFilePath));
        return queueItem;
    }

    private async Task ProcessQueueItemAsync(
        RecordingQueue queueItem,
        AudioImportOptions options,
        IProgress<AudioImportProgress>? progress,
        AudioImportProgress progressReport)
    {
        if (_sttService == null)
            return;

        var fileName = Path.GetFileName(queueItem.AudioFilePath);

        // Update status to processing
        queueItem.Status = QueueStatus.Processing;
        await _dbContext.SaveChangesAsync();

        try
        {
            // Transcribe
            _logger.LogInformation("Transcribing: {FileName}", fileName);
            var transcriptionResult = await _sttService.TranscribeAsync(queueItem.AudioFilePath);

            if (!transcriptionResult.Success || string.IsNullOrWhiteSpace(transcriptionResult.Text))
            {
                throw new InvalidOperationException(transcriptionResult.ErrorMessage ?? "Transcription returned empty result");
            }

            var transcript = transcriptionResult.Text;

            // Extract events if enabled - use the extraction service which handles creating pending events
            if (options.AutoExtract && _extractionService != null)
            {
                progressReport.CurrentStatus = AudioImportStatus.Extracting;
                progressReport.StatusMessage = "Extracting events...";
                progressReport.CurrentItemProgress = 80;
                ReportProgress(progress, progressReport);

                _logger.LogInformation("Extracting events from: {FileName}", fileName);
                var pendingEvents = await _extractionService.ExtractAndCreatePendingEventsAsync(queueItem.QueueId, transcript);
                _logger.LogInformation("Extracted {Count} events from: {FileName}", pendingEvents.Count, fileName);
            }

            queueItem.Status = QueueStatus.Completed;
            queueItem.ProcessedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing queue item: {Id}", queueItem.QueueId);
            queueItem.Status = QueueStatus.Failed;
            queueItem.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private void ReportProgress(IProgress<AudioImportProgress>? progress, AudioImportProgress report)
    {
        progress?.Report(report);
        ImportProgressChanged?.Invoke(this, report);
    }
}
