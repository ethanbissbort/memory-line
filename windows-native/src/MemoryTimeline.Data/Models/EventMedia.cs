using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// The kind of file a media attachment holds. Stored as INTEGER in SQLite -
/// the numeric values are part of the schema contract and must never be
/// reordered.
/// </summary>
public enum MediaType
{
    /// <summary>A photo/image (.jpg .jpeg .png .gif .bmp .webp .heic).</summary>
    Image = 0,

    /// <summary>A video clip (.mp4 .mov).</summary>
    Video = 1,

    /// <summary>An audio file (.mp3 .wav .m4a).</summary>
    Audio = 2,

    /// <summary>A document (.pdf .txt .md).</summary>
    Document = 3
}

/// <summary>
/// A media attachment (photo, video, audio, document) belonging to an event.
/// Files are COPIED into the managed media tree
/// (%LOCALAPPDATA%\MemoryTimeline\Media\{yyyy}\{MM}\{guid}{ext}) - never
/// referenced in place - and <see cref="FilePath"/>/<see cref="ThumbnailPath"/>
/// are stored RELATIVE to that media root so the tree can be moved or backed
/// up wholesale.
/// </summary>
[Table("event_media")]
public class EventMedia
{
    [Key]
    [Column("media_id")]
    public string MediaId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("event_id")]
    public string EventId { get; set; } = string.Empty;

    /// <summary>Path of the copied file, relative to the media root.</summary>
    [Required]
    [Column("file_path")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Path of the generated thumbnail (relative to the media root), or null
    /// when no thumbnail exists (non-images, or generation failed).
    /// </summary>
    [Column("thumbnail_path")]
    public string? ThumbnailPath { get; set; }

    [Column("media_type")]
    public MediaType MediaType { get; set; } = MediaType.Image;

    [MaxLength(500)]
    [Column("caption")]
    public string? Caption { get; set; }

    /// <summary>When the media was captured (EXIF DateTimeOriginal), if known.</summary>
    [Column("captured_at")]
    public DateTime? CapturedAt { get; set; }

    /// <summary>EXIF GPS latitude, if present (null island treated as absent).</summary>
    [Column("latitude")]
    public double? Latitude { get; set; }

    /// <summary>EXIF GPS longitude, if present (null island treated as absent).</summary>
    [Column("longitude")]
    public double? Longitude { get; set; }

    [Column("file_size_bytes")]
    public long? FileSizeBytes { get; set; }

    /// <summary>Lowercase SHA-256 hex of the file content (64 chars), for dedupe.</summary>
    [MaxLength(64)]
    [Column("content_hash")]
    public string? ContentHash { get; set; }

    /// <summary>Display order within the event's media strip (0-based).</summary>
    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Event Event { get; set; } = null!;
}
