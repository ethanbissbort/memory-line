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
        SyncChangeEntityType.RecordingQueue,
        SyncChangeEntityType.PendingEvent,
        SyncChangeEntityType.Event,
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

        var change = new SyncChangeRow
        {
            OwnerId = caller.OwnerId,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Operation = entry.Operation,
            Revision = revision,
            SourceDeviceId = caller.DeviceId,
            PayloadJson = entry.PayloadJson,
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

    private static SyncPushEntryResult Rejected(SyncPushEntry entry, string message) => new()
    {
        ClientSequence = entry.ClientSequence,
        Accepted = false,
        Error = $"{SyncApiErrorCodes.ValidationError}: {message}",
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
