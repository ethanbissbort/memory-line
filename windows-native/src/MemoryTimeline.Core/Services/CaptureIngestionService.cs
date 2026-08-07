using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Capture ingestion implementation. Registered as a SINGLETON: all dependencies
/// are stateless over the DbContext factory.
/// </summary>
public class CaptureIngestionService : ICaptureIngestionService
{
    private readonly ICaptureRepository _captureRepository;
    private readonly IRecordingQueueRepository _queueRepository;
    private readonly IArtifactResolver _artifactResolver;
    private readonly ILogger<CaptureIngestionService> _logger;

    public CaptureIngestionService(
        ICaptureRepository captureRepository,
        IRecordingQueueRepository queueRepository,
        IArtifactResolver artifactResolver,
        ILogger<CaptureIngestionService> logger)
    {
        _captureRepository = captureRepository;
        _queueRepository = queueRepository;
        _artifactResolver = artifactResolver;
        _logger = logger;
    }

    public async Task<IngestionResult> IngestLocalRecordingAsync(
        AudioRecordingDto recording,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(recording.AudioFilePath))
        {
            return IngestionResult.Failed(IngestionFailureKind.InvalidRequest, "Recording has no audio file path.");
        }

        var queueId = string.IsNullOrWhiteSpace(recording.QueueId)
            ? Guid.NewGuid().ToString()
            : recording.QueueId;

        // Idempotent replay: the recording service pre-generates the queue ID, so
        // a retried hand-off must not create a second queue item.
        var existing = await _queueRepository.GetByIdAsync(queueId);
        if (existing != null)
        {
            _logger.LogInformation("Recording {QueueId} already ingested; returning existing item", queueId);
            return IngestionResult.Duplicate(
                existing.SourceCaptureId ?? string.Empty, existing.AudioArtifactId, existing.QueueId);
        }

        if (!File.Exists(recording.AudioFilePath))
        {
            return IngestionResult.Failed(
                IngestionFailureKind.ArtifactUnavailable,
                $"Audio file not found: {recording.AudioFilePath}");
        }

        string sha256;
        long byteLength;
        try
        {
            (sha256, byteLength) = await ComputeSha256Async(recording.AudioFilePath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hash recording {QueueId}", queueId);
            return IngestionResult.Failed(
                IngestionFailureKind.ArtifactUnavailable, $"Could not read audio file: {ex.Message}");
        }

        // Content-hash idempotency (§7.2, §12.2): identical bytes are the same
        // recording regardless of path or queue ID (e.g. the same file imported
        // twice under different copy names).
        var sameContent = await _captureRepository.GetQueueItemByContentSha256Async(sha256);
        if (sameContent != null)
        {
            _logger.LogInformation(
                "Recording {QueueId} matches content of existing queue item {ExistingQueueId}; reusing",
                queueId, sameContent.QueueId);
            return IngestionResult.Duplicate(
                sameContent.SourceCaptureId ?? string.Empty, sameContent.AudioArtifactId, sameContent.QueueId);
        }

        var now = DateTime.UtcNow;
        var capture = new Capture
        {
            CaptureId = Guid.NewGuid().ToString(),
            SourcePlatform = CapturePlatform.Windows,
            CaptureType = CaptureTypes.Unclassified,
            CreatedAtUtc = now,
            CapturedAtUtc = recording.CreatedAt == default ? now : recording.CreatedAt,
            Status = CaptureStatus.Received,
        };

        var artifact = new CaptureArtifact
        {
            ArtifactId = Guid.NewGuid().ToString(),
            CaptureId = capture.CaptureId,
            ArtifactType = CaptureArtifactType.AudioOriginal,
            StorageLocator = recording.AudioFilePath,
            MediaType = MediaTypeFromPath(recording.AudioFilePath),
            ByteLength = byteLength,
            Sha256 = sha256,
            CreatedAtUtc = now,
            UploadState = CaptureArtifactUploadState.Local,
        };

        var queueItem = new RecordingQueue
        {
            QueueId = queueId,
            AudioFilePath = recording.AudioFilePath,
            Status = QueueStatus.Pending,
            ProcessingStage = QueueProcessingStage.ReadyForTranscription,
            DurationSeconds = recording.DurationSeconds,
            FileSizeBytes = byteLength,
            CreatedAt = capture.CapturedAtUtc,
            SourceCaptureId = capture.CaptureId,
            SourcePlatform = CapturePlatform.Windows,
            AudioArtifactId = artifact.ArtifactId,
            OriginalFileName = Path.GetFileName(recording.AudioFilePath),
            ContentSha256 = sha256,
            SyncState = QueueSyncState.LocalOnly,
            ReceivedAt = now,
        };

        return await PersistAsync(capture, artifact, queueItem);
    }

