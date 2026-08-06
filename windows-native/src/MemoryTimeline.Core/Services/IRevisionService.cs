using CommunityToolkit.Mvvm.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Read + restore surface over the append-only event revision history.
/// Kept separate from <see cref="IEventService"/> so the event CRUD contract
/// stays uncluttered.
/// </summary>
public interface IRevisionService
{
    /// <summary>Gets an event's revisions, newest first.</summary>
    Task<List<EventRevision>> GetHistoryAsync(string eventId);

    /// <summary>Gets one revision by id, or null.</summary>
    Task<EventRevision?> GetRevisionAsync(string revisionId);

    /// <summary>Deserializes a revision's stored snapshot, or null when malformed.</summary>
    EventSnapshot? DeserializeSnapshot(EventRevision revision);

    /// <summary>
    /// Restores an event to the state captured by the given revision:
    /// applies the snapshot's scalar fields (UpdateEventAsync semantics -
    /// validation + updated_at stamp) and reconciles the tag/person/location
    /// junctions to the snapshot's names (create-or-link case-insensitively).
    /// History stays append-only: a NEW Restored revision holding the
    /// PRE-restore state is written (when revision history is enabled), and
    /// an <see cref="EventUpdatedMessage"/> is published so open views refresh.
    /// Throws with a user-presentable message on failure.
    /// </summary>
    Task RestoreRevisionAsync(string eventId, string revisionId);
}

/// <summary>
/// Revision history implementation. Registered singleton; every DB operation
/// uses a short-lived context from the factory or a factory-based repository.
/// </summary>
public class RevisionService : IRevisionService
{
    private readonly IEventRevisionRepository _revisionRepository;
    private readonly IEventService _eventService;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly EventRevisionWriter _revisionWriter;
    private readonly ILogger<RevisionService> _logger;

