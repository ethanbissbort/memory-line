using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Manages media attachments for events. Files are COPIED into a managed
/// tree under the media root (%LOCALAPPDATA%\MemoryTimeline\Media by default,
/// mirroring AudioRecordingService's managed-directory pattern) - the source
/// file is never referenced in place and never modified. The database stores
/// paths RELATIVE to the media root; use <see cref="GetAbsolutePath"/> /
/// <see cref="GetAbsoluteThumbnailPath"/> to resolve them for display.
/// </summary>
public interface IMediaService
{
    /// <summary>Absolute path of the managed media root (created lazily on first attach).</summary>
    string MediaRoot { get; }

    /// <summary>
    /// Attaches a file to an event: validates the extension allowlist,
    /// verifies the event exists, hashes the content (attaching a file whose
    /// hash is already attached to the SAME event is a no-op returning the
    /// existing row), copies the file into the managed tree, reads EXIF
    /// capture date/GPS for images, generates a thumbnail (failure is logged
    /// and tolerated), persists the row with the next sort order and
    /// publishes <see cref="MediaAttachedMessage"/>.
    /// Failures throw with a user-presentable (InfoBar-ready) message.
    /// </summary>
    Task<EventMedia> AttachAsync(string eventId, string sourceFilePath, string? caption = null, CancellationToken ct = default);

    /// <summary>
    /// Attaches many files, reporting (done, total) progress after each file.
    /// Individual failures do not stop the batch; if any file failed, an
    /// exception listing the failures is thrown AFTER the rest were attached.
    /// </summary>
    Task<List<EventMedia>> AttachManyAsync(string eventId, IEnumerable<string> paths, IProgress<(int done, int total)>? progress = null, CancellationToken ct = default);

    /// <summary>Gets the event's media ordered by sort order.</summary>
    Task<List<EventMedia>> GetForEventAsync(string eventId);

    /// <summary>
    /// Removes a media row (a missing row is a logged no-op) and, when
    /// <paramref name="deleteFile"/> is true, best-effort deletes the managed
    /// file and its thumbnail.
    /// </summary>
    Task RemoveAsync(string mediaId, bool deleteFile = true);

    /// <summary>Updates the caption (trimmed; blank clears it). Throws when the row is missing.</summary>
    Task UpdateCaptionAsync(string mediaId, string? caption);

    /// <summary>
    /// Persists a new display order for the event's media. Ids missing from
    /// the list keep their relative order after the listed ones.
    /// </summary>
    Task ReorderAsync(string eventId, IReadOnlyList<string> mediaIdsInOrder);

    /// <summary>
    /// Reads file metadata WITHOUT importing: media type (null when the
    /// extension is unsupported), file size, and - for images - the EXIF
    /// capture date and GPS coordinates (null island treated as absent).
    /// </summary>
    Task<MediaProbeResult> ProbeAsync(string filePath);

    /// <summary>Resolves the stored relative file path against the media root.</summary>
    string GetAbsolutePath(EventMedia media);

    /// <summary>Resolves the stored relative thumbnail path, or null when there is no thumbnail.</summary>
    string? GetAbsoluteThumbnailPath(EventMedia media);

    /// <summary>
    /// Best-effort deletion of the physical files (file + thumbnail) for rows
    /// that are already gone from the database - e.g. after the event-delete
    /// FK cascade, which removes the rows but cannot touch the files.
    /// Failures are logged, never thrown.
    /// </summary>
    void DeleteFiles(IEnumerable<EventMedia> media);
}

/// <summary>
/// Result of <see cref="IMediaService.ProbeAsync"/>: metadata read from a file
/// without importing it.
/// </summary>
public class MediaProbeResult
{
    /// <summary>The media type implied by the extension, or null when unsupported.</summary>
    public MediaType? MediaType { get; set; }

    /// <summary>EXIF DateTimeOriginal for images, if present.</summary>
    public DateTime? CapturedAt { get; set; }

    /// <summary>EXIF GPS latitude, if present (null island treated as absent).</summary>
    public double? Latitude { get; set; }

    /// <summary>EXIF GPS longitude, if present (null island treated as absent).</summary>
    public double? Longitude { get; set; }

    /// <summary>Size of the file in bytes.</summary>
    public long FileSizeBytes { get; set; }
}
