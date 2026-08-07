using System.Text.Json;
using System.Text.Json.Nodes;
using MemoryTimeline.SyncApi.Domain;
using MemoryTimeline.SyncApi.Infrastructure;
using MemoryTimeline.SyncContracts;
using Microsoft.EntityFrameworkCore;

namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// <see cref="ICaptureService"/> over the sync database. Stateless over the
/// context factory; registered as a singleton. Logs identifiers only — never
/// titles, notes, or location content (design §14.5).
/// </summary>
public sealed class CaptureService : ICaptureService
{
    private static readonly string[] AllowedCaptureTypes =
        ["memory", "idea", "trip_log", "reminder", "question", "unclassified"];

    private readonly IDbContextFactory<SyncDbContext> _contextFactory;
    private readonly ILogger<CaptureService> _logger;

    public CaptureService(IDbContextFactory<SyncDbContext> contextFactory, ILogger<CaptureService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<ServiceResult<CaptureWriteResult>> CreateAsync(
        Device caller,
        CaptureCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.CaptureId, out var captureGuid))
        {
            return ServiceResult<CaptureWriteResult>.Fail(
                StatusCodes.Status400BadRequest, SyncApiErrorCodes.ValidationError, "captureId must be a UUID.");
        }

        if (request.CapturedAtUtc == default)
        {
            return ServiceResult<CaptureWriteResult>.Fail(
                StatusCodes.Status400BadRequest, SyncApiErrorCodes.ValidationError, "capturedAtUtc is required.");
        }

        var captureType = string.IsNullOrWhiteSpace(request.CaptureType)
            ? "unclassified"
            : request.CaptureType.Trim().ToLowerInvariant();
        if (!AllowedCaptureTypes.Contains(captureType))
        {
            return ServiceResult<CaptureWriteResult>.Fail(
                StatusCodes.Status400BadRequest, SyncApiErrorCodes.ValidationError,
                "captureType must be one of: memory, idea, trip_log, reminder, question, unclassified.");
        }

