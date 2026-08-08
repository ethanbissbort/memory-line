using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Import service implementation for timeline data.
/// Creates a short-lived DbContext per operation via IDbContextFactory.
/// </summary>
public class ImportService : IImportService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ImportService> _logger;
    private readonly EventRevisionWriter? _revisionWriter;
    private readonly ITimelineProjectionPublisher? _projectionPublisher;

    public ImportService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ImportService> logger,
        EventRevisionWriter? revisionWriter = null,
        ITimelineProjectionPublisher? projectionPublisher = null)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _revisionWriter = revisionWriter;
        _projectionPublisher = projectionPublisher;
    }

    /// <summary>
    /// Imports events from a JSON file.
    /// </summary>
    public async Task<ImportResult> ImportFromJsonAsync(string filePath, ImportOptions? options = null, IProgress<(int, string)>? progress = null)
    {
        options ??= new ImportOptions();
        var result = new ImportResult();

        try
        {
            _logger.LogInformation("Importing from JSON: {FilePath}", filePath);
            progress?.Report((10, "Reading file..."));

            var json = await File.ReadAllTextAsync(filePath);
            var importData = ParseImportJson(json);

            if (importData == null)
            {
                result.ErrorMessage = "Failed to parse JSON file";
                return result;
            }

            progress?.Report((20, "Validating data..."));

            await using var dbContext = await _contextFactory.CreateDbContextAsync();

            // Back up the database before making any changes. The backup is the
            // safety net for the app's only destructive bulk operation, so a
            // backup failure ABORTS the import instead of degrading to a warning.
            // Non-SQLite providers (e.g. the EF InMemory provider used in tests)
            // have no database file to back up, so the backup is skipped there.
            var isSqliteProvider = dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
            if (options.CreateBackup && !isSqliteProvider)
            {
                _logger.LogDebug("Skipping pre-import backup: provider {Provider} has no database file", dbContext.Database.ProviderName);
            }
            if (options.CreateBackup && isSqliteProvider)
            {
                progress?.Report((25, "Creating database backup..."));
                if (!TryCreateDatabaseBackup(dbContext, out var backupError))
                {
                    result.ErrorMessage = $"Import aborted: could not create a database backup ({backupError}). No changes were made.";
                    result.Errors.Add(result.ErrorMessage);
                    return result;
                }
            }

            progress?.Report((30, "Importing data..."));

            // Events created by this import, for post-save revision writes.
            var createdEvents = new List<Event>();

            // Rows this import wrote, projected onto the sync feed once the save
            // commits (see PublishImportedProjectionsAsync). The event list is
            // deliberately not createdEvents: revisions are for new events only,
            // while a companion also has to hear about the ones an import
            // overwrote, and neither list holds rows the import merely skipped.
            var importedEventIds = new List<string>();
            var importedEraIds = new List<string>();

            // Import events
            if (importData.Events?.Any() == true)
            {
                progress?.Report((40, $"Importing {importData.Events.Count} events..."));
                await ImportEventsAsync(dbContext, importData.Events, options, result, createdEvents, importedEventIds);
            }

            // Import eras
            if (options.ImportEras && importData.Eras?.Any() == true)
            {
                progress?.Report((70, $"Importing {importData.Eras.Count} eras..."));
                await ImportErasAsync(dbContext, importData.Eras, result, importedEraIds);
            }

            // Import tags. Nothing is collected for the sync feed here: a tag is
            // not a projected entity, it reaches a companion denormalised into
            // the events that carry it, so a tag row with no event to hang on
            // has nothing to project until one links to it.
            if (options.ImportTags && importData.Tags?.Any() == true)
            {
                progress?.Report((85, $"Importing {importData.Tags.Count} tags..."));
                await ImportTagsAsync(dbContext, importData.Tags, result);
            }

            await dbContext.SaveChangesAsync();

            // Revision history (F12): CreateEventFromJson is the single event
            // creation point of the import, so newly created events get one
            // Imported revision each AFTER the save committed them (the writer
            // reloads each event fresh, so snapshots include imported tag
            // names). Gated on revision_history_enabled inside the writer;
            // failures are logged there and never fail the import.
            if (_revisionWriter != null)
            {
                foreach (var createdEvent in createdEvents)
                {
                    await _revisionWriter.TryWriteForEventIdAsync(
                        createdEvent.EventId, RevisionKind.Imported);
                }
            }

            // Until now an import was the one whole user action a companion
            // never saw: rows went straight to the context and nothing reached
            // the sync feed (design §19 Phase 3). The pass can be long on a big
            // file, so it gets its own progress step rather than hiding behind
            // a bar that already says 100%.
            if (_projectionPublisher != null && (importedEventIds.Count > 0 || importedEraIds.Count > 0))
            {
                progress?.Report((95, "Publishing to companion devices..."));
                await PublishImportedProjectionsAsync(importedEventIds, importedEraIds);
            }

            result.Success = true;
            progress?.Report((100, "Import complete"));

            _logger.LogInformation("Import completed: {EventsImported} events, {ErasImported} eras, {TagsImported} tags",
                result.EventsImported, result.ErasImported, result.TagsImported);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing from JSON");
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Validates an import file without importing.
    /// </summary>
    public async Task<ValidationResult> ValidateImportFileAsync(string filePath)
    {
        var result = new ValidationResult();

        try
        {
            if (!File.Exists(filePath))
            {
                result.Errors.Add("File does not exist");
                return result;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var importData = ParseImportJson(json);

            if (importData == null)
            {
                result.Errors.Add("Invalid JSON format");
                return result;
            }

            result.Format = "JSON";
            result.EventCount = importData.Events?.Count ?? 0;
            result.EraCount = importData.Eras?.Count ?? 0;
            result.TagCount = importData.Tags?.Count ?? 0;

            // Validate events
            if (importData.Events?.Any() == true)
            {
                foreach (var evt in importData.Events)
                {
                    if (string.IsNullOrWhiteSpace(evt.Title))
                    {
                        result.Warnings.Add($"Event with ID {evt.EventId} has no title");
                    }

                    // A default StartDate means the date failed to bind or parse.
                    // Importing it would silently place the event in year 0001,
                    // so this is an error, not a warning.
                    if (evt.StartDate == default)
                    {
                        result.Errors.Add($"Event '{evt.Title}' has a missing or invalid start date and cannot be imported");
                    }
                }
            }

            result.IsValid = !result.Errors.Any();
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Validation error: {ex.Message}");
        }

        return result;
    }

    #region Private Methods

    /// <summary>
    /// Creates a timestamped .bak snapshot of the SQLite database next to it
    /// using SQLite's online backup API (SqliteConnection.BackupDatabase).
    /// The app runs the database in WAL mode, so a raw File.Copy of the main
    /// .db file would miss every transaction still in the -wal sidecar (and
    /// could be torn if it raced a checkpoint); the backup API takes a read
    /// transaction on the source and produces a consistent snapshot that
    /// includes WAL contents. Returns false (with a reason) on failure so the
    /// caller can abort the import.
    /// </summary>
    private bool TryCreateDatabaseBackup(AppDbContext dbContext, out string? error)
    {
        error = null;

        try
        {
            var databasePath = dbContext.Database.GetDbConnection().DataSource;

            if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            {
                _logger.LogError("Cannot back up database: file not found at '{Path}'", databasePath);
                error = $"database file not found at '{databasePath}'";
                return false;
            }

            var backupPath = $"{databasePath}.{DateTime.Now:yyyyMMdd_HHmmss}.bak";

            // Pooling=False so neither connection lingers in the connection pool
            // holding a handle on the .db or the new .bak after the backup.
            using var source = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            using var destination = new SqliteConnection($"Data Source={backupPath};Pooling=False");
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);

            _logger.LogInformation("Database backed up to {BackupPath}", backupPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create database backup before import");
            error = ex.Message;
            return false;
        }
    }

    private async Task ImportEventsAsync(
        AppDbContext dbContext,
        List<JsonEvent> events,
        ImportOptions options,
        ImportResult result,
        List<Event> createdEvents,
        List<string> importedEventIds)
    {
        foreach (var jsonEvent in events)
        {
            try
            {
                // Never import an event whose start date failed to bind or parse:
                // it would land on 0001-01-01, invisible in any timeline viewport,
                // with the real date permanently lost. Skip it as a counted error.
                if (jsonEvent.StartDate == default)
                {
                    _logger.LogWarning("Skipping event with missing or invalid start date: {Title}", jsonEvent.Title);
                    result.Errors.Add($"Event '{jsonEvent.Title}' skipped: start date is missing or invalid");
                    result.EventsSkipped++;
                    continue;
                }

                // Check for duplicates (same title + start date)
                var existingEvent = await dbContext.Events
                    .FirstOrDefaultAsync(e => e.Title == jsonEvent.Title && e.StartDate == jsonEvent.StartDate);

                if (existingEvent != null)
                {
                    if (options.UpdateExisting)
                    {
                        UpdateEventFromJson(existingEvent, jsonEvent, importedEventIds);
                        result.EventsUpdated++;
                        continue;
                    }

                    // SkipDuplicates is a legacy convenience that defaults to true;
                    // it must not shadow an explicitly chosen non-Skip strategy.
                    if (options.SkipDuplicates && options.ConflictResolution == ConflictResolution.Skip)
                    {
                        result.EventsSkipped++;
                        continue;
                    }

                    switch (options.ConflictResolution)
                    {
                        case ConflictResolution.Skip:
                            result.EventsSkipped++;
                            continue;

                        case ConflictResolution.Overwrite:
                            UpdateEventFromJson(existingEvent, jsonEvent, importedEventIds);
                            result.EventsUpdated++;
                            break;

                        case ConflictResolution.CreateDuplicate:
                            await CreateEventFromJson(dbContext, jsonEvent, result, createdEvents, importedEventIds);
                            result.EventsImported++;
                            break;

                        case ConflictResolution.Merge:
                            if (jsonEvent.UpdatedAt > existingEvent.UpdatedAt)
                            {
                                UpdateEventFromJson(existingEvent, jsonEvent, importedEventIds);
                                result.EventsUpdated++;
                            }
                            else
                            {
                                result.EventsSkipped++;
                            }
                            break;
                    }
                }
                else
                {
                    await CreateEventFromJson(dbContext, jsonEvent, result, createdEvents, importedEventIds);
                    result.EventsImported++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import event: {Title}", jsonEvent.Title);
                result.Warnings.Add($"Failed to import event '{jsonEvent.Title}': {ex.Message}");
            }
        }
    }

    private async Task CreateEventFromJson(
        AppDbContext dbContext,
        JsonEvent jsonEvent,
        ImportResult result,
        List<Event> createdEvents,
        List<string> importedEventIds)
    {
        var evt = new Event
        {
            EventId = string.IsNullOrWhiteSpace(jsonEvent.EventId) ? Guid.NewGuid().ToString() : jsonEvent.EventId,
            Title = jsonEvent.Title,
            Description = jsonEvent.Description,
            StartDate = jsonEvent.StartDate,
            EndDate = jsonEvent.EndDate,
            Category = jsonEvent.Category?.ToLowerInvariant(),
            Location = jsonEvent.Location,
            Confidence = jsonEvent.Confidence,
            CreatedAt = jsonEvent.CreatedAt != default ? jsonEvent.CreatedAt : DateTime.UtcNow,
            UpdatedAt = jsonEvent.UpdatedAt != default ? jsonEvent.UpdatedAt : DateTime.UtcNow
        };

        // Handle tags
        if (jsonEvent.Tags?.Any() == true)
        {
            foreach (var tagName in jsonEvent.Tags)
            {
                // Query on the mapped TagName column ([NotMapped] Tag.Name is
                // untranslatable in EF), checking rows added earlier this import too
                var tag = dbContext.Tags.Local
                        .FirstOrDefault(t => string.Equals(t.TagName, tagName, StringComparison.OrdinalIgnoreCase))
                    ?? await dbContext.Tags.FirstOrDefaultAsync(t => t.TagName == tagName);

                if (tag == null)
                {
                    tag = new Tag
                    {
                        TagId = Guid.NewGuid().ToString(),
                        TagName = tagName,
                        CreatedAt = DateTime.UtcNow
                    };
                    dbContext.Tags.Add(tag);
                    // Tags created via an event's tag list count as imported so
                    // TagsImported reflects every new tag this import produced.
                    result.TagsImported++;
                }
                evt.EventTags.Add(new EventTag
                {
                    EventId = evt.EventId,
                    TagId = tag.TagId,
                    Tag = tag,
                    Event = evt,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        dbContext.Events.Add(evt);
        createdEvents.Add(evt);
        importedEventIds.Add(evt.EventId);
    }

    /// <summary>
    /// Applies an imported record over an existing event and records its id for
    /// the sync feed. The recording lives here, next to the mutation, rather
    /// than at the three conflict branches that call it: an overwritten event
    /// looks exactly like an edit to a companion, and a branch added later must
    /// not be able to change the archive without the feed hearing about it.
    /// </summary>
    private void UpdateEventFromJson(Event existingEvent, JsonEvent jsonEvent, List<string> importedEventIds)
    {
        existingEvent.Title = jsonEvent.Title;
        existingEvent.Description = jsonEvent.Description;
        existingEvent.StartDate = jsonEvent.StartDate;
        existingEvent.EndDate = jsonEvent.EndDate;
        existingEvent.Category = jsonEvent.Category;
        existingEvent.Location = jsonEvent.Location;
        existingEvent.Confidence = jsonEvent.Confidence;
        existingEvent.UpdatedAt = DateTime.UtcNow;
        importedEventIds.Add(existingEvent.EventId);
    }

    private async Task ImportErasAsync(
        AppDbContext dbContext, List<JsonEra> eras, ImportResult result, List<string> importedEraIds)
    {
        foreach (var jsonEra in eras)
        {
            try
            {
                var existingEra = await dbContext.Eras
                    .FirstOrDefaultAsync(e => e.Name == jsonEra.Name);

                if (existingEra == null)
                {
                    var era = new Era
                    {
                        EraId = string.IsNullOrWhiteSpace(jsonEra.EraId) ? Guid.NewGuid().ToString() : jsonEra.EraId,
                        Name = jsonEra.Name,
                        Description = jsonEra.Description,
                        StartDate = jsonEra.StartDate,
                        EndDate = jsonEra.EndDate,
                        Color = jsonEra.Color ?? "#000000",
                        CreatedAt = jsonEra.CreatedAt != default ? jsonEra.CreatedAt : DateTime.UtcNow
                    };

                    dbContext.Eras.Add(era);
                    result.ErasImported++;

                    // Only created eras are recorded. An era whose name already
                    // exists is dropped entirely — not merged, not updated — so
                    // there is no change to project, and publishing the existing
                    // row would be an upsert of something the companion already
                    // holds unchanged. That is an intentional omission, not a
                    // missed publish site.
                    importedEraIds.Add(era.EraId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import era: {Name}", jsonEra.Name);
                result.Warnings.Add($"Failed to import era '{jsonEra.Name}': {ex.Message}");
            }
        }
    }

    private async Task ImportTagsAsync(AppDbContext dbContext, List<JsonTag> tags, ImportResult result)
    {
        foreach (var jsonTag in tags)
        {
            try
            {
                // Query on the mapped TagName column ([NotMapped] Tag.Name is
                // untranslatable in EF), checking rows added earlier this import too
                var existingTag = dbContext.Tags.Local
                        .FirstOrDefault(t => string.Equals(t.TagName, jsonTag.Name, StringComparison.OrdinalIgnoreCase))
                    ?? await dbContext.Tags.FirstOrDefaultAsync(t => t.TagName == jsonTag.Name);

                if (existingTag == null)
                {
                    var tag = new Tag
                    {
                        TagId = string.IsNullOrWhiteSpace(jsonTag.TagId) ? Guid.NewGuid().ToString() : jsonTag.TagId,
                        TagName = jsonTag.Name,
                        Color = jsonTag.Color,
                        CreatedAt = jsonTag.CreatedAt != default ? jsonTag.CreatedAt : DateTime.UtcNow
                    };

                    dbContext.Tags.Add(tag);
                    result.TagsImported++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import tag: {Name}", jsonTag.Name);
                result.Warnings.Add($"Failed to import tag '{jsonTag.Name}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Projects everything the import wrote onto the sync feed, in one pass over
    /// the ids the loops collected (design §19 Phase 3). Events and eras are the
    /// whole list because they are the only projected entities an import writes:
    /// it creates no people and no pending events, and tags travel inside the
    /// events that carry them.
    ///
    /// <para><b>Why at the end and not per row.</b> Not primarily speed: the
    /// import stages the whole file into one context and commits it with a
    /// single SaveChangesAsync, and the publisher reads each row back through
    /// repositories that open their own connections. Called from inside a loop
    /// it would find nothing committed, log "no longer exists" at debug, and
    /// publish silently nothing — the exact failure this change exists to fix.
    /// Splitting the import into committed batches so it could publish per batch
    /// would buy an earlier feed by giving up the all-or-nothing commit that the
    /// mandatory pre-import backup exists to guarantee, which is the wrong way
    /// round for the app's only destructive bulk operation.</para>
    ///
    /// <para><b>What holding the ids costs.</b> A GUID string is on the order of
    /// a hundred bytes, so even a 10,000-event file adds about a megabyte — next
    /// to a change tracker that is already holding every imported entity, and a
    /// createdEvents list holding the events themselves, for the whole import.
    /// There is nothing to re-query instead: no column marks a row as "written
    /// by this import".</para>
    ///
    /// <para><b>What publishing costs.</b> Each event projection re-reads the
    /// event with its navigations, counts its media, compares against the last
    /// published payload and inserts one outbox row in its own transaction —
    /// four round trips, one of them a commit — and each era costs three. An
    /// import of N events and M new eras therefore adds roughly 4N+3M queries
    /// and N+M outbox rows, which LocalOutboxPublisher then drains 100 at a
    /// time. On a large file that is real time and a real burst, and it is the
    /// price of the import being visible on a phone at all; there is deliberately
    /// no "skip publishing for large imports" option, because an import that
    /// silently lands nothing is the bug, not the optimisation.</para>
    ///
    /// <para><b>Failures are counted, never fatal, and summarised once.</b> The
    /// archive is committed by the time this runs and an import is the slowest,
    /// least repeatable thing a user can ask for; losing one to an unwritable
    /// outbox row would be indefensible. Per-row logging is equally wrong here:
    /// the realistic failure (outbox locked, sync misconfigured) fails every row,
    /// and thousands of identical warnings would bury the import's own errors.
    /// Every unpublished entity self-corrects the next time it is edited through
    /// a path that publishes.</para>
    /// </summary>
    private async Task PublishImportedProjectionsAsync(List<string> eventIds, List<string> eraIds)
    {
        var publisher = _projectionPublisher;
        if (publisher == null)
        {
            return;
        }

        var failures = 0;
        Exception? firstFailure = null;

        async Task TryPublishAsync(Func<Task> publish)
        {
            try
            {
                await publish();
            }
            catch (Exception ex)
            {
                failures++;
                firstFailure ??= ex;
            }
        }

        // Eras first: there is a handful of them against potentially thousands
        // of events, so an interrupted pass has at least delivered the cheap
        // half — the backdrop the events are drawn against. Nothing here needs
        // the order for referential reasons; an imported event carries no era.
        foreach (var eraId in eraIds)
        {
            await TryPublishAsync(() => publisher.PublishEraAsync(eraId));
        }

        // Distinct: a file that lists the same event twice under an overwriting
        // strategy records the id once per write, and each repeat would spend
        // four queries to conclude the projection had not changed. Materialised
        // so the summary below counts what was attempted, not what was collected.
        var distinctEventIds = eventIds.Distinct(StringComparer.Ordinal).ToList();
        foreach (var eventId in distinctEventIds)
        {
            await TryPublishAsync(() => publisher.PublishEventAsync(eventId));
        }

        if (firstFailure != null)
        {
            _logger.LogWarning(firstFailure,
                "{FailureCount} of {TotalCount} timeline projections could not be published for this import; " +
                "the imported data is committed and each entity republishes when it is next edited (first failure attached)",
                failures, eraIds.Count + distinctEventIds.Count);
        }
    }

    #endregion

    #region JSON Parsing

    private static readonly JsonSerializerOptions ImportSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new LenientDateTimeConverter(),
            new LenientNullableDateTimeConverter()
        }
    };

    /// <summary>
    /// Keys whose snake_case name does not mechanically translate to the DTO
    /// property name. Legacy exports write raw SQL column names:
    /// tags.tag_name maps to JsonTag.Name and eras.color_code to JsonEra.Color.
    /// </summary>
    private static readonly Dictionary<string, string> SnakeCaseKeyOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tag_name"] = "Name",
        ["color_code"] = "Color"
    };

    /// <summary>
    /// Parses import JSON accepting both supported shapes:
    /// - the native export (camelCase keys, handled by case-insensitive binding), and
    /// - legacy exports that emit snake_case SQL column names (event_id,
    ///   start_date, end_date, created_at, updated_at, tag_name, color_code, ...).
    /// The parsed tree is normalized by renaming snake_case keys to PascalCase
    /// before binding, so both shapes deserialize into the same DTOs. Keys
    /// without underscores are left untouched.
    /// </summary>
    private static JsonImportData? ParseImportJson(string json)
    {
        var root = JsonNode.Parse(json);
        if (root == null)
        {
            return null;
        }

        NormalizeKeysToPascalCase(root);
        return root.Deserialize<JsonImportData>(ImportSerializerOptions);
    }

    private static void NormalizeKeysToPascalCase(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                // Snapshot the key list: renaming mutates the object.
                foreach (var propertyName in obj.Select(p => p.Key).ToList())
                {
                    var value = obj[propertyName];
                    NormalizeKeysToPascalCase(value);

                    if (!propertyName.Contains('_'))
                    {
                        continue;
                    }

                    var newName = SnakeCaseKeyOverrides.TryGetValue(propertyName, out var mapped)
                        ? mapped
                        : ToPascalCase(propertyName);

                    // Never clobber an existing key (a file that somehow carries
                    // both shapes keeps its native value).
                    if (newName != propertyName && !obj.ContainsKey(newName))
                    {
                        obj.Remove(propertyName);
                        obj[newName] = value;
                    }
                }
                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    NormalizeKeysToPascalCase(item);
                }
                break;
        }
    }

    private static string ToPascalCase(string snakeCaseName)
    {
        var parts = snakeCaseName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return snakeCaseName;
        }

        var builder = new StringBuilder(snakeCaseName.Length);
        foreach (var part in parts)
        {
            builder.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                builder.Append(part.AsSpan(1));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a date token leniently. Legacy exports store SQLite TEXT
    /// dates ("2015-03-01", "2024-01-01 12:00:00") that System.Text.Json's
    /// strict ISO 8601 converter rejects, which would fail the whole file.
    /// Unparseable values return null so a bad date surfaces as a per-record
    /// validation error instead of aborting the entire import.
    /// </summary>
    private static DateTime? TryReadDateTime(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            if (reader.TryGetDateTime(out var value))
            {
                return value;
            }

            var text = reader.GetString();
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value))
            {
                return value;
            }

            return null;
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        reader.Skip();
        return null;
    }

    private sealed class LenientDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            // default(DateTime) is treated as "missing" by the import validation
            => TryReadDateTime(ref reader) ?? default;

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }

    private sealed class LenientNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => TryReadDateTime(ref reader);

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteStringValue(value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    #endregion

    #region DTOs

    private class JsonImportData
    {
        public DateTime ExportDate { get; set; }
        public string? Version { get; set; }
        public List<JsonEvent>? Events { get; set; }
        public List<JsonEra>? Eras { get; set; }
        public List<JsonTag>? Tags { get; set; }
    }

    private class JsonEvent
    {
        public string EventId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Category { get; set; }
        public string? Location { get; set; }
        public double? Confidence { get; set; }
        public List<string>? Tags { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private class JsonEra
    {
        public string EraId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Color { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private class JsonTag
    {
        public string TagId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    #endregion
}