    public RevisionService(
        IEventRevisionRepository revisionRepository,
        IEventService eventService,
        IDbContextFactory<AppDbContext> contextFactory,
        EventRevisionWriter revisionWriter,
        ILogger<RevisionService> logger)
    {
        _revisionRepository = revisionRepository;
        _eventService = eventService;
        _contextFactory = contextFactory;
        _revisionWriter = revisionWriter;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<List<EventRevision>> GetHistoryAsync(string eventId)
        => _revisionRepository.GetForEventAsync(eventId);

    /// <inheritdoc />
    public Task<EventRevision?> GetRevisionAsync(string revisionId)
        => _revisionRepository.GetByIdAsync(revisionId);

    /// <inheritdoc />
    public EventSnapshot? DeserializeSnapshot(EventRevision revision)
        => EventRevisionWriter.TryDeserialize(revision.SnapshotJson);

    /// <inheritdoc />
    public async Task RestoreRevisionAsync(string eventId, string revisionId)
    {
        try
        {
            var revision = await _revisionRepository.GetByIdAsync(revisionId)
                ?? throw new InvalidOperationException("The selected version could not be found.");

            if (!string.Equals(revision.EventId, eventId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The selected version belongs to a different event.");
            }

            var snapshot = EventRevisionWriter.TryDeserialize(revision.SnapshotJson)
                ?? throw new InvalidOperationException("The selected version's snapshot is unreadable and cannot be restored.");

            ValidateSnapshot(snapshot);

            // Capture the PRE-restore state first: it is about to be
            // overwritten and exists nowhere else in history (Edited revisions
            // only capture PRE-edit states).
            var preRestore = await _revisionWriter.BuildSnapshotFromDatabaseAsync(eventId)
                ?? throw new InvalidOperationException("The event no longer exists and cannot be restored.");

            // 1. Scalar fields, applied with UpdateEventAsync semantics
            //    (load-tracked-row + copy fields + save; updated_at is stamped
            //    by AppDbContext.UpdateTimestamps). Deliberately NOT routed
            //    through IEventService.UpdateEventAsync: that hook writes an
            //    Edited revision of its own, which would double-record every
            //    restore. The single Restored revision below is the record.
            var appliedEvent = await ApplyScalarFieldsAsync(eventId, snapshot);

            // 2. Junction reconciliation to the snapshot's names, reusing the
            //    existing association APIs (create-or-link case-insensitively).
            await ReconcileTagsAsync(eventId, snapshot.Tags);
            await ReconcilePeopleAsync(eventId, snapshot.People);
            await ReconcileLocationsAsync(eventId, snapshot.Locations);

            // 3. Append the Restored revision (history is append-only). The
            //    changed-fields csv covers scalar diffs plus set-level junction
            //    diffs between the pre-restore state and the applied snapshot.
            var changedCsv = ComputeRestoreChangedFields(preRestore, appliedEvent, snapshot);
            await _revisionWriter.TryWriteSnapshotAsync(preRestore, RevisionKind.Restored, changedCsv);

            _logger.LogInformation(
                "Restored event {EventId} to revision {RevisionId} ({Kind} from {RevisedAt:u}).",
                eventId, revisionId, revision.RevisionKind, revision.RevisedAt);

            // Notify open views (timeline, search). A subscriber failure must
            // not turn a successful restore into an error.
            try
            {
                WeakReferenceMessenger.Default.Send(new EventUpdatedMessage(eventId, snapshot.StartDate));
            }
            catch (Exception messengerEx)
            {
                _logger.LogWarning(messengerEx,
                    "Error publishing EventUpdatedMessage after restoring event {EventId}", eventId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring event {EventId} to revision {RevisionId}", eventId, revisionId);
            throw;
        }
    }

    private static void ValidateSnapshot(EventSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Title))
        {
            throw new InvalidOperationException("The selected version has no title and cannot be restored.");
        }

        if (snapshot.StartDate == default)
        {
            throw new InvalidOperationException("The selected version has no start date and cannot be restored.");
        }

        if (snapshot.EndDate.HasValue && snapshot.EndDate < snapshot.StartDate)
        {
            throw new InvalidOperationException("The selected version's end date is before its start date and cannot be restored.");
        }
    }

    private async Task<Event> ApplyScalarFieldsAsync(string eventId, EventSnapshot snapshot)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var tracked = await context.Events.FirstOrDefaultAsync(e => e.EventId == eventId)
            ?? throw new InvalidOperationException("The event no longer exists and cannot be restored.");

        tracked.Title = snapshot.Title;
        tracked.StartDate = snapshot.StartDate;
        tracked.EndDate = snapshot.EndDate;
        tracked.Description = snapshot.Description;
        tracked.Category = snapshot.Category;
        tracked.Location = snapshot.Location;
        tracked.Confidence = snapshot.Confidence;
        tracked.AudioFilePath = snapshot.AudioFilePath;
        tracked.RawTranscript = snapshot.RawTranscript;
        tracked.ExtractionMetadata = snapshot.ExtractionMetadata;
        tracked.DatePrecision = snapshot.DatePrecision;
        tracked.EarliestPossible = snapshot.EarliestPossible;
        tracked.LatestPossible = snapshot.LatestPossible;

        // The snapshot's era may have been deleted since; a stale FK value
        // would violate the events.era_id constraint, so only re-link eras
        // that still exist.
        if (!string.IsNullOrEmpty(snapshot.EraId) &&
            await context.Eras.AnyAsync(e => e.EraId == snapshot.EraId))
        {
            tracked.EraId = snapshot.EraId;
        }
        else
        {
            if (!string.IsNullOrEmpty(snapshot.EraId))
            {
                _logger.LogInformation(
                    "Era {EraId} from the restored snapshot no longer exists; event {EventId} keeps no era link.",
                    snapshot.EraId, eventId);
            }

            tracked.EraId = null;
        }

        await context.SaveChangesAsync();
        return tracked;
    }

    private async Task ReconcileTagsAsync(string eventId, List<string> snapshotNames)
    {
        var desired = NameSet(snapshotNames);
        var currentTags = (await _eventService.GetEventTagsAsync(eventId)).ToList();

        foreach (var tag in currentTags.Where(t => !desired.Contains(t.TagName)))
        {
            await _eventService.RemoveTagFromEventAsync(eventId, tag.TagId);
        }

        var currentNames = NameSet(currentTags.Select(t => t.TagName));
        foreach (var name in desired.Where(n => !currentNames.Contains(n)))
        {
            var tagId = await FindOrCreateTagAsync(name);
            await _eventService.AddTagToEventAsync(eventId, tagId);
        }
    }

    private async Task ReconcilePeopleAsync(string eventId, List<string> snapshotNames)
    {
        var desired = NameSet(snapshotNames);
        var currentPeople = (await _eventService.GetEventPeopleAsync(eventId)).ToList();

        foreach (var person in currentPeople.Where(p => !desired.Contains(p.Name)))
        {
            await _eventService.RemovePersonFromEventAsync(eventId, person.PersonId);
        }

        var currentNames = NameSet(currentPeople.Select(p => p.Name));
        foreach (var name in desired.Where(n => !currentNames.Contains(n)))
        {
            var personId = await FindOrCreatePersonAsync(name);
            // Two snapshot names can resolve to the same living contact via
            // aliases; AddPersonToEventAsync is idempotent, so re-links no-op.
            await _eventService.AddPersonToEventAsync(eventId, personId);
        }
    }

    private async Task ReconcileLocationsAsync(string eventId, List<string> snapshotNames)
    {
        var desired = NameSet(snapshotNames);
        var currentLocations = (await _eventService.GetEventLocationsAsync(eventId)).ToList();

        foreach (var location in currentLocations.Where(l => !desired.Contains(l.Name)))
        {
            await _eventService.RemoveLocationFromEventAsync(eventId, location.LocationId);
        }

        var currentNames = NameSet(currentLocations.Select(l => l.Name));
        foreach (var name in desired.Where(n => !currentNames.Contains(n)))
        {
            var locationId = await FindOrCreateLocationAsync(name);
            await _eventService.AddLocationToEventAsync(eventId, locationId);
        }
    }

    private async Task<string> FindOrCreateTagAsync(string name)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // ToLower() comparison is provider-portable case-insensitivity (the
        // same pattern EventExtractionService uses for its upserts).
        var existing = await context.Tags
            .FirstOrDefaultAsync(t => t.TagName.ToLower() == name.ToLower());
        if (existing != null)
        {
            return existing.TagId;
        }

        var tag = new Tag
        {
            TagId = Guid.NewGuid().ToString(),
            TagName = name,
            CreatedAt = DateTime.UtcNow
        };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        return tag.TagId;
    }

