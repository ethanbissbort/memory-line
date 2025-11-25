namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for importing timeline data.
/// </summary>
public interface IImportService
{
    Task<ImportResult> ImportFromJsonAsync(string filePath, ImportOptions? options = null, IProgress<(int, string)>? progress = null);
    Task<ImportResult> ImportFromElectronAsync(string filePath, ImportOptions? options = null, IProgress<(int, string)>? progress = null);
    Task<ValidationResult> ValidateImportFileAsync(string filePath);
}

/// <summary>
/// Options for import operations.
/// </summary>
public class ImportOptions
{
    public bool SkipDuplicates { get; set; } = true;
    public bool UpdateExisting { get; set; } = false;
    public bool CreateBackup { get; set; } = true;
}

/// <summary>
/// Result of an import operation.
/// </summary>
public class ImportResult
{
    public bool Success { get; set; }
    public int EventsImported { get; set; }
    public int EventsSkipped { get; set; }
    public int EventsUpdated { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of file validation.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? FileType { get; set; }
    public int EventCount { get; set; }
    public List<string> Issues { get; set; } = new();
}
