using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Export service implementation for timeline data.
/// </summary>
public class ExportService : IExportService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ExportService> _logger;

    public ExportService(
        AppDbContext dbContext,
        ILogger<ExportService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Exports events to JSON format.
    /// </summary>
    public async Task ExportToJsonAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null, IProgress<int>? progress = null)
    {
        try
        {
            _logger.LogInformation("Exporting to JSON: {FilePath}", filePath);

            var events = await GetFilteredEventsAsync(startDate, endDate);
            progress?.Report(50);

            var exportData = new
            {
                ExportDate = DateTime.UtcNow,
                EventCount = events.Count,
                Events = events.Select(e => new
                {
                    e.EventId,
                    e.Title,
                    e.Description,
                    e.StartDate,
                    e.EndDate,
                    e.Category,
                    e.Location,
                    e.Tags,
                    e.CreatedAt,
                    e.UpdatedAt
                }).ToList()
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(exportData, options);
            await File.WriteAllTextAsync(filePath, json);

            progress?.Report(100);
            _logger.LogInformation("Exported {Count} events to JSON", events.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to JSON");
            throw;
        }
    }

    /// <summary>
    /// Exports events to CSV format.
    /// </summary>
    public async Task ExportToCsvAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null, IProgress<int>? progress = null)
    {
        try
        {
            _logger.LogInformation("Exporting to CSV: {FilePath}", filePath);

            var events = await GetFilteredEventsAsync(startDate, endDate);
            progress?.Report(50);

            var csv = new StringBuilder();

            // Header
            csv.AppendLine("EventId,Title,Description,StartDate,EndDate,Category,Location,Tags,CreatedAt");

            // Rows
            foreach (var evt in events)
            {
                var tags = string.Join(";", evt.Tags?.Select(t => t.Name) ?? Array.Empty<string>());
                csv.AppendLine($"\"{evt.EventId}\",\"{EscapeCsv(evt.Title)}\",\"{EscapeCsv(evt.Description)}\",\"{evt.StartDate:yyyy-MM-dd}\",\"{evt.EndDate:yyyy-MM-dd}\",\"{evt.Category}\",\"{EscapeCsv(evt.Location)}\",\"{tags}\",\"{evt.CreatedAt:yyyy-MM-dd HH:mm:ss}\"");
            }

            await File.WriteAllTextAsync(filePath, csv.ToString());

            progress?.Report(100);
            _logger.LogInformation("Exported {Count} events to CSV", events.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to CSV");
            throw;
        }
    }

    /// <summary>
    /// Exports events to Markdown format.
    /// </summary>
    public async Task ExportToMarkdownAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null, IProgress<int>? progress = null)
    {
        try
        {
            _logger.LogInformation("Exporting to Markdown: {FilePath}", filePath);

            var events = await GetFilteredEventsAsync(startDate, endDate);
            progress?.Report(50);

            var md = new StringBuilder();
            md.AppendLine("# Memory Timeline Export");
            md.AppendLine();
            md.AppendLine($"**Exported:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            md.AppendLine($"**Event Count:** {events.Count}");
            md.AppendLine();
            md.AppendLine("---");
            md.AppendLine();

            // Group by year
            var eventsByYear = events
                .OrderBy(e => e.StartDate)
                .GroupBy(e => e.StartDate.Year);

            foreach (var yearGroup in eventsByYear)
            {
                md.AppendLine($"## {yearGroup.Key}");
                md.AppendLine();

                foreach (var evt in yearGroup)
                {
                    md.AppendLine($"### {evt.Title}");
                    md.AppendLine();
                    md.AppendLine($"**Date:** {evt.StartDate:yyyy-MM-dd}");

                    if (evt.EndDate.HasValue && evt.EndDate != evt.StartDate)
                    {
                        md.AppendLine($"**End Date:** {evt.EndDate:yyyy-MM-dd}");
                    }

                    md.AppendLine($"**Category:** {evt.Category}");

                    if (!string.IsNullOrWhiteSpace(evt.Location))
                    {
                        md.AppendLine($"**Location:** {evt.Location}");
                    }

                    if (evt.Tags?.Any() == true)
                    {
                        md.AppendLine($"**Tags:** {string.Join(", ", evt.Tags.Select(t => t.Name))}");
                    }

                    if (!string.IsNullOrWhiteSpace(evt.Description))
                    {
                        md.AppendLine();
                        md.AppendLine(evt.Description);
                    }

                    md.AppendLine();
                    md.AppendLine("---");
                    md.AppendLine();
                }
            }

            await File.WriteAllTextAsync(filePath, md.ToString());

            progress?.Report(100);
            _logger.LogInformation("Exported {Count} events to Markdown", events.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to Markdown");
            throw;
        }
    }

    /// <summary>
    /// Exports the entire database (events, eras, tags, etc.).
    /// </summary>
    public async Task ExportFullDatabaseAsync(string filePath, IProgress<int>? progress = null)
    {
        try
        {
            _logger.LogInformation("Exporting full database to: {FilePath}", filePath);

            progress?.Report(10);
            var events = await _dbContext.Events
                .Include(e => e.Tags)
                .AsNoTracking()
                .ToListAsync();

            progress?.Report(30);
            var eras = await _dbContext.Eras
                .AsNoTracking()
                .ToListAsync();

            progress?.Report(50);
            var tags = await _dbContext.Tags
                .AsNoTracking()
                .ToListAsync();

            progress?.Report(70);
            var exportData = new
            {
                ExportDate = DateTime.UtcNow,
                Version = "1.0",
                Events = events.Select(e => new
                {
                    e.EventId,
                    e.Title,
                    e.Description,
                    e.StartDate,
                    e.EndDate,
                    e.Category,
                    e.Location,
                    e.Confidence,
                    Tags = e.Tags?.Select(t => t.Name).ToList(),
                    e.CreatedAt,
                    e.UpdatedAt
                }).ToList(),
                Eras = eras.Select(era => new
                {
                    era.EraId,
                    era.Name,
                    era.Description,
                    era.StartDate,
                    era.EndDate,
                    era.Color,
                    era.CreatedAt
                }).ToList(),
                Tags = tags.Select(t => new
                {
                    t.TagId,
                    t.Name,
                    t.Color,
                    t.CreatedAt
                }).ToList()
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(exportData, options);
            await File.WriteAllTextAsync(filePath, json);

            progress?.Report(100);
            _logger.LogInformation("Exported full database: {Events} events, {Eras} eras, {Tags} tags",
                events.Count, eras.Count, tags.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting full database");
            throw;
        }
    }

    #region Private Methods

    private async Task<List<Event>> GetFilteredEventsAsync(DateTime? startDate, DateTime? endDate)
    {
        var query = _dbContext.Events
            .Include(e => e.Tags)
            .AsNoTracking()
            .AsQueryable();

        if (startDate.HasValue)
        {
            query = query.Where(e => e.StartDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(e => e.StartDate <= endDate.Value);
        }

        return await query
            .OrderBy(e => e.StartDate)
            .ToListAsync();
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("\"", "\"\"");
    }

    #endregion
}