    private async Task<string> FindOrCreatePersonAsync(string name)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Living person by exact ci name first...
        var living = await context.People
            .FirstOrDefaultAsync(p => p.MergedIntoId == null && p.Name.ToLower() == name.ToLower());
        if (living != null)
        {
            return living.PersonId;
        }

        // ...then the alias table ("Bob" for "Robert"), following the merge
        // tombstone chain to the living survivor so a restore never links a
        // merged-away person.
        var aliasOwnerId = await context.PersonAliases
            .Where(a => a.Alias.ToLower() == name.ToLower())
            .Select(a => a.PersonId)
            .FirstOrDefaultAsync();
        if (aliasOwnerId != null)
        {
            var resolvedId = aliasOwnerId;
            for (var hop = 0; hop < 10; hop++)
            {
                var mergedInto = await context.People
                    .Where(p => p.PersonId == resolvedId)
                    .Select(p => p.MergedIntoId)
                    .FirstOrDefaultAsync();
                if (string.IsNullOrEmpty(mergedInto))
                {
                    break;
                }

                resolvedId = mergedInto;
            }

            if (await context.People.AnyAsync(p => p.PersonId == resolvedId && p.MergedIntoId == null))
            {
                return resolvedId;
            }
        }

        var person = new Person
        {
            PersonId = Guid.NewGuid().ToString(),
            Name = name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.People.Add(person);
        await context.SaveChangesAsync();
        return person.PersonId;
    }

    private async Task<string> FindOrCreateLocationAsync(string name)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var existing = await context.Locations
            .FirstOrDefaultAsync(l => l.Name.ToLower() == name.ToLower());
        if (existing != null)
        {
            return existing.LocationId;
        }

        var location = new Location
        {
            LocationId = Guid.NewGuid().ToString(),
            Name = name,
            CreatedAt = DateTime.UtcNow
        };
        context.Locations.Add(location);
        await context.SaveChangesAsync();
        return location.LocationId;
    }

    private static string? ComputeRestoreChangedFields(
        EventSnapshot preRestore,
        Event appliedEvent,
        EventSnapshot appliedSnapshot)
    {
        var tokens = new List<string>();

        var scalarCsv = EventRevisionWriter.ComputeChangedFieldsCsv(preRestore, appliedEvent);
        if (scalarCsv != null)
        {
            tokens.AddRange(scalarCsv.Split(','));
        }

        if (!NameSet(preRestore.Tags).SetEquals(NameSet(appliedSnapshot.Tags)))
        {
            tokens.Add("tags");
        }

        if (!NameSet(preRestore.People).SetEquals(NameSet(appliedSnapshot.People)))
        {
            tokens.Add("people");
        }

        if (!NameSet(preRestore.Locations).SetEquals(NameSet(appliedSnapshot.Locations)))
        {
            tokens.Add("locations");
        }

        return tokens.Count == 0 ? null : string.Join(",", tokens);
    }

    private static HashSet<string> NameSet(IEnumerable<string> names)
        => names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
