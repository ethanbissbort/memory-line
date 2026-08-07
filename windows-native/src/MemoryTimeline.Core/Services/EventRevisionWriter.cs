using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using System.Text.Json;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// The JSON shape stored in <see cref="EventRevision.SnapshotJson"/>: every
/// event scalar field plus the tag/person/location NAMES linked at that
/// moment. Names (not ids) are stored so a snapshot stays meaningful and
/// restorable even after tags/people/locations are renamed, merged or deleted.
/// </summary>
public sealed class EventSnapshot
{
    public string EventId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? EraId { get; set; }
    public string? Location { get; set; }
    public double? Confidence { get; set; }
    public string? AudioFilePath { get; set; }
    public string? RawTranscript { get; set; }
    public string? ExtractionMetadata { get; set; }
    public DatePrecision DatePrecision { get; set; } = DatePrecision.Day;
    public DateTime? EarliestPossible { get; set; }
    public DateTime? LatestPossible { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> People { get; set; } = new();
    public List<string> Locations { get; set; } = new();
}

/// <summary>
/// Writes append-only <see cref="EventRevision"/> rows for the event
/// lifecycle hooks (create/edit/approve/import/restore). Every write is
/// gated per call on the <c>revision_history_enabled</c> setting (default
/// true) and NEVER throws: a failed revision write is logged at Warning and
/// must not fail the primary operation.
/// Registered singleton; every DB operation uses a short-lived context from
/// the factory.
/// </summary>
public sealed class EventRevisionWriter
{
    /// <summary>
    /// Canonical field tokens used in <see cref="EventRevision.ChangedFieldsCsv"/>,
    /// in display order. Only scalar fields editable through UpdateEventAsync
    /// are diffed here; junction (tags/people/locations) changes flow through
    /// the association APIs and are diffed set-wise on restore.
    /// </summary>
    public static readonly string[] ScalarFieldTokens =
    {
        "title", "start_date", "end_date", "description", "category",
        "era_id", "location", "date_precision", "earliest_possible",
        "latest_possible", "confidence"
    };

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<EventRevisionWriter> _logger;

    public EventRevisionWriter(
        IDbContextFactory<AppDbContext> contextFactory,
        ISettingsService settingsService,
        ILogger<EventRevisionWriter> logger)
    {
        _contextFactory = contextFactory;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Whether revision writes are enabled - read from settings PER CALL so a
    /// toggle takes effect without restart. A settings read failure counts as
    /// enabled (losing history silently is worse than an extra row).
    /// </summary>
    public async Task<bool> IsEnabledAsync()
    {
        try
        {
            var value = await _settingsService.GetSettingAsync<string>(
                SettingKeys.RevisionHistoryEnabled, "true");
            return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read {Key}; treating revision history as enabled.",
                SettingKeys.RevisionHistoryEnabled);
            return true;
        }
    }

    /// <summary>
    /// Loads the event (scalars + junction names) fresh from the database and
    /// writes a revision of the given kind. Standalone hook used for
    /// Created/Imported. Never throws.
    /// </summary>
    public async Task TryWriteForEventIdAsync(string eventId, RevisionKind kind, string? changedFieldsCsv = null)
    {
        try
        {
            if (!await IsEnabledAsync())
            {
                return;
            }

            var snapshot = await BuildSnapshotFromDatabaseAsync(eventId);
            if (snapshot == null)
            {
                _logger.LogWarning(
                    "Could not build a revision snapshot for event {EventId} ({Kind}): event not found.",
                    eventId, kind);
                return;
            }

            await WriteAsync(snapshot, kind, changedFieldsCsv);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Revision write ({Kind}) failed for event {EventId}; the primary operation is unaffected.",
                kind, eventId);
        }
    }

