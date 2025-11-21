namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for importing timeline data.
/// </summary>
public interface IImportService
{
    Task ImportFromJsonAsync(string filePath);
    Task ImportFromElectronDatabaseAsync(string dbPath);
}

/// <summary>
/// Import service implementation.
/// </summary>
public class ImportService : IImportService
{
    public Task ImportFromJsonAsync(string filePath)
    {
        // TODO: Implement JSON import
        throw new NotImplementedException();
    }

    public Task ImportFromElectronDatabaseAsync(string dbPath)
    {
        // TODO: Implement Electron database migration
        throw new NotImplementedException();
    }
}
