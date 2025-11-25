namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for exporting timeline data.
/// </summary>
public interface IExportService
{
    Task ExportToJsonAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null, IProgress<int>? progress = null);
    Task ExportToCsvAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null, IProgress<int>? progress = null);
    Task ExportToMarkdownAsync(string filePath, DateTime? startDate = null, DateTime? endDate = null, IProgress<int>? progress = null);
    Task ExportFullDatabaseAsync(string filePath, IProgress<int>? progress = null);
}