    /// <summary>
    /// Writes a revision from an already-built snapshot (e.g. the PRE-edit
    /// state captured before an update, or the PRE-restore state). Never throws.
    /// </summary>
    public async Task TryWriteSnapshotAsync(EventSnapshot snapshot, RevisionKind kind, string? changedFieldsCsv = null)
    {
        try
        {
            if (!await IsEnabledAsync())
            {
                return;
            }

            await WriteAsync(snapshot, kind, changedFieldsCsv);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Revision write ({Kind}) failed for event {EventId}; the primary operation is unaffected.",
                kind, snapshot.EventId);
        }
    }

    /// <summary>
    /// Adds an Approved revision to the CALLER's context (no SaveChanges) so
    /// it participates in the approve transaction. Junction names are read
    /// from the context's Local collections - the rows MapExtractedMetadataAsync
    /// just added, which are exactly what the transaction will commit.
    /// Failures are logged and swallowed; the approve itself is unaffected.
    /// </summary>
    public async Task TryAddApprovedToContextAsync(AppDbContext context, Event approvedEvent)
    {
        try
        {
            if (!await IsEnabledAsync())
            {
                return;
            }

            var tags = context.EventTags.Local
                .Where(et => et.EventId == approvedEvent.EventId)
                .Select(et => et.Tag?.TagName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!);
            var people = context.EventPeople.Local
                .Where(ep => ep.EventId == approvedEvent.EventId)
                .Select(ep => ep.Person?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!);
            var locations = context.EventLocations.Local
                .Where(el => el.EventId == approvedEvent.EventId)
                .Select(el => el.Location?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!);

            var snapshot = BuildSnapshot(approvedEvent, tags, people, locations);
            context.EventRevisions.Add(CreateRevision(snapshot, RevisionKind.Approved, changedFieldsCsv: null));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not add an Approved revision for event {EventId}; the approve itself is unaffected.",
                approvedEvent.EventId);
        }
    }

    /// <summary>
    /// Loads the event's current state (scalars + junction names) on one fresh
    /// context, or null when the event does not exist. Throws on DB failure -
    /// callers that must not fail wrap this via the Try* methods.
    /// </summary>
    public async Task<EventSnapshot?> BuildSnapshotFromDatabaseAsync(string eventId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var evt = await context.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.EventId == eventId);
        if (evt == null)
        {
            return null;
        }

        // Junction names on the SAME context (one connection, consistent view).
        var tags = await context.EventTags.AsNoTracking()
            .Where(et => et.EventId == eventId)
            .Select(et => et.Tag.TagName)
            .ToListAsync();
        var people = await context.EventPeople.AsNoTracking()
            .Where(ep => ep.EventId == eventId)
            .Select(ep => ep.Person.Name)
            .ToListAsync();
        var locations = await context.EventLocations.AsNoTracking()
            .Where(el => el.EventId == eventId)
            .Select(el => el.Location.Name)
            .ToListAsync();

        return BuildSnapshot(evt, tags, people, locations);
    }

    /// <summary>Builds a snapshot from an in-memory event + junction names.</summary>
    public static EventSnapshot BuildSnapshot(
        Event evt,
        IEnumerable<string> tagNames,
        IEnumerable<string> peopleNames,
        IEnumerable<string> locationNames)
    {
        return new EventSnapshot
        {
            EventId = evt.EventId,
            Title = evt.Title,
            StartDate = evt.StartDate,
            EndDate = evt.EndDate,
            Description = evt.Description,
            Category = evt.Category,
            EraId = evt.EraId,
            Location = evt.Location,
            Confidence = evt.Confidence,
            AudioFilePath = evt.AudioFilePath,
            RawTranscript = evt.RawTranscript,
            ExtractionMetadata = evt.ExtractionMetadata,
            DatePrecision = evt.DatePrecision,
            EarliestPossible = evt.EarliestPossible,
            LatestPossible = evt.LatestPossible,
            Tags = SortedNames(tagNames),
            People = SortedNames(peopleNames),
            Locations = SortedNames(locationNames)
        };
    }

    /// <summary>
    /// Computes the changed_fields_csv between a pre-change snapshot and the
    /// post-change event, field by field over <see cref="ScalarFieldTokens"/>.
    /// Returns null when no scalar field changed.
    /// </summary>
    public static string? ComputeChangedFieldsCsv(EventSnapshot before, Event after)
    {
        var changed = new List<string>();

        void Check(string token, bool isChanged)
        {
            if (isChanged)
            {
                changed.Add(token);
            }
        }

        Check("title", !StringEquals(before.Title, after.Title));
        Check("start_date", before.StartDate != after.StartDate);
        Check("end_date", before.EndDate != after.EndDate);
        Check("description", !StringEquals(before.Description, after.Description));
        Check("category", !StringEquals(before.Category, after.Category));
        Check("era_id", !StringEquals(before.EraId, after.EraId));
        Check("location", !StringEquals(before.Location, after.Location));
        Check("date_precision", before.DatePrecision != after.DatePrecision);
        Check("earliest_possible", before.EarliestPossible != after.EarliestPossible);
        Check("latest_possible", before.LatestPossible != after.LatestPossible);
        Check("confidence", before.Confidence != after.Confidence);

        return changed.Count == 0 ? null : string.Join(",", changed);
    }

    /// <summary>Serializes a snapshot to the stored JSON form.</summary>
    public static string Serialize(EventSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);

    /// <summary>Deserializes a stored snapshot, or null when malformed.</summary>
    public static EventSnapshot? TryDeserialize(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EventSnapshot>(snapshotJson, SnapshotJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WriteAsync(EventSnapshot snapshot, RevisionKind kind, string? changedFieldsCsv)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.EventRevisions.Add(CreateRevision(snapshot, kind, changedFieldsCsv));
        await context.SaveChangesAsync();

        _logger.LogDebug("Wrote {Kind} revision for event {EventId}.", kind, snapshot.EventId);
    }

    private static EventRevision CreateRevision(EventSnapshot snapshot, RevisionKind kind, string? changedFieldsCsv)
    {
        return new EventRevision
        {
            RevisionId = Guid.NewGuid().ToString(),
            EventId = snapshot.EventId,
            RevisedAt = DateTime.UtcNow,
            RevisionKind = kind,
            SnapshotJson = Serialize(snapshot),
            ChangedFieldsCsv = changedFieldsCsv
        };
    }

    private static List<string> SortedNames(IEnumerable<string> names)
        => names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool StringEquals(string? a, string? b)
        => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
}
