using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Import service implementation for timeline data.
/// </summary>
public class ImportService : IImportService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ImportService> _logger;

    public ImportService(
        AppDbContext dbContext,
        ILogger<ImportService> logger)
    {
        _dbContext = dbContext;
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
            var importData = JsonSerializer.Deserialize<JsonImportData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (importData == null)
            {
                result.ErrorMessage = "Failed to parse JSON file";
                return result;
            }

            progress?.Report((30, "Validating data..."));

            // Import events
            if (importData.Events?.Any() == true)
            {
                progress?.Report((40, $"Importing {importData.Events.Count} events..."));
                await ImportEventsAsync(importData.Events, options, result);
            }

            // Import eras
            if (options.ImportEras && importData.Eras?.Any() == true)
            {
                progress?.Report((70, $"Importing {importData.Eras.Count} eras..."));
                await ImportErasAsync(importData.Eras, result);
            }

            // Import tags
            if (options.ImportTags && importData.Tags?.Any() == true)
            {
                progress?.Report((85, $"Importing {importData.Tags.Count} tags..."));
                await ImportTagsAsync(importData.Tags, result);
            }

            await _dbContext.SaveChangesAsync();

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
        // Electron export format is similar to our JSON format
        // We can reuse the ImportFromJsonAsync method with some preprocessing
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
            var importData = JsonSerializer.Deserialize<JsonImportData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

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

                    if (evt.StartDate == default)
                    {
                        result.Warnings.Add($"Event '{evt.Title}' has invalid start date");
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

    private async Task ImportEventsAsync(List<JsonEvent> events, ImportOptions options, ImportResult result)
    {
        foreach (var jsonEvent in events)
        {
            try
            {
                // Check for duplicates
                var existingEvent = await _dbContext.Events
                    .FirstOrDefaultAsync(e => e.Title == jsonEvent.Title && e.StartDate == jsonEvent.StartDate);

                if (existingEvent != null)
                {
                    switch (options.ConflictResolution)
                    {
                        case ConflictResolution.Skip:
                            result.EventsSkipped++;
                            continue;

                        case ConflictResolution.Overwrite:
                            UpdateEventFromJson(existingEvent, jsonEvent);
                            result.EventsImported++;
                            break;

                        case ConflictResolution.CreateDuplicate:
                            await CreateEventFromJson(jsonEvent);
                            result.EventsImported++;
                            break;

                        case ConflictResolution.Merge:
                            if (jsonEvent.UpdatedAt > existingEvent.UpdatedAt)
                            {
                                UpdateEventFromJson(existingEvent, jsonEvent);
                            }
                            result.EventsImported++;
                            break;
                    }
                }
                else
                {
                    await CreateEventFromJson(jsonEvent);
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

    private async Task CreateEventFromJson(JsonEvent jsonEvent)
    {
        var evt = new Event
        {
            EventId = string.IsNullOrWhiteSpace(jsonEvent.EventId) ? Guid.NewGuid().ToString() : jsonEvent.EventId,
            Title = jsonEvent.Title,
            Description = jsonEvent.Description,
            StartDate = jsonEvent.StartDate,
            EndDate = jsonEvent.EndDate,
            Category = jsonEvent.Category,
            Location = jsonEvent.Location,
            Confidence = jsonEvent.Confidence,
            CreatedAt = jsonEvent.CreatedAt != default ? jsonEvent.CreatedAt : DateTime.UtcNow,
            UpdatedAt = jsonEvent.UpdatedAt != default ? jsonEvent.UpdatedAt : DateTime.UtcNow
        };

        // Handle tags
        if (jsonEvent.Tags?.Any() == true)
        {
            evt.Tags = new List<Tag>();
            foreach (var tagName in jsonEvent.Tags)
            {
                var tag = await _dbContext.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
                if (tag == null)
                {
                    tag = new Tag
                    {
                        TagId = Guid.NewGuid().ToString(),
                        Name = tagName,
                        CreatedAt = DateTime.UtcNow
                    };
                    _dbContext.Tags.Add(tag);
                }
                evt.Tags.Add(tag);
            }
        }

        _dbContext.Events.Add(evt);
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

    private async Task ImportErasAsync(List<JsonEra> eras, ImportResult result)
    {
        foreach (var jsonEra in eras)
        {
            try
            {
                var existingEra = await _dbContext.Eras
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
                        Color = jsonEra.Color,
                        CreatedAt = jsonEra.CreatedAt != default ? jsonEra.CreatedAt : DateTime.UtcNow
                    };

                    _dbContext.Eras.Add(era);
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

    private async Task ImportTagsAsync(List<JsonTag> tags, ImportResult result)
    {
        foreach (var jsonTag in tags)
        {
            try
            {
                var existingTag = await _dbContext.Tags
                    .FirstOrDefaultAsync(t => t.Name == jsonTag.Name);

                if (existingTag == null)
                {
                    var tag = new Tag
                    {
                        TagId = string.IsNullOrWhiteSpace(jsonTag.TagId) ? Guid.NewGuid().ToString() : jsonTag.TagId,
                        Name = jsonTag.Name,
                        Color = jsonTag.Color,
                        CreatedAt = jsonTag.CreatedAt != default ? jsonTag.CreatedAt : DateTime.UtcNow
                    };

                    _dbContext.Tags.Add(tag);
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
