using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// Content-carrying artifact of a capture (audio, transcript, waveform, ...).
/// The storage locator is opaque: for Windows-local artifacts it is a file path;
/// for synced artifacts it is a sync-service reference resolved to a local cache
/// path before processing. Integrity is verified via byte length and SHA-256.
/// See docs/design/IOS-ROADTRIP-COMPANION-SYSTEM-DESIGN.md §6.2.
/// </summary>
[Table("capture_artifacts")]
public class CaptureArtifact
{
    [Key]
    [Column("artifact_id")]
    public string ArtifactId { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [Column("capture_id")]
    public string CaptureId { get; set; } = string.Empty;

    [Required]
    [Column("artifact_type")]
    [MaxLength(30)]
    public string ArtifactType { get; set; } = CaptureArtifactType.AudioOriginal;

    [Required]
    [Column("storage_locator")]
    public string StorageLocator { get; set; } = string.Empty;

    [Column("media_type")]
    [MaxLength(100)]
    public string? MediaType { get; set; }

    [Column("byte_length")]
    public long? ByteLength { get; set; }

    [Column("sha256")]
    [MaxLength(64)]
    public string? Sha256 { get; set; }

    [Column("encryption_scheme")]
    [MaxLength(40)]
    public string? EncryptionScheme { get; set; }

    [Column("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [Required]
    [Column("upload_state")]
    [MaxLength(20)]
    public string UploadState { get; set; } = CaptureArtifactUploadState.Local;

    // Navigation properties
    public virtual Capture? Capture { get; set; }
}

/// <summary>
/// Artifact content types.
/// </summary>
public static class CaptureArtifactType
{
    public const string AudioOriginal = "audio_original";
    public const string AudioNormalized = "audio_normalized";
    public const string Transcript = "transcript";
    public const string Waveform = "waveform";
    public const string Image = "image";
    public const string Metadata = "metadata";
}

/// <summary>
/// Transfer state of an artifact relative to the sync service.
/// </summary>
public static class CaptureArtifactUploadState
{
    /// <summary>Exists only on this machine; nothing to transfer yet.</summary>
    public const string Local = "local";
    public const string PendingUpload = "pending_upload";
    public const string Uploaded = "uploaded";
    /// <summary>Remote-origin artifact that has been materialized locally.</summary>
    public const string Downloaded = "downloaded";
    public const string Failed = "failed";
}
