namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for exporting timeline data.
/// </summary>
public interface IExportService
{
    Task ExportToJsonAsync(string filePath);
    Task ExportToCsvAsync(string filePath);
    Task ExportToMarkdownAsync(string filePath);
    Task ExportToPdfAsync(string filePath);
}

/// <summary>
/// Export service implementation.
/// </summary>
public class ExportService : IExportService
{
    public Task ExportToJsonAsync(string filePath)
    {
        // TODO: Implement JSON export
        throw new NotImplementedException();
    }

    public Task ExportToCsvAsync(string filePath)
    {
        // TODO: Implement CSV export
        throw new NotImplementedException();
    }

    public Task ExportToMarkdownAsync(string filePath)
    {
        // TODO: Implement Markdown export
        throw new NotImplementedException();
    }

    public Task ExportToPdfAsync(string filePath)
    {
        // TODO: Implement PDF export using WinUI 3 printing
        throw new NotImplementedException();
    }
}
