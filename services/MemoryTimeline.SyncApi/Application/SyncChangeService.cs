using System.Text.Json;
using MemoryTimeline.SyncApi.Domain;
using MemoryTimeline.SyncApi.Infrastructure;
using MemoryTimeline.SyncContracts;
using Microsoft.EntityFrameworkCore;

namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// <see cref="ISyncChangeService"/> over the sync database. Stateless over the
/// context factory; registered as a singleton. Logs counts and identifiers
/// only — change payload contents are never logged (design §14.5).
/// </summary>
public sealed class SyncChangeService : ISyncChangeService
{
    private const int DefaultPullLimit = 100;
    private const int MaxPullLimit = 500;

    private static readonly string[] AllowedEntityTypes =
    [
        SyncChangeEntityType.Capture,
        SyncChangeEntityType.CaptureArtifact,
        SyncChangeEntityType.CaptureStatus,
        SyncChangeEntityType.RecordingQueue,
        SyncChangeEntityType.PendingEvent,
        SyncChangeEntityType.Event,
    ];

    /// <summary>Statuses a capture_status payload may carry (SyncCaptureStatus vocabulary).</summary>
    private static readonly string[] AllowedCaptureStatuses =
    [
        SyncCaptureStatus.LocalOnly,
        SyncCaptureStatus.Uploading,
        SyncCaptureStatus.Received,
        SyncCaptureStatus.Processing,
        SyncCaptureStatus.ReviewReady,
        SyncCaptureStatus.Completed,
        SyncCaptureStatus.Failed,
    ];

    private readonly IDbContextFactory<SyncDbContext> _contextFactory;
    private readonly ILogger<SyncChangeService> _logger;

