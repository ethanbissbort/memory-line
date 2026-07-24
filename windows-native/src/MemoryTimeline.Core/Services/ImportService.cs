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

    public ImportService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ImportService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
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
            if (options.CreateBackup)
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

            // Import events
            if (importData.Events?.Any() == true)
            {
                progress?.Report((40, $"Importing {importData.Events.Count} events..."));
                await ImportEventsAsync(dbContext, importData.Events, options, result);
            }

            // Import eras
            if (options.ImportEras && importData.Eras?.Any() == true)
            {
                progress?.Report((70, $"Importing {importData.Eras.Count} eras..."));
                await ImportErasAsync(dbContext, importData.Eras, result);
            }

            // Import tags
            if (options.ImportTags && importData.Tags?.Any() == true)
            {
                progress?.Report((85, $"Importing {importData.Tags.Count} tags..."));
                await ImportTagsAsync(dbContext, importData.Tags, result);
            }

            await dbContext.SaveChangesAsync();

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
    /// Imports data from Electron database export.
    /// </summary>
    public async Task<ImportResult> ImportFromElectronAsync(string filePath, ImportOptions? options = null, IProgress<(int, string)>? progress = null)
    {
        // The legacy Electron export emits snake_case keys (event_id, start_date,
        // tag_name, color_code, ...). ImportFromJsonAsync normalizes those to
        // PascalCase before binding, so both formats go through the same path.
        return await ImportFromJsonAsync(filePath, options, progress);
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

    private async Task ImportEventsAsync(AppDbContext dbContext, List<JsonEvent> events, ImportOptions options, ImportResult result)
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
                    // The simple boolean options take precedence, then the
                    // ConflictResolution strategy decides the remaining cases.
                    if (options.UpdateExisting)
                    {
                        UpdateEventFromJson(existingEvent, jsonEvent);
                        result.EventsUpdated++;
                        continue;
                    }

                    if (options.SkipDuplicates)
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
                            UpdateEventFromJson(existingEvent, jsonEvent);
                            result.EventsUpdated++;
                            break;

                        case ConflictResolution.CreateDuplicate:
                            await CreateEventFromJson(dbContext, jsonEvent);
                            result.EventsImported++;
                            break;

                        case ConflictResolution.Merge:
                            if (jsonEvent.UpdatedAt > existingEvent.UpdatedAt)
                            {
                                UpdateEventFromJson(existingEvent, jsonEvent);
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
                    await CreateEventFromJson(dbContext, jsonEvent);
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

    private async Task CreateEventFromJson(AppDbContext dbContext, JsonEvent jsonEvent)
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
    }

    private void UpdateEventFromJson(Event existingEvent, JsonEvent jsonEvent)
    {
        existingEvent.Title = jsonEvent.Title;
        existingEvent.Description = jsonEvent.Description;
        existingEvent.StartDate = jsonEvent.StartDate;
        existingEvent.EndDate = jsonEvent.EndDate;
        existingEvent.Category = jsonEvent.Category;
        existingEvent.Location = jsonEvent.Location;
        existingEvent.Confidence = jsonEvent.Confidence;
        existingEvent.UpdatedAt = DateTime.UtcNow;
    }

    private async Task ImportErasAsync(AppDbContext dbContext, List<JsonEra> eras, ImportResult result)
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
    /// property name. The legacy Electron export writes raw SQL column names:
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
    /// - the legacy Electron export (snake_case SQL column names: event_id,
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
    /// Reads a date token leniently. The Electron export stores SQLite TEXT
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