        var captureId = captureGuid.ToString("D");
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.Captures.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CaptureId == captureId, cancellationToken);
        if (existing is not null)
        {
            if (existing.OwnerId != caller.OwnerId)
            {
                return ServiceResult<CaptureWriteResult>.Fail(
                    StatusCodes.Status409Conflict, SyncApiErrorCodes.CaptureConflict,
                    "A capture with this ID already exists for a different owner.");
            }

            var existingArtifacts = await db.Artifacts.AsNoTracking()
                .Where(a => a.CaptureId == captureId)
                .ToListAsync(cancellationToken);
            _logger.LogInformation("Capture {CaptureId} create replayed idempotently.", captureId);
            return ServiceResult<CaptureWriteResult>.Ok(
                new CaptureWriteResult(EntityMappers.ToCaptureResponse(existing, existingArtifacts), Created: false));
        }

        var capture = new ServerCapture
        {
            CaptureId = captureId,
            OwnerId = caller.OwnerId,
            SourceDeviceId = caller.DeviceId,
            SourcePlatform = caller.Platform,
            CaptureType = captureType,
            CapturedAtUtc = request.CapturedAtUtc,
            TimezoneId = request.TimezoneId,
            LocalOffsetMinutes = request.LocalOffsetMinutes,
            TripId = request.TripId,
            TitleHint = request.TitleHint,
            UserNote = request.UserNote,
            LocationJson = request.LocationContext is null
                ? null
                : JsonSerializer.Serialize(request.LocationContext, SyncJson.Options),
            ClientSchemaVersion = request.ClientSchemaVersion,
            Status = SyncCaptureStatus.Received,
            Revision = 1,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.Captures.Add(capture);
        db.SyncChanges.Add(EntityMappers.CreateCaptureChange(capture, audioArtifact: null, caller.DeviceId));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a concurrent create of the same captureId: replay the winner.
            await using var retryDb = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var winner = await retryDb.Captures.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CaptureId == captureId && c.OwnerId == caller.OwnerId, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            var winnerArtifacts = await retryDb.Artifacts.AsNoTracking()
                .Where(a => a.CaptureId == captureId)
                .ToListAsync(cancellationToken);
            return ServiceResult<CaptureWriteResult>.Ok(
                new CaptureWriteResult(EntityMappers.ToCaptureResponse(winner, winnerArtifacts), Created: false));
        }

        _logger.LogInformation(
            "Capture {CaptureId} created by device {DeviceId} (type {CaptureType}).",
            captureId, caller.DeviceId, captureType);
        return ServiceResult<CaptureWriteResult>.Ok(
            new CaptureWriteResult(EntityMappers.ToCaptureResponse(capture, []), Created: true));
    }

    public async Task<ServiceResult<CaptureResponse>> GetAsync(
        Device caller,
        string captureId,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var (capture, error) = await LoadCaptureAsync(db, caller, captureId, track: false, cancellationToken);
        if (capture is null)
        {
            return ServiceResult<CaptureResponse>.Fail(error!);
        }

        var artifacts = await db.Artifacts.AsNoTracking()
            .Where(a => a.CaptureId == capture.CaptureId)
            .ToListAsync(cancellationToken);
        return ServiceResult<CaptureResponse>.Ok(EntityMappers.ToCaptureResponse(capture, artifacts));
    }

    public async Task<ServiceResult<CaptureResponse>> UpdateAsync(
        Device caller,
        string captureId,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var (capture, error) = await LoadCaptureAsync(db, caller, captureId, track: true, cancellationToken);
        if (capture is null)
        {
            return ServiceResult<CaptureResponse>.Fail(error!);
        }

        var changed = false;

        if (body.TryGetPropertyValue("captureType", out var captureTypeNode))
        {
            if (!TryGetString(captureTypeNode, out var rawCaptureType) || rawCaptureType is null)
            {
                return ValidationError("captureType must be a string.");
            }

            var captureType = rawCaptureType.Trim().ToLowerInvariant();
            if (!AllowedCaptureTypes.Contains(captureType))
            {
                return ValidationError(
                    "captureType must be one of: memory, idea, trip_log, reminder, question, unclassified.");
            }

            changed |= capture.CaptureType != captureType;
            capture.CaptureType = captureType;
        }

        if (body.TryGetPropertyValue("titleHint", out var titleHintNode))
        {
            if (!TryGetString(titleHintNode, out var titleHint))
            {
                return ValidationError("titleHint must be a string or null.");
            }

            changed |= capture.TitleHint != titleHint;
            capture.TitleHint = titleHint;
        }

        if (body.TryGetPropertyValue("userNote", out var userNoteNode))
        {
            if (!TryGetString(userNoteNode, out var userNote))
            {
                return ValidationError("userNote must be a string or null.");
            }

            changed |= capture.UserNote != userNote;
            capture.UserNote = userNote;
        }

        if (body.TryGetPropertyValue("tripId", out var tripIdNode))
        {
            if (!TryGetString(tripIdNode, out var tripId) || (tripId is not null && !Guid.TryParse(tripId, out _)))
            {
                return ValidationError("tripId must be a UUID or null.");
            }

            changed |= capture.TripId != tripId;
            capture.TripId = tripId;
        }

        var artifacts = await db.Artifacts.AsNoTracking()
            .Where(a => a.CaptureId == capture.CaptureId)
            .ToListAsync(cancellationToken);

        if (changed)
        {
            capture.Revision++;
            var completedAudio = artifacts.FirstOrDefault(
                a => a.State == ArtifactState.Complete && EntityMappers.IsAudioArtifact(a));
            db.SyncChanges.Add(EntityMappers.CreateCaptureChange(
                capture,
                completedAudio is null ? null : EntityMappers.ToSummary(completedAudio),
                caller.DeviceId));
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Capture {CaptureId} updated by device {DeviceId}; revision {Revision}.",
                capture.CaptureId, caller.DeviceId, capture.Revision);
        }

        return ServiceResult<CaptureResponse>.Ok(EntityMappers.ToCaptureResponse(capture, artifacts));
    }

    public async Task<ServiceResult<CaptureResponse>> TouchAsync(
        Device caller,
        string captureId,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var (capture, error) = await LoadCaptureAsync(db, caller, captureId, track: false, cancellationToken);
        if (capture is null)
        {
            return ServiceResult<CaptureResponse>.Fail(error!);
        }

        var device = await db.Devices.FirstOrDefaultAsync(d => d.DeviceId == caller.DeviceId, cancellationToken);
        if (device is not null)
        {
            device.LastSeenAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        var artifacts = await db.Artifacts.AsNoTracking()
            .Where(a => a.CaptureId == capture.CaptureId)
            .ToListAsync(cancellationToken);
        return ServiceResult<CaptureResponse>.Ok(EntityMappers.ToCaptureResponse(capture, artifacts));
    }

    /// <summary>Reads a JSON node expected to be a string or null; false when it is another type.</summary>
    private static bool TryGetString(JsonNode? node, out string? value)
    {
        if (node is null)
        {
            value = null;
            return true;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
        {
            value = text;
            return true;
        }

        value = null;
        return false;
    }

    private static ServiceResult<CaptureResponse> ValidationError(string message)
        => ServiceResult<CaptureResponse>.Fail(
            StatusCodes.Status400BadRequest, SyncApiErrorCodes.ValidationError, message);

    private static async Task<(ServerCapture? Capture, ServiceError? Error)> LoadCaptureAsync(
        SyncDbContext db,
        Device caller,
        string captureId,
        bool track,
        CancellationToken cancellationToken)
    {
        var notFound = new ServiceError(
            StatusCodes.Status404NotFound, SyncApiErrorCodes.CaptureNotFound, "No such capture.");

        if (!Guid.TryParse(captureId, out var captureGuid))
        {
            return (null, notFound);
        }

        var normalizedId = captureGuid.ToString("D");
        var query = track ? db.Captures : db.Captures.AsNoTracking();
        var capture = await query.FirstOrDefaultAsync(
            c => c.CaptureId == normalizedId && c.OwnerId == caller.OwnerId, cancellationToken);
        return capture is null ? (null, notFound) : (capture, null);
    }
}
