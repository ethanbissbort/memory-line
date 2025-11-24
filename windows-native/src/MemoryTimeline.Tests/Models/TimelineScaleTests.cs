using FluentAssertions;
using MemoryTimeline.Core.Models;
using Xunit;

namespace MemoryTimeline.Tests.Models;

/// <summary>
/// Unit tests for TimelineScale helper methods.
/// </summary>
public class TimelineScaleTests
{
    [Theory]
    [InlineData(ZoomLevel.Year, 0.1)]
    [InlineData(ZoomLevel.Month, 3.0)]
    [InlineData(ZoomLevel.Week, 50.0)]
    [InlineData(ZoomLevel.Day, 800.0)]
    public void GetPixelsPerDay_AllZoomLevels_ReturnsCorrectValues(ZoomLevel zoom, double expected)
    {
        // Act
        var result = TimelineScale.GetPixelsPerDay(zoom);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ZoomLevel.Year, 1000, 10000)] // 1000px / 0.1 = 10000 days
    [InlineData(ZoomLevel.Month, 900, 300)]   // 900px / 3.0 = 300 days
    [InlineData(ZoomLevel.Week, 500, 10)]     // 500px / 50.0 = 10 days
    [InlineData(ZoomLevel.Day, 800, 1)]       // 800px / 800.0 = 1 day
    public void GetVisibleDays_VariousViewportWidths_CalculatesCorrectly(
        ZoomLevel zoom,
        double viewportWidth,
        double expectedDays)
    {
        // Act
        var result = TimelineScale.GetVisibleDays(zoom, viewportWidth);

        // Assert
        result.Should().BeApproximately(expectedDays, 0.01);
    }

    [Fact]
    public void GetPixelPosition_SameDate_ReturnsZero()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var date = new DateTime(2024, 1, 1);

        // Act
        var result = TimelineScale.GetPixelPosition(date, referenceDate, ZoomLevel.Month);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void GetPixelPosition_OneDayLater_ReturnsPixelsPerDay()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var date = new DateTime(2024, 1, 2);

        // Act
        var result = TimelineScale.GetPixelPosition(date, referenceDate, ZoomLevel.Month);

        // Assert
        result.Should().Be(3.0); // 1 day * 3.0 pixels per day
    }

    [Fact]
    public void GetPixelPosition_OneMonthLater_AtMonthZoom_Returns90Pixels()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var date = new DateTime(2024, 2, 1);

        // Act
        var result = TimelineScale.GetPixelPosition(date, referenceDate, ZoomLevel.Month);

        // Assert
        result.Should().BeApproximately(93.0, 1.0); // 31 days * 3.0 = 93 pixels
    }

    [Fact]
    public void GetDateFromPixel_ZeroPixels_ReturnsReferenceDate()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);

        // Act
        var result = TimelineScale.GetDateFromPixel(0, referenceDate, ZoomLevel.Month);

        // Assert
        result.Should().Be(referenceDate);
    }

    [Fact]
    public void GetDateFromPixel_PositivePixels_ReturnsLaterDate()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var pixels = 30.0; // 10 days at Month zoom (3.0 pixels/day)

        // Act
        var result = TimelineScale.GetDateFromPixel(pixels, referenceDate, ZoomLevel.Month);

        // Assert
        result.Should().Be(new DateTime(2024, 1, 11));
    }

    [Fact]
    public void GetEventWidth_PointEvent_ReturnsMinimumWidth()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1);

        // Act
        var result = TimelineScale.GetEventWidth(startDate, null, ZoomLevel.Month);

        // Assert
        result.Should().Be(4.0); // Minimum width for Month zoom
    }

    [Fact]
    public void GetEventWidth_OneDayDuration_ReturnsCorrectWidth()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 2);

        // Act
        var result = TimelineScale.GetEventWidth(startDate, endDate, ZoomLevel.Month);

        // Assert
        result.Should().Be(3.0); // 1 day * 3.0 pixels/day
    }

    [Fact]
    public void GetEventWidth_SevenDayDuration_AtWeekZoom_Returns350Pixels()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1);
        var endDate = new DateTime(2024, 1, 8);

        // Act
        var result = TimelineScale.GetEventWidth(startDate, endDate, ZoomLevel.Week);

        // Assert
        result.Should().Be(350.0); // 7 days * 50 pixels/day
    }

    [Fact]
    public void GetEventWidth_VeryShortDuration_ReturnsMinimumWidth()
    {
        // Arrange - Event shorter than minimum width threshold
        var startDate = new DateTime(2024, 1, 1, 0, 0, 0);
        var endDate = new DateTime(2024, 1, 1, 1, 0, 0); // 1 hour at Year zoom

        // Act
        var result = TimelineScale.GetEventWidth(startDate, endDate, ZoomLevel.Year);

        // Assert
        result.Should().Be(2.0); // Minimum width for Year zoom
    }

    [Theory]
    [InlineData(ZoomLevel.Year, 2.0)]
    [InlineData(ZoomLevel.Month, 4.0)]
    [InlineData(ZoomLevel.Week, 8.0)]
    [InlineData(ZoomLevel.Day, 24.0)]
    public void GetMinimumEventWidth_AllZoomLevels_ReturnsCorrectMinimum(
        ZoomLevel zoom,
        double expected)
    {
        // Act
        var result = TimelineScale.GetMinimumEventWidth(zoom);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ZoomLevel.Year, 365)]
    [InlineData(ZoomLevel.Month, 30)]
    [InlineData(ZoomLevel.Week, 7)]
    [InlineData(ZoomLevel.Day, 1)]
    public void GetGridInterval_AllZoomLevels_ReturnsCorrectInterval(
        ZoomLevel zoom,
        int expected)
    {
        // Act
        var result = TimelineScale.GetGridInterval(zoom);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ZoomLevel.Year, ZoomLevel.Month)]
    [InlineData(ZoomLevel.Month, ZoomLevel.Week)]
    [InlineData(ZoomLevel.Week, ZoomLevel.Day)]
    [InlineData(ZoomLevel.Day, ZoomLevel.Day)] // Max zoom stays at Day
    public void ZoomIn_AllLevels_ReturnsNextZoomLevel(ZoomLevel current, ZoomLevel expected)
    {
        // Act
        var result = TimelineScale.ZoomIn(current);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ZoomLevel.Day, ZoomLevel.Week)]
    [InlineData(ZoomLevel.Week, ZoomLevel.Month)]
    [InlineData(ZoomLevel.Month, ZoomLevel.Year)]
    [InlineData(ZoomLevel.Year, ZoomLevel.Year)] // Min zoom stays at Year
    public void ZoomOut_AllLevels_ReturnsPreviousZoomLevel(ZoomLevel current, ZoomLevel expected)
    {
        // Act
        var result = TimelineScale.ZoomOut(current);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ZoomLevel.Year, false)]
    [InlineData(ZoomLevel.Month, true)]
    [InlineData(ZoomLevel.Week, true)]
    [InlineData(ZoomLevel.Day, true)]
    public void CanZoomOut_VariousLevels_ReturnsCorrectResult(ZoomLevel zoom, bool expected)
    {
        // Act
        var result = TimelineScale.CanZoomOut(zoom);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ZoomLevel.Year, true)]
    [InlineData(ZoomLevel.Month, true)]
    [InlineData(ZoomLevel.Week, true)]
    [InlineData(ZoomLevel.Day, false)]
    public void CanZoomIn_VariousLevels_ReturnsCorrectResult(ZoomLevel zoom, bool expected)
    {
        // Act
        var result = TimelineScale.CanZoomIn(zoom);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ZoomLevel.Year, "Year View")]
    [InlineData(ZoomLevel.Month, "Month View")]
    [InlineData(ZoomLevel.Week, "Week View")]
    [InlineData(ZoomLevel.Day, "Day View")]
    public void GetZoomLevelName_AllLevels_ReturnsCorrectName(ZoomLevel zoom, string expected)
    {
        // Act
        var result = TimelineScale.GetZoomLevelName(zoom);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ZoomLevel.Year, "View decades at a glance")]
    [InlineData(ZoomLevel.Month, "View years in detail")]
    [InlineData(ZoomLevel.Week, "View months in detail")]
    [InlineData(ZoomLevel.Day, "View weeks in detail")]
    public void GetZoomLevelDescription_AllLevels_ReturnsDescription(
        ZoomLevel zoom,
        string expected)
    {
        // Act
        var result = TimelineScale.GetZoomLevelDescription(zoom);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void GetPixelPosition_NegativeDays_ReturnsNegativePixels()
    {
        // Arrange - Date before reference
        var referenceDate = new DateTime(2024, 1, 15);
        var date = new DateTime(2024, 1, 1);

        // Act
        var result = TimelineScale.GetPixelPosition(date, referenceDate, ZoomLevel.Month);

        // Assert
        result.Should().BeNegative();
        result.Should().BeApproximately(-42.0, 1.0); // -14 days * 3.0 = -42 pixels
    }

    [Fact]
    public void GetDateFromPixel_NegativePixels_ReturnsEarlierDate()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 15);
        var pixels = -30.0; // -10 days at Month zoom

        // Act
        var result = TimelineScale.GetDateFromPixel(pixels, referenceDate, ZoomLevel.Month);

        // Assert
        result.Should().Be(new DateTime(2024, 1, 5));
    }

    [Fact]
    public void RoundTrip_DateToPixelToDate_ReturnsOriginalDate()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var originalDate = new DateTime(2024, 3, 15);
        var zoom = ZoomLevel.Month;

        // Act
        var pixels = TimelineScale.GetPixelPosition(originalDate, referenceDate, zoom);
        var resultDate = TimelineScale.GetDateFromPixel(pixels, referenceDate, zoom);

        // Assert
        resultDate.Date.Should().Be(originalDate.Date);
    }
}
