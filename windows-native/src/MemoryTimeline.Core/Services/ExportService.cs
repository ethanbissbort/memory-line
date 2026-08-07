using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Export service implementation for timeline data.
/// Creates a short-lived DbContext per operation via IDbContextFactory.
/// </summary>
public class ExportService : IExportService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<ExportService> _logger;

    public ExportService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<ExportService> logger)
    {
        _contextFactory = contextFactory;
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
                    // Project names — serializing Tag entities would cycle via Tag.EventTags
                    Tags = e.Tags?.Select(t => t.TagName).ToList(),
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

            await using var dbContext = await _contextFactory.CreateDbContextAsync();

            progress?.Report(10);
            // Event.Tags is [NotMapped]; include the real junction + navigation
            var events = await dbContext.Events
                .Include(e => e.EventTags)
                .ThenInclude(et => et.Tag)
                .AsNoTracking()
                .ToListAsync();

            progress?.Report(30);
            var eras = await dbContext.Eras
                .AsNoTracking()
                .ToListAsync();

            progress?.Report(50);
            var tags = await dbContext.Tags
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

    /// <summary>
    /// Exports a generated narrative as Markdown or a single self-contained
    /// HTML document. Atomic write: the content is written to a sibling
    /// ".tmp" file and moved over the target, so a failure mid-write never
    /// leaves a partial export; the temp file is deleted in a finally block.
    /// </summary>
    public async Task ExportNarrativeAsync(Narrative narrative, string filePath, NarrativeFormat format)
    {
        if (narrative == null)
        {
            throw new ArgumentNullException(nameof(narrative));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        _logger.LogInformation("Exporting narrative '{Title}' as {Format}: {FilePath}",
            narrative.Title, format, filePath);

        var citedIds = CollectCitedEventIds(narrative);
        var citedEvents = await LoadCitedEventsAsync(citedIds);

        var content = format == NarrativeFormat.Html
            ? BuildNarrativeHtml(narrative, citedIds, citedEvents)
            : BuildNarrativeMarkdown(narrative, citedIds, citedEvents);

        var tempPath = filePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content);
            File.Move(tempPath, filePath, overwrite: true);
            _logger.LogInformation("Exported narrative ({Sections} sections, {Sources} source events)",
                narrative.Sections.Count, narrative.SourceEventIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting narrative to {FilePath}", filePath);
            throw;
        }
        finally
        {
            // On success the temp file no longer exists (it was moved); on
            // failure this removes the partial write.
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Could not delete temp export file: {TempPath}", tempPath);
                }
            }
        }
    }

    #region Private Methods

    // Same marker shape the narrative/ask features emit.
    private static readonly Regex NarrativeCitationRegex = new(@"\[event:([^\]\s]+)\]", RegexOptions.Compiled);

    /// <summary>
    /// Ids cited inline in the prose, in first-appearance order. When the
    /// narrative was generated without citations (no markers), falls back to
    /// the full source-event list so the footer still documents provenance.
    /// </summary>
    private static List<string> CollectCitedEventIds(Narrative narrative)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var section in narrative.Sections)
        {
            foreach (Match match in NarrativeCitationRegex.Matches(section.Prose ?? string.Empty))
            {
                var id = match.Groups[1].Value;
                if (seen.Add(id))
                {
                    ids.Add(id);
                }
            }
        }

        if (ids.Count == 0)
        {
            foreach (var id in narrative.SourceEventIds)
            {
                if (seen.Add(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private async Task<Dictionary<string, Event>> LoadCitedEventsAsync(List<string> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<string, Event>(StringComparer.Ordinal);
        }

        await using var dbContext = await _contextFactory.CreateDbContextAsync();

        var events = await dbContext.Events
            .AsNoTracking()
            .Where(e => ids.Contains(e.EventId))
            .ToListAsync();

        return events.ToDictionary(e => e.EventId, StringComparer.Ordinal);
    }

    private static string BuildNarrativeMarkdown(
        Narrative narrative, List<string> citedIds, Dictionary<string, Event> citedEvents)
    {
        var md = new StringBuilder();

        md.AppendLine($"# {narrative.Title}");
        md.AppendLine();
        if (!string.IsNullOrWhiteSpace(narrative.ScopeLabel))
        {
            md.AppendLine($"**Scope:** {narrative.ScopeLabel}");
        }
        md.AppendLine($"**Voice:** {narrative.Voice}");
        md.AppendLine($"**Generated:** {narrative.GeneratedAt:yyyy-MM-dd HH:mm} UTC");
        md.AppendLine();
        md.AppendLine("---");
        md.AppendLine();

        if (narrative.Sections.Count == 0)
        {
            md.AppendLine("_No memories were found for this scope._");
            md.AppendLine();
        }

        foreach (var section in narrative.Sections)
        {
            md.AppendLine($"## {section.Heading}");
            md.AppendLine();
            md.AppendLine(section.Prose);
            md.AppendLine();
        }

        if (citedIds.Count > 0)
        {
            md.AppendLine("---");
            md.AppendLine();
            md.AppendLine("## Sources");
            md.AppendLine();
            foreach (var id in citedIds)
            {
                md.AppendLine($"- `{id}` — {DescribeCitedEvent(id, citedEvents)}");
            }
        }

        return md.ToString();
    }

    private static string BuildNarrativeHtml(
        Narrative narrative, List<string> citedIds, Dictionary<string, Event> citedEvents)
    {
        // All model-derived text is HTML-encoded — the narrative prose is LLM
        // output and must never be emitted as raw markup.
        static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\"/>");
        html.AppendLine($"<title>{E(narrative.Title)}</title>");
        html.AppendLine("<style>");
        html.AppendLine("body { font-family: Georgia, 'Times New Roman', serif; max-width: 46rem; margin: 2rem auto; padding: 0 1rem; line-height: 1.6; color: #222; background: #fdfdfb; }");
        html.AppendLine("h1 { font-size: 1.9rem; margin-bottom: 0.25rem; }");
        html.AppendLine("h2 { font-size: 1.3rem; margin-top: 2rem; }");
        html.AppendLine(".meta { color: #666; font-size: 0.9rem; margin-bottom: 1.5rem; }");
        html.AppendLine(".sources { margin-top: 2.5rem; border-top: 1px solid #ccc; padding-top: 1rem; font-size: 0.85rem; color: #444; }");
        html.AppendLine(".sources code { background: #eee; padding: 0 0.25rem; border-radius: 3px; }");
        html.AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine($"<h1>{E(narrative.Title)}</h1>");

        var metaParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(narrative.ScopeLabel))
        {
            metaParts.Add(E(narrative.ScopeLabel));
        }
        metaParts.Add(E(narrative.Voice.ToString()) + " voice");
        metaParts.Add("generated " + E(narrative.GeneratedAt.ToString("yyyy-MM-dd HH:mm")) + " UTC");
        html.AppendLine($"<p class=\"meta\">{string.Join(" · ", metaParts)}</p>");

        if (narrative.Sections.Count == 0)
        {
            html.AppendLine("<p><em>No memories were found for this scope.</em></p>");
        }

        foreach (var section in narrative.Sections)
        {
            html.AppendLine($"<h2>{E(section.Heading)}</h2>");
            var paragraphs = (section.Prose ?? string.Empty)
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            foreach (var paragraph in paragraphs)
            {
                html.AppendLine($"<p>{E(paragraph.Trim()).Replace("\n", "<br/>")}</p>");
            }
        }

        if (citedIds.Count > 0)
        {
            html.AppendLine("<div class=\"sources\">");
            html.AppendLine("<h2>Sources</h2>");
            html.AppendLine("<ul>");
            foreach (var id in citedIds)
            {
                html.AppendLine($"<li><code>{E(id)}</code> — {E(DescribeCitedEvent(id, citedEvents))}</li>");
            }
            html.AppendLine("</ul>");
            html.AppendLine("</div>");
        }

        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    /// <summary>
    /// Footer text for a cited event: title plus its precision-honest display
    /// date (a Year-precision event renders as "1998", never a fabricated day).
    /// </summary>
    private static string DescribeCitedEvent(string id, Dictionary<string, Event> citedEvents)
    {
        if (!citedEvents.TryGetValue(id, out var evt))
        {
            return "(event no longer in the archive)";
        }

        var dateText = DateDisplay.FormatPrecise(evt.StartDate, evt.DatePrecision, evt.EndDate);
        return $"{evt.Title} ({dateText})";
    }

    private async Task<List<Event>> GetFilteredEventsAsync(DateTime? startDate, DateTime? endDate)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync();

        // Event.Tags is [NotMapped]; include the real junction + navigation
        var query = dbContext.Events
            .Include(e => e.EventTags)
            .ThenInclude(et => et.Tag)
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