    public SyncChangeService(IDbContextFactory<SyncDbContext> contextFactory, ILogger<SyncChangeService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<ServiceResult<SyncPullResponse>> PullAsync(
        Device caller,
        long cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var effectiveLimit = limit <= 0 ? DefaultPullLimit : Math.Clamp(limit, 1, MaxPullLimit);

        // The change log is owner-scoped and fans out to every device of that
        // owner except the one that authored the row. A capture_status change
        // therefore reaches the capture's originating phone: its publisher is
        // Windows, so echo suppression (which keys on the *publishing* device,
        // never the capture's source device) cannot swallow it.
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SyncChanges.AsNoTracking()
            .Where(c => c.OwnerId == caller.OwnerId
                && c.ChangeId > cursor
                && (c.SourceDeviceId == null || c.SourceDeviceId != caller.DeviceId))
            .OrderBy(c => c.ChangeId)
            .Take(effectiveLimit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > effectiveLimit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var response = new SyncPullResponse
        {
            Changes = rows.Select(ToDto).ToList(),
            NextCursor = rows.Count > 0 ? rows[^1].ChangeId : cursor,
            HasMore = hasMore,
        };

        _logger.LogDebug(
            "Device {DeviceId} pulled {ChangeCount} changes after cursor {Cursor}.",
            caller.DeviceId, rows.Count, cursor);
        return ServiceResult<SyncPullResponse>.Ok(response);
    }

    public async Task<ServiceResult<SyncPushResponse>> PushAsync(
        Device caller,
        SyncPushRequest request,
        CancellationToken cancellationToken)
    {
        var response = new SyncPushResponse();
        var entries = request.Entries ?? [];
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        foreach (var entry in entries)
        {
            try
            {
                response.Results.Add(await ApplyEntryAsync(db, caller, entry, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad entry must not fail the batch (design §12.1); surface
                // it per-entry, and drop its tracked rows so they cannot leak
                // into the next entry's save on the shared context.
                db.ChangeTracker.Clear();
                _logger.LogError(
                    ex,
                    "Failed to apply pushed change (device {DeviceId}, sequence {ClientSequence}, entity type {EntityType}).",
                    caller.DeviceId, entry.ClientSequence, entry.EntityType);
                response.Results.Add(new SyncPushEntryResult
                {
                    ClientSequence = entry.ClientSequence,
                    Accepted = false,
                    Error = SyncApiErrorCodes.InternalError,
                });
            }
        }

        _logger.LogInformation(
            "Device {DeviceId} pushed {EntryCount} entries ({AcceptedCount} accepted).",
            caller.DeviceId, entries.Count, response.Results.Count(r => r.Accepted));
        return ServiceResult<SyncPushResponse>.Ok(response);
    }

    public async Task<ServiceResult<bool>> AckAsync(
        Device caller,
        SyncAckRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var device = await db.Devices.FirstOrDefaultAsync(d => d.DeviceId == caller.DeviceId, cancellationToken);
        if (device is null)
        {
            return ServiceResult<bool>.Fail(
                StatusCodes.Status401Unauthorized, SyncApiErrorCodes.DeviceUnknown, "No such device.");
        }

        device.AckedCursor = request.Cursor;
        device.LastSeenAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Ok(true);
    }

    private async Task<SyncPushEntryResult> ApplyEntryAsync(
        SyncDbContext db,
        Device caller,
        SyncPushEntry entry,
        CancellationToken cancellationToken)
    {
        var receipt = await db.PushReceipts.FindAsync(
            [caller.DeviceId, entry.ClientSequence], cancellationToken);
        if (receipt is not null)
        {
            return new SyncPushEntryResult
            {
                ClientSequence = entry.ClientSequence,
                Accepted = true,
                Duplicate = true,
                ServerChangeId = receipt.ServerChangeId,
            };
        }

        if (string.IsNullOrWhiteSpace(entry.EntityId) || !AllowedEntityTypes.Contains(entry.EntityType))
        {
            return Rejected(entry, "entityType or entityId is not valid.");
        }

        if (entry.Operation is not (SyncOperation.Upsert or SyncOperation.Delete))
        {
            return Rejected(entry, "operation must be upsert or delete.");
        }

        long revision = 1;
        var payloadJson = entry.PayloadJson;
        if (entry.EntityType == SyncChangeEntityType.Capture)
        {
            var payload = TryParseCapturePayload(entry.PayloadJson);
            if (payload is not null)
            {
                var capture = await db.Captures.FirstOrDefaultAsync(
                    c => c.CaptureId == entry.EntityId && c.OwnerId == caller.OwnerId, cancellationToken);
                if (capture is not null && !string.IsNullOrWhiteSpace(payload.Status))
                {
                    capture.Status = payload.Status;
                    capture.Revision++;
                    revision = capture.Revision;
                }
            }
        }
        else if (entry.EntityType == SyncChangeEntityType.CaptureStatus)
        {
            var (status, rejection) = await TryApplyCaptureStatusAsync(db, caller, entry, cancellationToken);
            if (status is null)
            {
                return rejection!;
            }

            revision = status.Revision;
            payloadJson = EntityMappers.SerializeCaptureStatusPayload(status);
            _logger.LogDebug(
                "Capture {CaptureId} status {Status} (revision {Revision}) recorded from device {DeviceId}.",
                status.CaptureId, status.Status, status.Revision, caller.DeviceId);
        }

        var change = new SyncChangeRow
        {
            OwnerId = caller.OwnerId,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Operation = entry.Operation,
            Revision = revision,
            SourceDeviceId = caller.DeviceId,
            PayloadJson = payloadJson,
            ChangedAtUtc = entry.ChangedAtUtc == default ? DateTime.UtcNow : entry.ChangedAtUtc,
        };
        db.SyncChanges.Add(change);

        // ServerChangeId carries the change row's temporary key (readable only
        // through the change tracker before save); the FK configured in
        // SyncDbContext replaces it with the store-generated change ID at save
        // time. One SaveChanges commits the change row, the receipt, and any
        // capture update atomically, so a crashed request that the client
        // retries can never leave a receipt-less duplicate change row.
        db.PushReceipts.Add(new PushReceipt
        {
            DeviceId = caller.DeviceId,
            ClientSequence = entry.ClientSequence,
            ServerChangeId = db.Entry(change).Property(c => c.ChangeId).CurrentValue,
            ReceivedAtUtc = DateTime.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Concurrent push of the same sequence from the same device: the
            // whole save rolled back (no orphan change row); report the
            // winning receipt.
            _logger.LogWarning(
                ex,
                "Concurrent push receipt for device {DeviceId} sequence {ClientSequence}.",
                caller.DeviceId, entry.ClientSequence);
            db.ChangeTracker.Clear();
            var winner = await db.PushReceipts.FindAsync(
                [caller.DeviceId, entry.ClientSequence], cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return new SyncPushEntryResult
            {
                ClientSequence = entry.ClientSequence,
                Accepted = true,
                Duplicate = true,
                ServerChangeId = winner.ServerChangeId,
            };
        }

        return new SyncPushEntryResult
        {
            ClientSequence = entry.ClientSequence,
            Accepted = true,
            Duplicate = false,
            ServerChangeId = change.ChangeId,
        };
    }

    /// <summary>
    /// Validates a pushed capture_status entry and applies it to the capture's
    /// status projection, returning the tracked (still unsaved) row so the
    /// caller commits it together with the change row and the push receipt.
    /// On failure returns the per-entry rejection to hand back to the
    /// publisher. Windows — not the service — owns truncation of the
    /// transcript preview, so an over-long preview is rejected rather than cut
    /// down (design §19 Phase 3).
    /// </summary>
    private static async Task<(CaptureStatusRow? Status, SyncPushEntryResult? Rejection)> TryApplyCaptureStatusAsync(
        SyncDbContext db,
        Device caller,
        SyncPushEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.Operation != SyncOperation.Upsert)
        {
            return (null, Rejected(entry, "capture_status supports upsert only."));
        }

        var payload = TryParseCaptureStatusPayload(entry.PayloadJson);
        if (payload is null)
        {
            return (null, Rejected(entry, "capture_status requires a well-formed payload."));
        }

        if (!Guid.TryParse(entry.EntityId, out var entityGuid)
            || !Guid.TryParse(payload.CaptureId, out var payloadGuid)
            || entityGuid != payloadGuid)
        {
            return (null, Rejected(entry, "payload captureId must be a UUID equal to entityId."));
        }

        var status = string.IsNullOrWhiteSpace(payload.Status)
            ? string.Empty
            : payload.Status.Trim().ToLowerInvariant();
        if (!AllowedCaptureStatuses.Contains(status))
        {
            return (null, Rejected(
                entry,
                "status must be one of: local_only, uploading, received, processing, review_ready, completed, failed."));
        }

        if (payload.TranscriptPreview is { Length: > CaptureStatusChangePayload.TranscriptPreviewMaxChars })
        {
            return (null, Rejected(
                entry,
                $"transcriptPreview must be at most {CaptureStatusChangePayload.TranscriptPreviewMaxChars} characters."));
        }

        if (payload.TranscriptCharCount < 0 || payload.PendingEventCount < 0 || payload.ApprovedEventCount < 0)
        {
            return (null, Rejected(
                entry, "transcriptCharCount, pendingEventCount and approvedEventCount must not be negative."));
        }

        var captureId = entityGuid.ToString("D");
        var capture = await db.Captures.FirstOrDefaultAsync(
            c => c.CaptureId == captureId && c.OwnerId == caller.OwnerId, cancellationToken);
        if (capture is null)
        {
            return (null, Rejected(entry, SyncApiErrorCodes.CaptureNotFound, "No such capture."));
        }

        var row = await db.CaptureStatuses.FirstOrDefaultAsync(
            s => s.CaptureId == captureId, cancellationToken);
        if (row is null)
        {
            row = new CaptureStatusRow { CaptureId = captureId, OwnerId = capture.OwnerId };
            db.CaptureStatuses.Add(row);
        }

        // Latest wins: the projection holds the current state only, and the
        // change log preserves the sequence that produced it.
        row.Status = status;
        row.ProcessingStage = payload.ProcessingStage;
        row.UpdatedAtUtc = payload.UpdatedAtUtc == default ? DateTime.UtcNow : payload.UpdatedAtUtc;
        row.TranscriptAvailable = payload.TranscriptAvailable;
        row.TranscriptPreview = payload.TranscriptPreview;
        row.TranscriptCharCount = payload.TranscriptCharCount;
        row.PendingEventCount = payload.PendingEventCount;
        row.ApprovedEventCount = payload.ApprovedEventCount;
        row.FailureReason = payload.FailureReason;
        row.FailureRetryable = payload.FailureRetryable;
        row.Revision++;
        row.ReceivedAtUtc = DateTime.UtcNow;

        // Keep the capture read model (CaptureResponse.Status) in step with the
        // coarse lifecycle state; a stage-only advance leaves the capture row
        // and its revision alone.
        if (capture.Status != status)
        {
            capture.Status = status;
            capture.Revision++;
        }

        return (row, null);
    }

    private static SyncPushEntryResult Rejected(SyncPushEntry entry, string message)
        => Rejected(entry, SyncApiErrorCodes.ValidationError, message);

    private static SyncPushEntryResult Rejected(SyncPushEntry entry, string code, string message) => new()
    {
        ClientSequence = entry.ClientSequence,
        Accepted = false,
        Error = $"{code}: {message}",
    };

    private static CaptureChangePayload? TryParseCapturePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CaptureChangePayload>(payloadJson, SyncJson.Options);
        }
        catch (JsonException)
        {
            // Unparseable payloads are still logged to the change stream (the
            // entry is accepted); they simply do not update the capture row.
            return null;
        }
    }

    /// <summary>
    /// Parses a capture_status payload; null when it is missing or malformed,
    /// which rejects the entry (unlike a capture payload, the status change
    /// carries no meaning without one).
    /// </summary>
    private static CaptureStatusChangePayload? TryParseCaptureStatusPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CaptureStatusChangePayload>(payloadJson, SyncJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SyncChangeDto ToDto(SyncChangeRow row) => new()
    {
        ChangeId = row.ChangeId,
        EntityType = row.EntityType,
        EntityId = row.EntityId,
        Operation = row.Operation,
        Revision = row.Revision,
        ChangedAtUtc = row.ChangedAtUtc,
        SourceDeviceId = row.SourceDeviceId,
        PayloadJson = row.PayloadJson,
    };
}
