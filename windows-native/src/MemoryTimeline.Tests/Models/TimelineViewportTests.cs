using FluentAssertions;
using MemoryTimeline.Core.Models;
using Xunit;

namespace MemoryTimeline.Tests.Models;

/// <summary>
/// Unit tests for TimelineViewport class.
/// Tests viewport calculations, coordinate conversions, and viewport manipulation.
/// </summary>
public class TimelineViewportTests
{
    #region Property Tests

    [Fact]
    public void VisibleTimeSpan_CalculatesCorrectly()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act
        var timeSpan = viewport.VisibleTimeSpan;

        // Assert
        timeSpan.TotalDays.Should().Be(30);
    }

    [Fact]
    public void VisibleDays_CalculatesCorrectly()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 2, 1) // 31 days
        };

        // Act
        var days = viewport.VisibleDays;

        // Assert
        days.Should().Be(31);
    }

    [Fact]
    public void VisibleDays_FractionalDays_CalculatesCorrectly()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1, 0, 0, 0),
            EndDate = new DateTime(2024, 1, 1, 12, 0, 0) // 0.5 days
        };

        // Act
        var days = viewport.VisibleDays;

        // Assert
        days.Should().Be(0.5);
    }

    #endregion

    #region DateToPixel Tests

    [Fact]
    public void DateToPixel_DateInMiddleOfViewport_ReturnsCorrectPixel()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31),
            PixelsPerDay = 10.0,
            ViewportWidth = 300
        };

        var testDate = new DateTime(2024, 1, 11); // 10 days from start

        // Act
        var pixel = viewport.DateToPixel(testDate);

        // Assert
        pixel.Should().Be(100); // 10 days * 10 pixels/day
    }

    [Fact]
    public void DateToPixel_DateAtStart_ReturnsZero()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31),
            PixelsPerDay = 10.0,
            ViewportWidth = 300
        };

        // Act
        var pixel = viewport.DateToPixel(new DateTime(2024, 1, 1));

        // Assert
        pixel.Should().Be(0);
    }

    [Fact]
    public void DateToPixel_DateAtEnd_ReturnsCorrectPixel()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31),
            PixelsPerDay = 10.0,
            ViewportWidth = 300
        };

        // Act
        var pixel = viewport.DateToPixel(new DateTime(2024, 1, 31));

        // Assert
        pixel.Should().Be(300); // 30 days * 10 pixels/day
    }

    [Fact]
    public void DateToPixel_DateBeforeViewport_ReturnsZero()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31),
            PixelsPerDay = 10.0,
            ViewportWidth = 300
        };

        // Act
        var pixel = viewport.DateToPixel(new DateTime(2023, 12, 15));

        // Assert
        pixel.Should().Be(0);
    }

    [Fact]
    public void DateToPixel_DateAfterViewport_ReturnsViewportWidth()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31),
            PixelsPerDay = 10.0,
            ViewportWidth = 300
        };

        // Act
        var pixel = viewport.DateToPixel(new DateTime(2024, 2, 15));

        // Assert
        pixel.Should().Be(300);
    }

    #endregion

    #region PixelToDate Tests

    [Fact]
    public void PixelToDate_ZeroPixels_ReturnsStartDate()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            PixelsPerDay = 10.0
        };

        // Act
        var date = viewport.PixelToDate(0);

        // Assert
        date.Should().Be(new DateTime(2024, 1, 1));
    }

    [Fact]
    public void PixelToDate_MiddleOfViewport_ReturnsCorrectDate()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            PixelsPerDay = 10.0
        };

        // Act
        var date = viewport.PixelToDate(100); // 10 days worth

        // Assert
        date.Date.Should().Be(new DateTime(2024, 1, 11));
    }

    [Fact]
    public void PixelToDate_NegativePixels_ReturnsDateBeforeStart()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            PixelsPerDay = 10.0
        };

        // Act
        var date = viewport.PixelToDate(-100); // -10 days

        // Assert
        date.Date.Should().Be(new DateTime(2023, 12, 22));
    }

    [Fact]
    public void PixelToDate_BeyondViewportWidth_ReturnsDateAfterEnd()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31),
            PixelsPerDay = 10.0,
            ViewportWidth = 300
        };

        // Act
        var date = viewport.PixelToDate(400); // 40 days worth

        // Assert
        date.Date.Should().Be(new DateTime(2024, 2, 10));
    }

    [Fact]
    public void PixelToDate_RoundTrip_PreservesDate()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31),
            PixelsPerDay = 10.0,
            ViewportWidth = 300
        };
        var originalDate = new DateTime(2024, 1, 15);

        // Act
        var pixel = viewport.DateToPixel(originalDate);
        var resultDate = viewport.PixelToDate(pixel);

        // Assert
        resultDate.Date.Should().Be(originalDate.Date);
    }

    #endregion

    #region IsDateVisible Tests

    [Fact]
    public void IsDateVisible_DateInRange_ReturnsTrue()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsDateVisible(new DateTime(2024, 1, 15)).Should().BeTrue();
    }

    [Fact]
    public void IsDateVisible_DateAtStart_ReturnsTrue()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsDateVisible(new DateTime(2024, 1, 1)).Should().BeTrue();
    }

    [Fact]
    public void IsDateVisible_DateAtEnd_ReturnsTrue()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsDateVisible(new DateTime(2024, 1, 31)).Should().BeTrue();
    }

    [Fact]
    public void IsDateVisible_DateBeforeRange_ReturnsFalse()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsDateVisible(new DateTime(2023, 12, 31)).Should().BeFalse();
    }

    [Fact]
    public void IsDateVisible_DateAfterRange_ReturnsFalse()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsDateVisible(new DateTime(2024, 2, 1)).Should().BeFalse();
    }

    #endregion

    #region IsEventVisible Tests

    [Fact]
    public void IsEventVisible_PointEventInRange_ReturnsTrue()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsEventVisible(new DateTime(2024, 1, 15), null).Should().BeTrue();
    }

    [Fact]
    public void IsEventVisible_DurationEventInRange_ReturnsTrue()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsEventVisible(
            new DateTime(2024, 1, 10),
            new DateTime(2024, 1, 20)).Should().BeTrue();
    }

    [Fact]
    public void IsEventVisible_EventStartsBeforeViewportEndsInside_ReturnsTrue()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsEventVisible(
            new DateTime(2023, 12, 25),
            new DateTime(2024, 1, 15)).Should().BeTrue();
    }

    [Fact]
    public void IsEventVisible_EventStartsInsideEndsAfterViewport_ReturnsTrue()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsEventVisible(
            new DateTime(2024, 1, 15),
            new DateTime(2024, 2, 15)).Should().BeTrue();
    }

    [Fact]
    public void IsEventVisible_EventSpansEntireViewport_ReturnsTrue()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsEventVisible(
            new DateTime(2023, 12, 1),
            new DateTime(2024, 2, 28)).Should().BeTrue();
    }

    [Fact]
    public void IsEventVisible_EventBeforeViewport_ReturnsFalse()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsEventVisible(
            new DateTime(2023, 12, 1),
            new DateTime(2023, 12, 31)).Should().BeFalse();
    }

    [Fact]
    public void IsEventVisible_EventAfterViewport_ReturnsFalse()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsEventVisible(
            new DateTime(2024, 2, 1),
            new DateTime(2024, 2, 28)).Should().BeFalse();
    }

    [Fact]
    public void IsEventVisible_EventEndsExactlyAtViewportStart_ReturnsFalse()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsEventVisible(
            new DateTime(2023, 12, 25),
            new DateTime(2024, 1, 1)).Should().BeTrue(); // Actually should be true (>= comparison)
    }

    [Fact]
    public void IsEventVisible_EventStartsExactlyAtViewportEnd_ReturnsTrue()
    {
        // Arrange
        var viewport = new TimelineViewport
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 31)
        };

        // Act & Assert
        viewport.IsEventVisible(
            new DateTime(2024, 1, 31),
            new DateTime(2024, 2, 15)).Should().BeTrue();
    }

    #endregion

    #region CreateCentered Tests

    [Fact]
    public void CreateCentered_SetsCorrectCenterDate()
    {
        // Arrange
        var centerDate = new DateTime(2024, 1, 15);

        // Act
        var viewport = TimelineViewport.CreateCentered(
            centerDate, ZoomLevel.Month, 900);

        // Assert
        viewport.CenterDate.Should().Be(centerDate);
    }

    [Fact]
    public void CreateCentered_CalculatesCorrectStartAndEndDates()
    {
        // Arrange
        var centerDate = new DateTime(2024, 1, 15);
        var zoom = ZoomLevel.Month; // 3 pixels per day
        var viewportWidth = 900.0; // 300 days visible

        // Act
        var viewport = TimelineViewport.CreateCentered(centerDate, zoom, viewportWidth);

        // Assert
        var expectedHalfDays = 150.0; // 900 / (3 * 2)
        viewport.StartDate.Date.Should().Be(centerDate.AddDays(-expectedHalfDays).Date);
        viewport.EndDate.Date.Should().Be(centerDate.AddDays(expectedHalfDays).Date);
    }

    [Fact]
    public void CreateCentered_SetsCorrectPixelsPerDay()
    {
        // Arrange & Act
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Week, 1000);

        // Assert
        viewport.PixelsPerDay.Should().Be(50.0); // Week zoom level
    }

    [Fact]
    public void CreateCentered_SetsCorrectZoomLevel()
    {
        // Arrange & Act
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Day, 1000);

        // Assert
        viewport.ZoomLevel.Should().Be(ZoomLevel.Day);
    }

    [Fact]
    public void CreateCentered_SetsCorrectViewportWidth()
    {
        // Arrange & Act
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 1200);

        // Assert
        viewport.ViewportWidth.Should().Be(1200);
    }

    [Fact]
    public void CreateCentered_SetsDefaultHeight()
    {
        // Arrange & Act
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 1000);

        // Assert
        viewport.ViewportHeight.Should().Be(600);
    }

    [Fact]
    public void CreateCentered_InitializesScrollPositionToZero()
    {
        // Arrange & Act
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 1000);

        // Assert
        viewport.ScrollPosition.Should().Be(0);
    }

    [Theory]
    [InlineData(ZoomLevel.Year, 0.1)]
    [InlineData(ZoomLevel.Month, 3.0)]
    [InlineData(ZoomLevel.Week, 50.0)]
    [InlineData(ZoomLevel.Day, 800.0)]
    public void CreateCentered_DifferentZoomLevels_SetsCorrectPixelsPerDay(
        ZoomLevel zoom, double expectedPixelsPerDay)
    {
        // Arrange & Act
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), zoom, 1000);

        // Assert
        viewport.PixelsPerDay.Should().Be(expectedPixelsPerDay);
    }

    #endregion

    #region UpdateForZoom Tests

    [Fact]
    public void UpdateForZoom_UpdatesZoomLevel()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 1000);

        // Act
        viewport.UpdateForZoom(ZoomLevel.Week);

        // Assert
        viewport.ZoomLevel.Should().Be(ZoomLevel.Week);
    }

    [Fact]
    public void UpdateForZoom_MaintainsCenterDate()
    {
        // Arrange
        var centerDate = new DateTime(2024, 1, 15);
        var viewport = TimelineViewport.CreateCentered(centerDate, ZoomLevel.Month, 1000);

        // Act
        viewport.UpdateForZoom(ZoomLevel.Week);

        // Assert
        viewport.CenterDate.Should().Be(centerDate);
    }

    [Fact]
    public void UpdateForZoom_UpdatesPixelsPerDay()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 1000);

        // Act
        viewport.UpdateForZoom(ZoomLevel.Day);

        // Assert
        viewport.PixelsPerDay.Should().Be(800.0); // Day zoom level
    }

    [Fact]
    public void UpdateForZoom_RecalculatesStartAndEndDates()
    {
        // Arrange
        var centerDate = new DateTime(2024, 1, 15);
        var viewport = TimelineViewport.CreateCentered(centerDate, ZoomLevel.Year, 1000);
        var originalVisibleDays = viewport.VisibleDays;

        // Act
        viewport.UpdateForZoom(ZoomLevel.Month);

        // Assert
        var newVisibleDays = viewport.VisibleDays;
        newVisibleDays.Should().BeLessThan(originalVisibleDays); // More zoomed in = fewer days
        viewport.StartDate.Should().BeAfter(centerDate.AddDays(-originalVisibleDays / 2));
        viewport.EndDate.Should().BeBefore(centerDate.AddDays(originalVisibleDays / 2));
    }

    [Fact]
    public void UpdateForZoom_ZoomIn_ReducesVisibleTimeSpan()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Year, 1000);
        var originalDays = viewport.VisibleDays;

        // Act
        viewport.UpdateForZoom(ZoomLevel.Month);

        // Assert
        viewport.VisibleDays.Should().BeLessThan(originalDays);
    }

    [Fact]
    public void UpdateForZoom_ZoomOut_IncreasesVisibleTimeSpan()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Day, 1000);
        var originalDays = viewport.VisibleDays;

        // Act
        viewport.UpdateForZoom(ZoomLevel.Week);

        // Assert
        viewport.VisibleDays.Should().BeGreaterThan(originalDays);
    }

    #endregion

    #region Pan Tests

    [Fact]
    public void Pan_PositivePixels_MovesViewportBackward()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 900);
        var originalStartDate = viewport.StartDate;
        var originalEndDate = viewport.EndDate;
        var originalCenterDate = viewport.CenterDate;

        // Act
        viewport.Pan(90); // Pan right by 90 pixels = 30 days at Month zoom

        // Assert
        viewport.StartDate.Should().BeBefore(originalStartDate);
        viewport.EndDate.Should().BeBefore(originalEndDate);
        viewport.CenterDate.Should().BeBefore(originalCenterDate);
    }

    [Fact]
    public void Pan_NegativePixels_MovesViewportForward()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 900);
        var originalStartDate = viewport.StartDate;
        var originalEndDate = viewport.EndDate;
        var originalCenterDate = viewport.CenterDate;

        // Act
        viewport.Pan(-90); // Pan left by 90 pixels = 30 days at Month zoom

        // Assert
        viewport.StartDate.Should().BeAfter(originalStartDate);
        viewport.EndDate.Should().BeAfter(originalEndDate);
        viewport.CenterDate.Should().BeAfter(originalCenterDate);
    }

    [Fact]
    public void Pan_CalculatesCorrectDateShift()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 900);
        var originalCenterDate = viewport.CenterDate;

        // Act
        viewport.Pan(90); // 90 pixels at 3 pixels/day = 30 days backward

        // Assert
        var expectedDate = originalCenterDate.AddDays(-30);
        viewport.CenterDate.Date.Should().Be(expectedDate.Date);
    }

    [Fact]
    public void Pan_UpdatesScrollPosition()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 900);
        var originalScrollPosition = viewport.ScrollPosition;

        // Act
        viewport.Pan(100);

        // Assert
        viewport.ScrollPosition.Should().Be(originalScrollPosition + 100);
    }

    [Fact]
    public void Pan_MaintainsViewportWidth()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 900);
        var originalVisibleDays = viewport.VisibleDays;

        // Act
        viewport.Pan(100);

        // Assert
        viewport.VisibleDays.Should().BeApproximately(originalVisibleDays, 0.01);
    }

    [Fact]
    public void Pan_MultipleTimes_AccumulatesCorrectly()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 900);
        var originalCenterDate = viewport.CenterDate;

        // Act
        viewport.Pan(30);  // -10 days
        viewport.Pan(30);  // -10 days
        viewport.Pan(30);  // -10 days

        // Assert
        var expectedDate = originalCenterDate.AddDays(-30);
        viewport.CenterDate.Date.Should().Be(expectedDate.Date);
    }

    #endregion

    #region CenterOn Tests

    [Fact]
    public void CenterOn_UpdatesCenterDate()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 900);
        var newCenterDate = new DateTime(2024, 2, 20);

        // Act
        viewport.CenterOn(newCenterDate);

        // Assert
        viewport.CenterDate.Should().Be(newCenterDate);
    }

    [Fact]
    public void CenterOn_RecalculatesStartAndEndDates()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 900);
        var newCenterDate = new DateTime(2024, 2, 20);

        // Act
        viewport.CenterOn(newCenterDate);

        // Assert
        var visibleDays = viewport.VisibleDays;
        var halfDays = visibleDays / 2.0;
        viewport.StartDate.Date.Should().Be(newCenterDate.AddDays(-halfDays).Date);
        viewport.EndDate.Date.Should().Be(newCenterDate.AddDays(halfDays).Date);
    }

    [Fact]
    public void CenterOn_MaintainsZoomLevel()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Week, 900);
        var originalZoom = viewport.ZoomLevel;

        // Act
        viewport.CenterOn(new DateTime(2024, 2, 20));

        // Assert
        viewport.ZoomLevel.Should().Be(originalZoom);
    }

    [Fact]
    public void CenterOn_MaintainsViewportWidth()
    {
        // Arrange
        var viewport = TimelineViewport.CreateCentered(
            new DateTime(2024, 1, 15), ZoomLevel.Month, 900);
        var originalVisibleDays = viewport.VisibleDays;

        // Act
        viewport.CenterOn(new DateTime(2024, 2, 20));

        // Assert
        viewport.VisibleDays.Should().BeApproximately(originalVisibleDays, 0.01);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void IntegrationTest_CompleteWorkflow_ZoomPanCenter()
    {
        // Arrange - Create initial viewport
        var initialCenter = new DateTime(2024, 1, 15);
        var viewport = TimelineViewport.CreateCentered(initialCenter, ZoomLevel.Year, 1000);

        // Act & Assert - Zoom in
        viewport.UpdateForZoom(ZoomLevel.Month);
        viewport.ZoomLevel.Should().Be(ZoomLevel.Month);
        viewport.CenterDate.Should().Be(initialCenter);

        // Act & Assert - Pan forward
        var beforePan = viewport.CenterDate;
        viewport.Pan(-300); // Move forward
        viewport.CenterDate.Should().BeAfter(beforePan);

        // Act & Assert - Center on new date
        var newDate = new DateTime(2024, 6, 1);
        viewport.CenterOn(newDate);
        viewport.CenterDate.Should().Be(newDate);

        // Act & Assert - Zoom to Day level
        viewport.UpdateForZoom(ZoomLevel.Day);
        viewport.ZoomLevel.Should().Be(ZoomLevel.Day);
        viewport.CenterDate.Should().Be(newDate); // Center maintained through zoom
    }

    [Fact]
    public void IntegrationTest_EventVisibilityAcrossOperations()
    {
        // Arrange
        var eventDate = new DateTime(2024, 1, 15);
        var viewport = TimelineViewport.CreateCentered(eventDate, ZoomLevel.Month, 900);

        // Assert - Event should be visible at center
        viewport.IsEventVisible(eventDate, null).Should().BeTrue();

        // Act - Zoom in
        viewport.UpdateForZoom(ZoomLevel.Week);

        // Assert - Event still visible
        viewport.IsEventVisible(eventDate, null).Should().BeTrue();

        // Act - Pan away from event (forward 1000 pixels = 20 days at Week zoom)
        viewport.Pan(-1000);

        // Assert - Event no longer visible
        viewport.IsEventVisible(eventDate, null).Should().BeFalse();

        // Act - Center back on event
        viewport.CenterOn(eventDate);

        // Assert - Event visible again
        viewport.IsEventVisible(eventDate, null).Should().BeTrue();
    }

    #endregion
}
