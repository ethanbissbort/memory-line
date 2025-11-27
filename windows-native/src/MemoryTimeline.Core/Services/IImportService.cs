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
    public bool ImportEras { get; set; } = true;
    public bool ImportTags { get; set; } = true;
    public ConflictResolution ConflictResolution { get; set; } = ConflictResolution.Skip;
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
    public int ErasImported { get; set; }
    public int TagsImported { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of file validation.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? FileType { get; set; }
    public string? Format { get; set; }
    public int EventCount { get; set; }
    public int EraCount { get; set; }
    public int TagCount { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Conflict resolution strategy for import operations.
/// </summary>
public enum ConflictResolution
{
    Skip,
    Overwrite,
    CreateDuplicate,
    Merge
}