    public async Task<IngestionResult> IngestRemoteCaptureAsync(
        RemoteCaptureEnvelope capture,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validationError = ValidateEnvelope(capture);
        if (validationError != null)
        {
            return IngestionResult.Failed(IngestionFailureKind.InvalidRequest, validationError, capture.CaptureId);
        }

        // Idempotency: at most one queue item per capture ID, ever. Repeated
        // uploads and background-transfer retries land here.
        var existing = await _captureRepository.GetQueueItemBySourceCaptureIdAsync(capture.CaptureId);
        if (existing != null)
        {
            // Same ID with different content is a conflict, not a replay.
            if (existing.ContentSha256 != null &&
                !string.Equals(existing.ContentSha256, capture.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return IngestionResult.Failed(
                    IngestionFailureKind.ArtifactInvalid,
                    "Capture ID was already ingested with different content.",
                    capture.CaptureId);
            }

            _logger.LogInformation("Capture {CaptureId} already ingested as queue item {QueueId}",
                capture.CaptureId, existing.QueueId);
            return IngestionResult.Duplicate(capture.CaptureId, existing.AudioArtifactId, existing.QueueId);
        }

        // A capture row without a queue item means the queue item was deleted by
        // the user after a previous ingestion (queue removal does not delete the
        // capture). Deletions are never silently resurrected by a re-upload
        // (§12.2): acknowledge the duplicate without creating rows, instead of
        // colliding on the captures primary key forever.
        var priorCapture = await _captureRepository.GetByIdAsync(capture.CaptureId);
        if (priorCapture != null)
        {
            var priorAudio = priorCapture.Artifacts
                .FirstOrDefault(a => a.ArtifactType == CaptureArtifactType.AudioOriginal);
            if (priorAudio?.Sha256 != null &&
                !string.Equals(priorAudio.Sha256, capture.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return IngestionResult.Failed(
                    IngestionFailureKind.ArtifactInvalid,
                    "Capture ID was already ingested with different content.",
                    capture.CaptureId);
            }

            _logger.LogInformation(
                "Capture {CaptureId} was previously ingested and its queue item since removed; not resurrecting",
                capture.CaptureId);
            return IngestionResult.Duplicate(capture.CaptureId, priorAudio?.ArtifactId, null);
        }

        var resolution = await _artifactResolver.ResolveAsync(capture, cancellationToken);
        if (!resolution.Success || string.IsNullOrEmpty(resolution.LocalPath))
        {
            return IngestionResult.Failed(
                IngestionFailureKind.ArtifactUnavailable,
                resolution.Error ?? "Artifact could not be resolved.",
                capture.CaptureId);
        }

        string sha256;
        long byteLength;
        try
        {
            (sha256, byteLength) = await ComputeSha256Async(resolution.LocalPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hash artifact for capture {CaptureId}", capture.CaptureId);
            return IngestionResult.Failed(
                IngestionFailureKind.ArtifactUnavailable,
                $"Could not read artifact file: {ex.Message}",
                capture.CaptureId);
        }

        if (byteLength != capture.ExpectedByteLength)
        {
            return IngestionResult.Failed(
                IngestionFailureKind.ArtifactInvalid,
                $"Artifact byte length {byteLength} does not match declared {capture.ExpectedByteLength}.",
                capture.CaptureId);
        }

        if (!string.Equals(sha256, capture.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return IngestionResult.Failed(
                IngestionFailureKind.ArtifactInvalid,
                "Artifact SHA-256 does not match the declared hash.",
                capture.CaptureId);
        }

        // Content-hash idempotency (§7.2, §12.2): the same audio bytes under a
        // freshly minted capture ID (e.g. an app reinstall regenerating IDs for
        // unacknowledged recordings) must not produce a second queue item and
        // duplicate pending events.
        var sameContent = await _captureRepository.GetQueueItemByContentSha256Async(sha256);
        if (sameContent != null)
        {
            _logger.LogInformation(
                "Capture {CaptureId} matches content of existing queue item {QueueId}; reusing",
                capture.CaptureId, sameContent.QueueId);
            return IngestionResult.Duplicate(
                sameContent.SourceCaptureId ?? capture.CaptureId, sameContent.AudioArtifactId, sameContent.QueueId);
        }

        var now = DateTime.UtcNow;
        var captureEntity = new Capture
        {
            CaptureId = capture.CaptureId,
            SourceDeviceId = capture.SourceDeviceId,
            SourcePlatform = string.IsNullOrWhiteSpace(capture.SourcePlatform)
                ? CapturePlatform.Other
                : capture.SourcePlatform,
            CaptureType = string.IsNullOrWhiteSpace(capture.CaptureType)
                ? CaptureTypes.Unclassified
                : capture.CaptureType,
            CreatedAtUtc = now,
            CapturedAtUtc = capture.CapturedAtUtc == default ? now : capture.CapturedAtUtc,
            TimezoneId = capture.TimezoneId,
            LocalOffsetMinutes = capture.LocalOffsetMinutes,
            Status = CaptureStatus.Received,
            TitleHint = capture.TitleHint,
            UserNote = capture.UserNote,
            TripId = capture.TripId,
            ClientSchemaVersion = capture.ClientSchemaVersion,
        };

        var artifact = new CaptureArtifact
        {
            ArtifactId = string.IsNullOrWhiteSpace(capture.ArtifactId)
                ? Guid.NewGuid().ToString()
                : capture.ArtifactId,
            CaptureId = captureEntity.CaptureId,
            ArtifactType = CaptureArtifactType.AudioOriginal,
            StorageLocator = resolution.LocalPath,
            MediaType = capture.MediaType ?? MediaTypeFromPath(resolution.LocalPath),
            ByteLength = byteLength,
            Sha256 = sha256,
            CreatedAtUtc = now,
            UploadState = CaptureArtifactUploadState.Downloaded,
        };

        var queueItem = new RecordingQueue
        {
            QueueId = Guid.NewGuid().ToString(),
            AudioFilePath = resolution.LocalPath,
            Status = QueueStatus.Pending,
            ProcessingStage = QueueProcessingStage.ReadyForTranscription,
            FileSizeBytes = byteLength,
            CreatedAt = now,
            SourceCaptureId = captureEntity.CaptureId,
            SourceDeviceId = capture.SourceDeviceId,
            SourcePlatform = captureEntity.SourcePlatform,
            AudioArtifactId = artifact.ArtifactId,
            OriginalFileName = capture.OriginalFileName ?? Path.GetFileName(resolution.LocalPath),
            ContentSha256 = sha256,
            SyncState = QueueSyncState.Received,
            ReceivedAt = now,
        };

        return await PersistAsync(captureEntity, artifact, queueItem);
    }

    #region Private Methods

    private async Task<IngestionResult> PersistAsync(
        Capture capture,
        CaptureArtifact artifact,
        RecordingQueue queueItem)
    {
        var outboxEntry = new SyncOutbox
        {
            EntityType = SyncEntityType.Capture,
            EntityId = capture.CaptureId,
            Operation = SyncOutboxOperation.Upsert,
            // Metadata only — never transcripts, notes, audio bytes, or location
            // (privacy rule §14.5).
            PayloadJson = JsonSerializer.Serialize(new
            {
                captureId = capture.CaptureId,
                queueId = queueItem.QueueId,
                artifactId = artifact.ArtifactId,
                sha256 = artifact.Sha256,
                byteLength = artifact.ByteLength,
                capturedAtUtc = capture.CapturedAtUtc,
                sourcePlatform = capture.SourcePlatform,
                status = capture.Status,
            }),
            CreatedAt = DateTime.UtcNow,
        };

        try
        {
            await _captureRepository.IngestAsync(capture, artifact, queueItem, outboxEntry);
        }
        catch (DbUpdateException ex)
        {
            // A constraint conflict here means another row already explains this
            // capture; prefer the idempotent-duplicate outcome over a failure.

            // Remote-path race: both racers share the capture ID, so the loser
            // hits the source_capture_id unique index.
            var winner = await _captureRepository.GetQueueItemBySourceCaptureIdAsync(capture.CaptureId);
            if (winner != null)
            {
                _logger.LogInformation(
                    "Concurrent ingestion of capture {CaptureId} detected; returning existing queue item {QueueId}",
                    capture.CaptureId, winner.QueueId);
                return IngestionResult.Duplicate(capture.CaptureId, winner.AudioArtifactId, winner.QueueId);
            }

            // Local-path race: both racers share the pre-generated queue ID but
            // mint distinct capture IDs, so the conflict is the queue primary key.
            var byQueueId = await _queueRepository.GetByIdAsync(queueItem.QueueId);
            if (byQueueId != null)
            {
                _logger.LogInformation(
                    "Concurrent ingestion of recording {QueueId} detected; returning existing queue item",
                    queueItem.QueueId);
                return IngestionResult.Duplicate(
                    byQueueId.SourceCaptureId ?? capture.CaptureId, byQueueId.AudioArtifactId, byQueueId.QueueId);
            }

            // Orphaned capture row (queue item deleted between the pre-check and
            // this insert): the captures primary key rejected the re-insert.
            // Deletions are never resurrected (§12.2).
            if (await _captureRepository.ExistsAsync(capture.CaptureId))
            {
                _logger.LogInformation(
                    "Capture {CaptureId} already exists without a queue item; not resurrecting", capture.CaptureId);
                return IngestionResult.Duplicate(capture.CaptureId, null, null);
            }

            _logger.LogError(ex, "Failed to persist ingestion of capture {CaptureId}", capture.CaptureId);
            return IngestionResult.Failed(
                IngestionFailureKind.StorageFailure, $"Could not persist capture: {ex.Message}", capture.CaptureId);
        }

        _logger.LogInformation(
            "Ingested capture {CaptureId} ({Platform}) as queue item {QueueId}, artifact {ArtifactId}, {Bytes} bytes",
            capture.CaptureId, capture.SourcePlatform, queueItem.QueueId, artifact.ArtifactId, artifact.ByteLength);

        return IngestionResult.Ingested(capture.CaptureId, artifact.ArtifactId, queueItem.QueueId);
    }

    private static string? ValidateEnvelope(RemoteCaptureEnvelope capture)
    {
        if (string.IsNullOrWhiteSpace(capture.CaptureId))
        {
            return "Capture ID is required.";
        }

        if (string.IsNullOrWhiteSpace(capture.ExpectedSha256))
        {
            return "Expected SHA-256 is required.";
        }

        if (capture.ExpectedByteLength <= 0)
        {
            return "Expected byte length must be positive.";
        }

        if (string.IsNullOrWhiteSpace(capture.StorageLocator))
        {
            return "Storage locator is required.";
        }

        return null;
    }

    private static async Task<(string Sha256, long ByteLength)> ComputeSha256Async(
        string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return (Convert.ToHexString(hash).ToLowerInvariant(), stream.Length);
    }

    private static string MediaTypeFromPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".mp3" => "audio/mpeg",
            ".aac" => "audio/aac",
            ".flac" => "audio/flac",
            ".ogg" => "audio/ogg",
            _ => "application/octet-stream",
        };

    #endregion
}
