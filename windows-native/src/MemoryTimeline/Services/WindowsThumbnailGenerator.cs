using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MemoryTimeline.Services;

/// <summary>
/// JPEG thumbnail generation via Windows.Graphics.Imaging (WinRT
/// BitmapDecoder/BitmapEncoder). Scales so the longest edge is at most the
/// requested size (never upscales), honors the EXIF orientation flag, and
/// encodes at ~0.8 quality. ALL failures are logged and reported as false -
/// per the <see cref="IThumbnailGenerator"/> contract, a failed thumbnail
/// never invalidates the attachment.
/// </summary>
public class WindowsThumbnailGenerator : IThumbnailGenerator
{
    private readonly ILogger<WindowsThumbnailGenerator> _logger;

    public WindowsThumbnailGenerator(ILogger<WindowsThumbnailGenerator> logger)
    {
        _logger = logger;
    }

    public async Task<bool> TryGenerateAsync(
        string sourceImagePath,
        string destJpegPath,
        int longestEdge,
        CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested || longestEdge <= 0)
            {
                return false;
            }

            var sourceFile = await StorageFile.GetFileFromPathAsync(sourceImagePath);
            using IRandomAccessStream inputStream = await sourceFile.OpenAsync(FileAccessMode.Read);

            var decoder = await BitmapDecoder.CreateAsync(inputStream);
            var width = decoder.PixelWidth;
            var height = decoder.PixelHeight;
            if (width == 0 || height == 0)
            {
                _logger.LogWarning("Thumbnail source has zero dimensions: {Path}", sourceImagePath);
                return false;
            }

            // Scale so the longest edge fits, never upscaling small images.
            var scale = Math.Min(1.0, (double)longestEdge / Math.Max(width, height));
            var scaledWidth = (uint)Math.Max(1, Math.Round(width * scale));
            var scaledHeight = (uint)Math.Max(1, Math.Round(height * scale));

            var transform = new BitmapTransform
            {
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight,
                InterpolationMode = BitmapInterpolationMode.Fant
            };

            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            var destDirectory = Path.GetDirectoryName(destJpegPath);
            if (!string.IsNullOrEmpty(destDirectory))
            {
                Directory.CreateDirectory(destDirectory);
            }

            var destFolder = await StorageFolder.GetFolderFromPathAsync(destDirectory!);
            var destFile = await destFolder.CreateFileAsync(
                Path.GetFileName(destJpegPath), CreationCollisionOption.ReplaceExisting);

            using IRandomAccessStream outputStream = await destFile.OpenAsync(FileAccessMode.ReadWrite);

            // ~80% JPEG quality (BitmapEncoder's ImageQuality expects a Single).
            var propertySet = new BitmapPropertySet();
            var qualityValue = new BitmapTypedValue(0.8, Windows.Foundation.PropertyType.Single);
            propertySet.Add("ImageQuality", qualityValue);

            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outputStream, propertySet);
            encoder.SetSoftwareBitmap(softwareBitmap);
            await encoder.FlushAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail generation failed for {Source}", sourceImagePath);
            return false;
        }
    }
}
