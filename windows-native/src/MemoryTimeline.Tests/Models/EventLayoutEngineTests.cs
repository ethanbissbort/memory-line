using FluentAssertions;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Data.Models;
using Xunit;

namespace MemoryTimeline.Tests.Models;

/// <summary>
/// Unit tests for EventLayoutEngine and EventLayout classes.
/// Tests event positioning, overlap detection, and viewport filtering.
/// </summary>
public class EventLayoutEngineTests
{
    #region Test Data Helpers

    private static Event CreateEvent(string title, DateTime startDate, DateTime? endDate = null, string? category = null)
    {
        return new Event
        {
            EventId = Guid.NewGuid().ToString(),
            Title = title,
            StartDate = startDate,
            EndDate = endDate,
            Category = category ?? "Personal",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    #endregion

    #region CalculateLayout - Basic Functionality Tests

    [Fact]
    public void CalculateLayout_EmptyEventList_ReturnsEmptyList()
    {
        // Arrange
        var events = new List<Event>();
        var referenceDate = new DateTime(2024, 1, 1);
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 1000);

        // Assert
        layouts.Should().BeEmpty();
    }

    [Fact]
    public void CalculateLayout_SingleEvent_PlacedOnTrackZero()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 1, 15))
        };
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 1000);

        // Assert
        layouts.Should().HaveCount(1);
        layouts[0].Track.Should().Be(0);
        layouts[0].Y.Should().Be(0); // Track 0 * 30 = 0
        layouts[0].Event.Title.Should().Be("Event 1");
    }

    [Fact]
    public void CalculateLayout_NonOverlappingEvents_AllOnTrackZero()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 1, 5), new DateTime(2024, 1, 10)),
            CreateEvent("Event 2", new DateTime(2024, 1, 15), new DateTime(2024, 1, 20)),
            CreateEvent("Event 3", new DateTime(2024, 1, 25), new DateTime(2024, 1, 30))
        };
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 10000);

        // Assert
        layouts.Should().HaveCount(3);
        layouts.Should().AllSatisfy(layout => layout.Track.Should().Be(0));
        layouts.Should().AllSatisfy(layout => layout.Y.Should().Be(0));
    }

    [Fact]
    public void CalculateLayout_OverlappingEvents_UsesMultipleTracks()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 1, 5), new DateTime(2024, 1, 15)),
            CreateEvent("Event 2", new DateTime(2024, 1, 10), new DateTime(2024, 1, 20)), // Overlaps with Event 1
            CreateEvent("Event 3", new DateTime(2024, 1, 12), new DateTime(2024, 1, 18))  // Overlaps with both
        };
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 10000);

        // Assert
        layouts.Should().HaveCount(3);
        layouts[0].Track.Should().Be(0); // First event on track 0
        layouts[1].Track.Should().Be(1); // Second event on track 1 (overlaps with first)
        layouts[2].Track.Should().Be(2); // Third event on track 2 (overlaps with both)
    }

    [Fact]
    public void CalculateLayout_ComplexOverlaps_DistributesAcrossTracks()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 1, 5), new DateTime(2024, 1, 10)),
            CreateEvent("Event 2", new DateTime(2024, 1, 8), new DateTime(2024, 1, 12)),  // Overlaps Event 1
            CreateEvent("Event 3", new DateTime(2024, 1, 15), new DateTime(2024, 1, 20)), // No overlap, back to track 0
            CreateEvent("Event 4", new DateTime(2024, 1, 18), new DateTime(2024, 1, 22))  // Overlaps Event 3
        };
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 10000);

        // Assert
        layouts.Should().HaveCount(4);
        layouts[0].Track.Should().Be(0);
        layouts[1].Track.Should().Be(1);
        layouts[2].Track.Should().Be(0); // Can reuse track 0 since Event 1 ended
        layouts[3].Track.Should().Be(1); // Uses track 1 (overlaps Event 3)
    }

    #endregion

    #region CalculateLayout - Position and Size Tests

    [Fact]
    public void CalculateLayout_CalculatesCorrectXPosition()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var eventDate = new DateTime(2024, 1, 15); // 14 days after reference
        var events = new List<Event>
        {
            CreateEvent("Event 1", eventDate)
        };
        var zoom = ZoomLevel.Month; // 3.0 pixels per day

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 10000);

        // Assert
        var expectedX = 14 * 3.0; // 42 pixels
        layouts[0].X.Should().BeApproximately(expectedX, 0.01);
    }

    [Fact]
    public void CalculateLayout_CalculatesCorrectWidth_ForDurationEvent()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1",
                new DateTime(2024, 1, 10),
                new DateTime(2024, 1, 20)) // 10 day duration
        };
        var zoom = ZoomLevel.Month; // 3.0 pixels per day

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 10000);

        // Assert
        var expectedWidth = 10 * 3.0; // 30 pixels
        layouts[0].Width.Should().BeApproximately(expectedWidth, 0.01);
    }

    [Fact]
    public void CalculateLayout_CalculatesCorrectWidth_ForPointEvent()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 1, 15)) // No end date
        };
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 10000);

        // Assert
        var minWidth = TimelineScale.GetMinimumEventWidth(zoom);
        layouts[0].Width.Should().Be(minWidth);
    }

    [Fact]
    public void CalculateLayout_CustomTrackHeight_CalculatesCorrectYPosition()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 1, 5), new DateTime(2024, 1, 10)),
            CreateEvent("Event 2", new DateTime(2024, 1, 8), new DateTime(2024, 1, 12)) // Will be on track 1
        };
        var zoom = ZoomLevel.Month;
        const double customTrackHeight = 50.0;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 10000, customTrackHeight);

        // Assert
        layouts[0].Y.Should().Be(0);                  // Track 0 * 50 = 0
        layouts[1].Y.Should().Be(customTrackHeight);  // Track 1 * 50 = 50
    }

    [Fact]
    public void CalculateLayout_SetsStandardHeight()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 1, 15))
        };
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 10000);

        // Assert
        layouts[0].Height.Should().Be(24.0);
    }

    #endregion

    #region CalculateLayout - Viewport Visibility Tests

    [Fact]
    public void CalculateLayout_EventInViewport_MarkedAsVisible()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 1, 15)) // Will be at ~42 pixels
        };
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 100); // Viewport 0-100 pixels

        // Assert
        layouts[0].IsVisible.Should().BeTrue();
    }

    [Fact]
    public void CalculateLayout_EventBeforeViewport_MarkedAsNotVisible()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 1, 5)) // Will be at ~12 pixels
        };
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 50, 100); // Viewport 50-100 pixels

        // Assert
        layouts[0].IsVisible.Should().BeFalse();
    }

    [Fact]
    public void CalculateLayout_EventAfterViewport_MarkedAsNotVisible()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 2, 15)) // Will be at ~135 pixels
        };
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 100); // Viewport 0-100 pixels

        // Assert
        layouts[0].IsVisible.Should().BeFalse();
    }

    [Fact]
    public void CalculateLayout_DurationEventPartiallyInViewport_MarkedAsVisible()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1",
                new DateTime(2024, 1, 5),   // Start at ~12 pixels
                new DateTime(2024, 1, 25))  // End at ~72 pixels, width ~60
        };
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 50, 100); // Viewport 50-100 pixels (partially overlaps event)

        // Assert
        layouts[0].IsVisible.Should().BeTrue();
    }

    #endregion

    #region CalculateLayout - Edge Cases

    [Fact]
    public void CalculateLayout_EventsAtSameTime_PlacedOnDifferentTracks()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var sameDate = new DateTime(2024, 1, 15);
        var events = new List<Event>
        {
            CreateEvent("Event 1", sameDate),
            CreateEvent("Event 2", sameDate),
            CreateEvent("Event 3", sameDate)
        };
        var zoom = ZoomLevel.Month;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 10000);

        // Assert
        layouts.Should().HaveCount(3);
        layouts[0].Track.Should().Be(0);
        layouts[1].Track.Should().Be(1);
        layouts[2].Track.Should().Be(2);
    }

    [Fact]
    public void CalculateLayout_OverlapBuffer_PreventsAdjacentEventsTouching()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var zoom = ZoomLevel.Week; // 50 pixels per day

        // Create events that end/start on consecutive days
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 1, 5), new DateTime(2024, 1, 10)), // Ends Jan 10
            CreateEvent("Event 2", new DateTime(2024, 1, 10), new DateTime(2024, 1, 15)) // Starts Jan 10
        };

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 10000);

        // Assert
        // With 50 pixels/day, events are close enough that 4-pixel buffer matters
        // Both events should be on same track since there's a buffer preventing overlap
        layouts[0].Track.Should().Be(0);
        layouts[1].Track.Should().Be(1); // Actually, they'll overlap due to the buffer
    }

    [Fact]
    public void CalculateLayout_DifferentZoomLevels_ProducesConsistentResults()
    {
        // Arrange
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("Event 1", new DateTime(2024, 1, 5), new DateTime(2024, 1, 10)),
            CreateEvent("Event 2", new DateTime(2024, 1, 8), new DateTime(2024, 1, 12))
        };

        // Act
        var layoutsYear = EventLayoutEngine.CalculateLayout(
            events, ZoomLevel.Year, referenceDate, 0, 10000);
        var layoutsMonth = EventLayoutEngine.CalculateLayout(
            events, ZoomLevel.Month, referenceDate, 0, 10000);
        var layoutsWeek = EventLayoutEngine.CalculateLayout(
            events, ZoomLevel.Week, referenceDate, 0, 10000);

        // Assert - Track assignments should be consistent across zoom levels
        layoutsYear[0].Track.Should().Be(layoutsMonth[0].Track).And.Be(layoutsWeek[0].Track);
        layoutsYear[1].Track.Should().Be(layoutsMonth[1].Track).And.Be(layoutsWeek[1].Track);
    }

    #endregion

    #region GetVisibleLayouts Tests

    [Fact]
    public void GetVisibleLayouts_AllLayoutsVisible_ReturnsAll()
    {
        // Arrange
        var layouts = new List<EventLayout>
        {
            new() { X = 10, Width = 20 },  // 10-30
            new() { X = 40, Width = 20 },  // 40-60
            new() { X = 70, Width = 20 }   // 70-90
        };

        // Act
        var visible = EventLayoutEngine.GetVisibleLayouts(layouts, 0, 100);

        // Assert
        visible.Should().HaveCount(3);
    }

    [Fact]
    public void GetVisibleLayouts_NoLayoutsVisible_ReturnsEmpty()
    {
        // Arrange
        var layouts = new List<EventLayout>
        {
            new() { X = 10, Width = 20 },   // 10-30
            new() { X = 120, Width = 20 },  // 120-140
            new() { X = 200, Width = 20 }   // 200-220
        };

        // Act
        var visible = EventLayoutEngine.GetVisibleLayouts(layouts, 50, 100);

        // Assert
        visible.Should().BeEmpty();
    }

    [Fact]
    public void GetVisibleLayouts_PartiallyVisible_ReturnsOnlyVisible()
    {
        // Arrange
        var layouts = new List<EventLayout>
        {
            new() { X = 10, Width = 20 },   // 10-30 (before viewport)
            new() { X = 40, Width = 20 },   // 40-60 (visible)
            new() { X = 80, Width = 20 },   // 80-100 (visible)
            new() { X = 120, Width = 20 }   // 120-140 (after viewport)
        };

        // Act
        var visible = EventLayoutEngine.GetVisibleLayouts(layouts, 35, 105);

        // Assert
        visible.Should().HaveCount(2);
        visible[0].X.Should().Be(40);
        visible[1].X.Should().Be(80);
    }

    [Fact]
    public void GetVisibleLayouts_LayoutAtViewportBoundary_IsVisible()
    {
        // Arrange
        var layouts = new List<EventLayout>
        {
            new() { X = 50, Width = 20 }  // 50-70 (starts exactly at viewport start)
        };

        // Act
        var visible = EventLayoutEngine.GetVisibleLayouts(layouts, 50, 100);

        // Assert
        visible.Should().HaveCount(1);
    }

    [Fact]
    public void GetVisibleLayouts_LayoutEndsAtViewportStart_IsNotVisible()
    {
        // Arrange
        var layouts = new List<EventLayout>
        {
            new() { X = 30, Width = 20 }  // 30-50 (ends exactly at viewport start)
        };

        // Act
        var visible = EventLayoutEngine.GetVisibleLayouts(layouts, 50, 100);

        // Assert
        visible.Should().BeEmpty();
    }

    [Fact]
    public void GetVisibleLayouts_LayoutStartsAtViewportEnd_IsNotVisible()
    {
        // Arrange
        var layouts = new List<EventLayout>
        {
            new() { X = 100, Width = 20 }  // 100-120 (starts exactly at viewport end)
        };

        // Act
        var visible = EventLayoutEngine.GetVisibleLayouts(layouts, 50, 100);

        // Assert
        visible.Should().BeEmpty();
    }

    [Fact]
    public void GetVisibleLayouts_EmptyList_ReturnsEmpty()
    {
        // Arrange
        var layouts = new List<EventLayout>();

        // Act
        var visible = EventLayoutEngine.GetVisibleLayouts(layouts, 0, 100);

        // Assert
        visible.Should().BeEmpty();
    }

    #endregion

    #region CalculateTotalHeight Tests

    [Fact]
    public void CalculateTotalHeight_EmptyList_ReturnsMinimumHeight()
    {
        // Arrange
        var layouts = new List<EventLayout>();

        // Act
        var height = EventLayoutEngine.CalculateTotalHeight(layouts);

        // Assert
        height.Should().Be(30.0); // Minimum height = 1 track
    }

    [Fact]
    public void CalculateTotalHeight_SingleTrack_ReturnsTrackHeight()
    {
        // Arrange
        var layouts = new List<EventLayout>
        {
            new() { Track = 0 },
            new() { Track = 0 },
            new() { Track = 0 }
        };

        // Act
        var height = EventLayoutEngine.CalculateTotalHeight(layouts);

        // Assert
        height.Should().Be(30.0); // 1 track * 30
    }

    [Fact]
    public void CalculateTotalHeight_MultipleTracks_ReturnsCorrectHeight()
    {
        // Arrange
        var layouts = new List<EventLayout>
        {
            new() { Track = 0 },
            new() { Track = 1 },
            new() { Track = 2 },
            new() { Track = 1 }
        };

        // Act
        var height = EventLayoutEngine.CalculateTotalHeight(layouts);

        // Assert
        height.Should().Be(90.0); // 3 tracks * 30
    }

    [Fact]
    public void CalculateTotalHeight_CustomTrackHeight_CalculatesCorrectly()
    {
        // Arrange
        var layouts = new List<EventLayout>
        {
            new() { Track = 0 },
            new() { Track = 1 },
            new() { Track = 2 }
        };
        const double customTrackHeight = 50.0;

        // Act
        var height = EventLayoutEngine.CalculateTotalHeight(layouts, customTrackHeight);

        // Assert
        height.Should().Be(150.0); // 3 tracks * 50
    }

    [Fact]
    public void CalculateTotalHeight_NonSequentialTracks_UsesMaxTrack()
    {
        // Arrange
        var layouts = new List<EventLayout>
        {
            new() { Track = 0 },
            new() { Track = 5 },  // Gap in track numbers
            new() { Track = 2 }
        };

        // Act
        var height = EventLayoutEngine.CalculateTotalHeight(layouts);

        // Assert
        height.Should().Be(180.0); // (5 + 1) tracks * 30 = 6 * 30
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void IntegrationTest_RealWorldScenario_HandlesComplexTimeline()
    {
        // Arrange - Simulate a month of events with various overlaps
        var referenceDate = new DateTime(2024, 1, 1);
        var events = new List<Event>
        {
            CreateEvent("New Year", new DateTime(2024, 1, 1)),
            CreateEvent("Work Project", new DateTime(2024, 1, 5), new DateTime(2024, 1, 15)),
            CreateEvent("Personal Trip", new DateTime(2024, 1, 10), new DateTime(2024, 1, 12)),
            CreateEvent("Birthday", new DateTime(2024, 1, 15)),
            CreateEvent("Conference", new DateTime(2024, 1, 20), new DateTime(2024, 1, 22)),
            CreateEvent("Family Dinner", new DateTime(2024, 1, 21)),
            CreateEvent("Month End Review", new DateTime(2024, 1, 31))
        };
        var zoom = ZoomLevel.Month;
        var viewportWidth = 300.0;

        // Act
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, viewportWidth);
        var totalHeight = EventLayoutEngine.CalculateTotalHeight(layouts);
        var visible = EventLayoutEngine.GetVisibleLayouts(layouts, 0, viewportWidth);

        // Assert
        layouts.Should().HaveCount(7);

        // Events should be distributed across tracks where they overlap
        layouts.Should().Contain(l => l.Track == 0);
        layouts.Should().Contain(l => l.Track == 1);

        // All events in January should be visible with a 300px viewport (~100 days at Month zoom)
        visible.Should().HaveCount(7);

        // Total height should accommodate all tracks
        totalHeight.Should().BeGreaterThan(30.0); // More than 1 track
    }

    [Fact]
    public void IntegrationTest_ThousandEvents_PerformanceAndCorrectness()
    {
        // Arrange - Generate many events
        var referenceDate = new DateTime(2024, 1, 1);
        var random = new Random(42); // Fixed seed for reproducibility
        var events = new List<Event>();

        for (int i = 0; i < 1000; i++)
        {
            var startDate = referenceDate.AddDays(random.Next(0, 365));
            var endDate = random.Next(2) == 0 ? (DateTime?)null : startDate.AddDays(random.Next(1, 10));
            events.Add(CreateEvent($"Event {i}", startDate, endDate));
        }

        var zoom = ZoomLevel.Year;

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var layouts = EventLayoutEngine.CalculateLayout(
            events, zoom, referenceDate, 0, 10000);
        stopwatch.Stop();

        // Assert
        layouts.Should().HaveCount(1000);

        // Performance: Should complete in reasonable time (< 1 second for 1000 events)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);

        // All layouts should have valid positions
        layouts.Should().AllSatisfy(layout =>
        {
            layout.X.Should().BeGreaterOrEqualTo(0);
            layout.Width.Should().BeGreaterThan(0);
            layout.Track.Should().BeGreaterOrEqualTo(0);
        });
    }

    #endregion
}
