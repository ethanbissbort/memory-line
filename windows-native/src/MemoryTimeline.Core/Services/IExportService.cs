namespace MemoryTimeline.Core.Services;

/// <summary>
/// Output formats for narrative export. PDF is deferred.
/// </summary>
public enum NarrativeFormat
{
    Markdown = 0,
    Html = 1
}

/// <summary>
/// Service interface for exporting timeline data.
/// </summary>
public interface IExportService
{
    Task ExportToJsonAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null, IProgress<int>? progress = null);
    Task ExportToCsvAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null, IProgress<int>? progress = null);
    Task ExportToMarkdownAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null, IProgress<int>? progress = null);
    Task ExportFullDatabaseAsync(string filePath, IProgress<int>? progress = null);

    /// <summary>
    /// Exports a generated narrative as a readable document.
    /// The write is atomic: content goes to a sibling ".tmp" file first and is
    /// moved over the target, so a failure never leaves a partial export file
    /// (the temp file is cleaned up on failure).
    /// The footer lists the cited events (id, title, precision-honest date).
    /// </summary>
    Task ExportNarrativeAsync(Narrative narrative, string filePath, NarrativeFormat format);
}
