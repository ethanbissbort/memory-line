using FluentAssertions;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for the pure media-attachment rules: the extension allowlist mapping
/// and the EXIF normalization (capture date + GPS, including the null-island
/// case) - no binary image fixtures needed.
/// </summary>
public class MediaAttachmentRulesTests
{
    #region GetMediaTypeForExtension

    [Theory]
    [InlineData(".jpg", MediaType.Image)]
    [InlineData(".jpeg", MediaType.Image)]
    [InlineData(".png", MediaType.Image)]
    [InlineData(".gif", MediaType.Image)]
    [InlineData(".bmp", MediaType.Image)]
    [InlineData(".webp", MediaType.Image)]
    [InlineData(".heic", MediaType.Image)]
    [InlineData(".mp4", MediaType.Video)]
    [InlineData(".mov", MediaType.Video)]
    [InlineData(".mp3", MediaType.Audio)]
    [InlineData(".wav", MediaType.Audio)]
    [InlineData(".m4a", MediaType.Audio)]
    [InlineData(".pdf", MediaType.Document)]
    [InlineData(".txt", MediaType.Document)]
    [InlineData(".md", MediaType.Document)]
    public void GetMediaTypeForExtension_AllowlistedExtension_MapsToExpectedType(
        string extension, MediaType expected)
    {
        MediaAttachmentRules.GetMediaTypeForExtension(extension).Should().Be(expected);
    }

    [Theory]
    [InlineData(".JPG")]
    [InlineData(".Jpeg")]
    [InlineData("PNG")]
    [InlineData("heic")]
    public void GetMediaTypeForExtension_CasingAndMissingDot_StillMatch(string extension)
    {
        MediaAttachmentRules.GetMediaTypeForExtension(extension).Should().NotBeNull();
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".zip")]
    [InlineData(".docx")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void GetMediaTypeForExtension_UnsupportedOrEmpty_ReturnsNull(string? extension)
    {
        MediaAttachmentRules.GetMediaTypeForExtension(extension).Should().BeNull();
    }

    #endregion

    #region NormalizeExif - GPS

    [Fact]
    public void NormalizeExif_ValidCoordinates_PassThrough()
    {
        // Arrange - Denver
        var (_, latitude, longitude) = MediaAttachmentRules.NormalizeExif(null, 39.7392, -104.9903);

        // Assert
        latitude.Should().Be(39.7392);
        longitude.Should().Be(-104.9903);
    }

    [Fact]
    public void NormalizeExif_NullIsland_TreatedAsAbsent()
    {
        // Arrange - (0, 0) is what cameras write when the GPS fix was missing
        var (_, latitude, longitude) = MediaAttachmentRules.NormalizeExif(null, 0.0, 0.0);

        // Assert
        latitude.Should().BeNull();
        longitude.Should().BeNull();
    }

    [Fact]
    public void NormalizeExif_ZeroLatitudeWithRealLongitude_IsKept()
    {
        // Arrange - the equator is a real place; only exact (0, 0) is suspect
        var (_, latitude, longitude) = MediaAttachmentRules.NormalizeExif(null, 0.0, 11.5);

        // Assert
        latitude.Should().Be(0.0);
        longitude.Should().Be(11.5);
    }

    [Theory]
    [InlineData(91.0, 10.0)]
    [InlineData(-91.0, 10.0)]
    [InlineData(45.0, 181.0)]
    [InlineData(45.0, -181.0)]
    [InlineData(double.NaN, 10.0)]
    [InlineData(45.0, double.NaN)]
    public void NormalizeExif_OutOfRangeCoordinates_TreatedAsAbsent(double latitude, double longitude)
    {
        var (_, lat, lon) = MediaAttachmentRules.NormalizeExif(null, latitude, longitude);

        lat.Should().BeNull();
        lon.Should().BeNull();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void NormalizeExif_PartiallyMissingCoordinates_TreatedAsAbsent(bool hasLatitude, bool hasLongitude)
    {
        var (_, lat, lon) = MediaAttachmentRules.NormalizeExif(
            null,
            hasLatitude ? 39.7 : null,
            hasLongitude ? -104.9 : null);

        lat.Should().BeNull();
        lon.Should().BeNull();
    }

    #endregion

    #region NormalizeExif - capture date

    [Fact]
    public void NormalizeExif_ValidCaptureDate_PassesThrough()
    {
        var captured = new DateTime(2019, 8, 14, 16, 30, 12);

        var (capturedAt, _, _) = MediaAttachmentRules.NormalizeExif(captured, null, null);

        capturedAt.Should().Be(captured);
    }

    [Fact]
    public void NormalizeExif_DefaultDate_TreatedAsAbsent()
    {
        var (capturedAt, _, _) = MediaAttachmentRules.NormalizeExif(default(DateTime), null, null);

        capturedAt.Should().BeNull();
    }

    [Fact]
    public void NormalizeExif_NullDate_StaysNull()
    {
        var (capturedAt, _, _) = MediaAttachmentRules.NormalizeExif(null, null, null);

        capturedAt.Should().BeNull();
    }

    #endregion
}
