using FluentAssertions;
using MemoryTimeline.Core.Models;
using Xunit;

namespace MemoryTimeline.Tests.Models;

/// <summary>
/// Pure math tests for the offline map (F10): equirectangular projection
/// round-trips, viewport pan/zoom clamping at the poles/antimeridian, and
/// screen-space grid clustering. No DB, no UI.
/// </summary>
public class MapModelsTests
{
    private const double ViewWidth = 1024;
    private const double ViewHeight = 640;

    #region Projection round-trips

    [Theory]
    [InlineData(0.0, 0.0, 1.0, 48.8566, 2.3522)]      // world view, Paris
    [InlineData(48.85, 2.35, 8.0, 51.5074, -0.1278)]  // Europe view, London
    [InlineData(-33.87, 151.21, 32.0, -33.8688, 151.2093)] // Sydney close-up
    [InlineData(10.0, -170.0, 2.0, -45.0, 170.0)]     // near the antimeridian
    public void ProjectUnproject_SeveralViewports_RoundTripsExactly(
        double centerLat, double centerLon, double zoom, double latitude, double longitude)
    {
        // Arrange
        var viewport = new MapViewport { CenterLat = centerLat, CenterLon = centerLon, ZoomScale = zoom };

        // Act
        var (x, y) = MapProjection.Project(latitude, longitude, ViewWidth, ViewHeight, viewport);
        var (roundLat, roundLon) = MapProjection.Unproject(x, y, ViewWidth, ViewHeight, viewport);

        // Assert
        roundLat.Should().BeApproximately(latitude, 1e-9);
        roundLon.Should().BeApproximately(longitude, 1e-9);
    }

    [Theory]
    [InlineData(100.0, 100.0)]
    [InlineData(512.0, 320.0)]
    [InlineData(900.5, 30.25)]
    public void UnprojectProject_PixelCoordinates_RoundTripExactly(double x, double y)
    {
        // Arrange
        var viewport = new MapViewport { CenterLat = 20.0, CenterLon = -60.0, ZoomScale = 6.0 };

        // Act
        var (latitude, longitude) = MapProjection.Unproject(x, y, ViewWidth, ViewHeight, viewport);
        var (roundX, roundY) = MapProjection.Project(latitude, longitude, ViewWidth, ViewHeight, viewport);

        // Assert
        roundX.Should().BeApproximately(x, 1e-9);
        roundY.Should().BeApproximately(y, 1e-9);
    }

    [Fact]
    public void Project_CenterCoordinate_LandsOnViewCenter()
    {
        // Arrange
        var viewport = new MapViewport { CenterLat = 37.5, CenterLon = 126.9, ZoomScale = 12.0 };

        // Act
        var (x, y) = MapProjection.Project(37.5, 126.9, ViewWidth, ViewHeight, viewport);

        // Assert
        x.Should().BeApproximately(ViewWidth / 2.0, 1e-9);
        y.Should().BeApproximately(ViewHeight / 2.0, 1e-9);
    }

    [Fact]
    public void Project_DoubledZoom_DoublesDistanceFromCenter()
    {
        // Arrange
        var near = new MapViewport { CenterLat = 0, CenterLon = 0, ZoomScale = 2.0 };
        var far = new MapViewport { CenterLat = 0, CenterLon = 0, ZoomScale = 4.0 };

        // Act
        var (x1, _) = MapProjection.Project(0, 10, ViewWidth, ViewHeight, near);
        var (x2, _) = MapProjection.Project(0, 10, ViewWidth, ViewHeight, far);

        // Assert - offsets from the view center scale linearly with zoom
        (x2 - ViewWidth / 2.0).Should().BeApproximately(2.0 * (x1 - ViewWidth / 2.0), 1e-9);
    }

    [Fact]
    public void ClampToWorld_OutOfRangeCoordinates_ClampToPolesAndAntimeridian()
    {
        MapProjection.ClampToWorld(123.0, 456.0).Should().Be((90.0, 180.0));
        MapProjection.ClampToWorld(-123.0, -456.0).Should().Be((-90.0, -180.0));
        MapProjection.ClampToWorld(45.0, -100.0).Should().Be((45.0, -100.0));
    }

    #endregion

    #region Viewport pan/zoom clamping

    [Fact]
    public void Pan_FarNorth_ClampsCenterLatitudeAtThePole()
    {
        // Arrange - ppd = 800/360*4 ≈ 8.89, so 10000px ≈ 1125 degrees
        var viewport = new MapViewport { CenterLat = 80.0, CenterLon = 0.0, ZoomScale = 4.0 };

        // Act - dragging DOWN moves the view north
        viewport.Pan(0, 10000, 800);

        // Assert
        viewport.CenterLat.Should().Be(90.0);
    }

    [Fact]
    public void Pan_FarWest_ClampsCenterLongitudeAtTheAntimeridian()
    {
        // Arrange
        var viewport = new MapViewport { CenterLat = 0.0, CenterLon = -170.0, ZoomScale = 4.0 };

        // Act - dragging RIGHT moves the view west
        viewport.Pan(10000, 0, 800);

        // Assert
        viewport.CenterLon.Should().Be(-180.0);
    }

    [Fact]
    public void Pan_SmallDrag_MovesCenterOppositeToContentDirection()
    {
        // Arrange - ppd = 1000/360*10 ≈ 27.78 px/degree
        var viewport = new MapViewport { CenterLat = 10.0, CenterLon = 20.0, ZoomScale = 10.0 };

        // Act - drag content right and down by ~one degree's worth of pixels
        viewport.Pan(27.7778, 27.7778, 1000);

        // Assert - center moved west and north
        viewport.CenterLon.Should().BeApproximately(19.0, 1e-3);
        viewport.CenterLat.Should().BeApproximately(11.0, 1e-3);
    }

