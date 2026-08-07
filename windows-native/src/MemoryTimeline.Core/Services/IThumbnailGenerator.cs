namespace MemoryTimeline.Core.Services;

/// <summary>
/// Generates JPEG thumbnails for image files. The implementation lives in the
/// app project (WindowsThumbnailGenerator, Windows.Graphics.Imaging) so Core
/// stays free of imaging/UI concerns; tests substitute a mock.
/// </summary>
public interface IThumbnailGenerator
{
    /// <summary>
    /// Tries to write a JPEG thumbnail of <paramref name="sourceImagePath"/>
    /// to <paramref name="destJpegPath"/>, scaled so the longest edge is at
    /// most <paramref name="longestEdge"/> pixels (never upscaled).
    /// Returns false on ANY failure instead of throwing - a failed thumbnail
    /// must never invalidate the attachment itself.
    /// </summary>
    Task<bool> TryGenerateAsync(string sourceImagePath, string destJpegPath, int longestEdge, CancellationToken ct);
}
