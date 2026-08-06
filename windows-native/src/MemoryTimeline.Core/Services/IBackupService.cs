namespace MemoryTimeline.Core.Services;

/// <summary>What a backup should include beyond the database itself.</summary>
public class BackupOptions
{
    /// <summary>Include the managed media tree (photos, thumbnails, ...).</summary>
    public bool IncludeMedia { get; set; } = true;

    /// <summary>Include the managed audio-recordings tree.</summary>
    public bool IncludeAudio { get; set; } = true;
}

/// <summary>Result of a completed backup.</summary>
public class BackupResult
{
    /// <summary>The final .mtbak path.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Size of the written archive in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Events contained in the backed-up database snapshot.</summary>
    public int EventCount { get; set; }

    /// <summary>How long the backup took.</summary>
    public double DurationSeconds { get; set; }
}

/// <summary>
/// What a backup file contains, read from its manifest WITHOUT touching the
/// live database.
/// </summary>
public class RestorePreview
{
    /// <summary>The inspected backup file.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Manifest formatVersion (1 for this build).</summary>
    public int FormatVersion { get; set; }

    /// <summary>App version that wrote the backup.</summary>
    public string? AppVersion { get; set; }

    /// <summary>When the backup was created (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Events in the backed-up database.</summary>
    public int EventCount { get; set; }

    /// <summary>Whether the archive carries a media tree.</summary>
    public bool IncludesMedia { get; set; }

    /// <summary>Whether the archive carries an audio tree.</summary>
    public bool IncludesAudio { get; set; }

    /// <summary>Files under media/ in the archive.</summary>
    public int MediaFileCount { get; set; }

    /// <summary>Files under audio/ in the archive.</summary>
    public int AudioFileCount { get; set; }

    /// <summary>Size of the backup file in bytes.</summary>
    public long SizeBytes { get; set; }
}

/// <summary>
/// How a restore applies the backup.
/// Merge (combining a backup's events into the current archive without
/// replacing it) is deferred: it needs per-entity conflict resolution across
/// events/tags/people/locations/media and a dedicated review UI, so Replace
/// is the only supported mode for now.
/// </summary>
public enum RestoreMode
{
    /// <summary>Replace the current database (and media/audio trees) with the backup's contents.</summary>
    Replace
}

/// <summary>One discovered backup file with its parsed manifest.</summary>
public class BackupRecord
{
    /// <summary>Full path of the .mtbak file.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>File name only, for display.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Size of the archive in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>When the backup was created (UTC, from the manifest).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Events in the backed-up database (from the manifest).</summary>
    public int EventCount { get; set; }

    /// <summary>Whether the archive carries a media tree.</summary>
    public bool IncludesMedia { get; set; }

    /// <summary>Whether the archive carries an audio tree.</summary>
    public bool IncludesAudio { get; set; }
}

/// <summary>
/// User-facing backup/restore of the whole archive as a single .mtbak file
/// (a zip: database.db + manifest.json + optional media/** and audio/**).
/// The database is captured with SQLite's online backup API (WAL-safe) -
/// never a raw file copy of the live database.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Writes a .mtbak backup to <paramref name="destinationPath"/> (any
    /// writable path, including UNC shares). The archive is written to
    /// "<c>destinationPath</c>.tmp" and moved into place on success, so a
    /// torn/partial .mtbak is never left behind. Progress is reported 0-100
    /// at meaningful stages. Throws with a clear message on failure; the .tmp
    /// is cleaned up in all failure/cancel paths.
    /// </summary>
    Task<BackupResult> CreateBackupAsync(
        string destinationPath,
        BackupOptions options,
        IProgress<int>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Reads a backup's manifest and entry counts. ZERO writes to the live
    /// database. Throws when the file is missing, not a Memory Timeline
    /// backup, or from a newer format version.
    /// </summary>
    Task<RestorePreview> PreviewRestoreAsync(string backupPath);

    /// <summary>
    /// Replaces the current archive with a backup's contents:
    /// (1) an automatic db-only safety backup of the CURRENT database is
    /// written to the backup destination first, (2) pooled SQLite connections
    /// are cleared, (3) database.db is extracted over the live database path
    /// (stale WAL/SHM sidecars removed), (4) the media/audio trees are
    /// restored with existing files overwritten, (5) an
    /// <see cref="ArchiveRestoredMessage"/> is published and a restart
    /// recommendation logged. Throws with a clear message on failure.
    /// </summary>
    Task RestoreAsync(string backupPath, RestoreMode mode, CancellationToken ct = default);

    /// <summary>
    /// Scans the configured backup destination for *.mtbak files and parses
    /// their manifests (unreadable files are skipped with a log entry).
    /// Newest first. Empty when no destination is configured.
    /// </summary>
    Task<List<BackupRecord>> ListBackupsAsync();
}