    [Fact]
    public void ZoomAbout_KeepsTheAnchorGeoPointUnderTheCursor()
    {
        // Arrange
        var viewport = new MapViewport { CenterLat = 20.0, CenterLon = 30.0, ZoomScale = 4.0 };
        var anchorX = 700.0;
        var anchorY = 100.0;
        var (anchorLat, anchorLon) = MapProjection.Unproject(anchorX, anchorY, 1000, 600, viewport);

        // Act
        viewport.ZoomAbout(2.0, anchorX, anchorY, 1000, 600);

        // Assert - the same geographic point still projects onto the anchor pixel
        var (x, y) = MapProjection.Project(anchorLat, anchorLon, 1000, 600, viewport);
        x.Should().BeApproximately(anchorX, 1e-6);
        y.Should().BeApproximately(anchorY, 1e-6);
        viewport.ZoomScale.Should().Be(8.0);
    }

    [Fact]
    public void ZoomAbout_HugeFactor_ClampsToMaxZoom()
    {
        var viewport = new MapViewport { ZoomScale = 100.0 };

        viewport.ZoomAbout(1e9, 500, 300, 1000, 600);

        viewport.ZoomScale.Should().Be(MapViewport.MaxZoom);
    }

    [Fact]
    public void ZoomAbout_TinyFactor_ClampsToMinZoom()
    {
        var viewport = new MapViewport { ZoomScale = 2.0 };

        viewport.ZoomAbout(1e-9, 500, 300, 1000, 600);

        viewport.ZoomScale.Should().Be(MapViewport.MinZoom);
    }

    [Fact]
    public void Clamp_NaNValues_ResetToSafeDefaults()
    {
        var viewport = new MapViewport { CenterLat = double.NaN, CenterLon = double.NaN, ZoomScale = double.NaN };

        viewport.Clamp();

        viewport.CenterLat.Should().Be(0);
        viewport.CenterLon.Should().Be(0);
        viewport.ZoomScale.Should().Be(MapViewport.MinZoom);
    }

    #endregion

    #region Grid clustering

    private static MapPinDto Pin(string id, double lat, double lon, int eventCount) => new()
    {
        LocationId = id,
        Name = id,
        Latitude = lat,
        Longitude = lon,
        EventCount = eventCount
    };

    [Fact]
    public void Cluster_TwoNearbyPinsAtCoarseZoom_MergeIntoOneCluster()
    {
        // Arrange - 0.5 degrees apart is ~1px at world zoom: same 48px cell
        var viewport = new MapViewport { CenterLat = 48.85, CenterLon = 2.6, ZoomScale = 1.0 };
        var pins = new[] { Pin("paris", 48.85, 2.35, 2), Pin("meaux", 48.85, 2.85, 3) };

        // Act
        var clusters = GridClusterer.Cluster(pins, 48.0, viewport, 800, 600);

        // Assert
        clusters.Should().HaveCount(1);
        clusters[0].IsCluster.Should().BeTrue();
        clusters[0].Pins.Should().HaveCount(2);
        clusters[0].TotalEventCount.Should().Be(5);
    }

    [Fact]
    public void Cluster_SamePinsAtFineZoom_StaySeparate()
    {
        // Arrange - at max zoom the same 0.5 degrees is ~284px: different cells
        var viewport = new MapViewport { CenterLat = 48.85, CenterLon = 2.6, ZoomScale = MapViewport.MaxZoom };
        var pins = new[] { Pin("paris", 48.85, 2.35, 2), Pin("meaux", 48.85, 2.85, 3) };

        // Act
        var clusters = GridClusterer.Cluster(pins, 48.0, viewport, 800, 600);

        // Assert
        clusters.Should().HaveCount(2);
        clusters.Should().OnlyContain(c => !c.IsCluster && c.Pins.Count == 1);
    }

    [Fact]
    public void Cluster_Centroid_IsTheMeanOfMemberCoordinates()
    {
        // Arrange - both pins land in the same cell at world zoom
        var viewport = new MapViewport { CenterLat = 15.0, CenterLon = 30.0, ZoomScale = 1.0 };
        var pins = new[] { Pin("a", 10.0, 20.0, 1), Pin("b", 20.0, 40.0, 1) };

        // Act - a cell wider than both projected points, so they share one cluster
        var clusters = GridClusterer.Cluster(pins, 500.0, viewport, 800, 600);

        // Assert
        clusters.Should().HaveCount(1);
        clusters[0].CentroidLatitude.Should().BeApproximately(15.0, 1e-9);
        clusters[0].CentroidLongitude.Should().BeApproximately(30.0, 1e-9);

        // The projected centroid matches projecting the mean coordinate directly
        var (x, y) = MapProjection.Project(15.0, 30.0, 800, 600, viewport);
        clusters[0].X.Should().BeApproximately(x, 1e-9);
        clusters[0].Y.Should().BeApproximately(y, 1e-9);
    }

    [Fact]
    public void Cluster_EmptyInput_ReturnsEmptyList()
    {
        var viewport = new MapViewport();

        var clusters = GridClusterer.Cluster(Array.Empty<MapPinDto>(), 48.0, viewport, 800, 600);

        clusters.Should().BeEmpty();
    }

    #endregion
}
