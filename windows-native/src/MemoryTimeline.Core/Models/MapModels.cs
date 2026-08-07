namespace MemoryTimeline.Core.Models;

/// <summary>
/// The visible window of the offline map (F10): a center coordinate plus a
/// zoom scale over an equirectangular (plate carrée) projection. Pure math,
/// no UI types - fully unit-tested. At <see cref="ZoomScale"/> = 1.0 the full
/// 360 degrees of longitude fit the view width; latitude uses the same
/// pixels-per-degree so the world renders 2:1 without distortion at the
/// projection level.
/// </summary>
public class MapViewport
{
    /// <summary>Smallest allowed zoom (whole world fits the view width).</summary>
    public const double MinZoom = 1.0;

    /// <summary>Largest allowed zoom (street-ish scale; there are no tiles to reveal beyond it).</summary>
    public const double MaxZoom = 256.0;

    /// <summary>Latitude of the view center, decimal degrees, clamped to [-90, 90].</summary>
    public double CenterLat { get; set; }

    /// <summary>Longitude of the view center, decimal degrees, clamped to [-180, 180].</summary>
    public double CenterLon { get; set; }

    /// <summary>Zoom scale, clamped to [<see cref="MinZoom"/>, <see cref="MaxZoom"/>].</summary>
    public double ZoomScale { get; set; } = 1.0;

    /// <summary>
    /// Clamps the center to the world (poles / antimeridian) and the zoom to
    /// its bounds; NaN values reset to safe defaults.
    /// </summary>
    public void Clamp()
    {
        if (double.IsNaN(ZoomScale) || double.IsInfinity(ZoomScale))
        {
            ZoomScale = MinZoom;
        }

        ZoomScale = Math.Clamp(ZoomScale, MinZoom, MaxZoom);

        if (double.IsNaN(CenterLat))
        {
            CenterLat = 0;
        }

        if (double.IsNaN(CenterLon))
        {
            CenterLon = 0;
        }

        CenterLat = Math.Clamp(CenterLat, -90.0, 90.0);
        CenterLon = Math.Clamp(CenterLon, -180.0, 180.0);
    }

    /// <summary>
    /// Pans the map so its CONTENT follows a drag by (deltaX, deltaY) pixels:
    /// dragging right moves the center west, dragging down moves it north.
    /// The center clamps at the poles and the antimeridian.
    /// </summary>
    public void Pan(double deltaXPixels, double deltaYPixels, double viewWidth)
    {
        var pixelsPerDegree = MapProjection.PixelsPerDegree(viewWidth, ZoomScale);
        if (pixelsPerDegree <= 0)
        {
            return;
        }

        CenterLon -= deltaXPixels / pixelsPerDegree;
        CenterLat += deltaYPixels / pixelsPerDegree;
        Clamp();
    }

    /// <summary>
    /// Multiplies the zoom by <paramref name="factor"/> (clamped) while keeping
    /// the geographic point under the anchor pixel fixed - i.e. wheel zoom
    /// about the cursor. A clamped (unchanged) zoom is a no-op.
    /// </summary>
    public void ZoomAbout(double factor, double anchorX, double anchorY, double viewWidth, double viewHeight)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor) || viewWidth <= 0)
        {
            return;
        }

        var (anchorLat, anchorLon) = MapProjection.Unproject(anchorX, anchorY, viewWidth, viewHeight, this);

        ZoomScale = Math.Clamp(ZoomScale * factor, MinZoom, MaxZoom);

        // Re-center so the anchor's geographic point projects back onto the
        // same pixel at the new scale (derived from the Project equations).
        var pixelsPerDegree = MapProjection.PixelsPerDegree(viewWidth, ZoomScale);
        CenterLon = anchorLon - (anchorX - viewWidth / 2.0) / pixelsPerDegree;
        CenterLat = anchorLat + (anchorY - viewHeight / 2.0) / pixelsPerDegree;
        Clamp();
    }
}

/// <summary>
/// Pure equirectangular projection math shared by the map page, the
/// clusterer and the travel animation. x maps longitude linearly, y maps
/// latitude linearly (inverted - screen y grows downwards), both with the
/// same pixels-per-degree so <see cref="Project"/> and <see cref="Unproject"/>
/// are exact inverses.
/// </summary>
public static class MapProjection
{
    /// <summary>Pixels per degree for the given view width and zoom (0 when the width is not positive).</summary>
    public static double PixelsPerDegree(double viewWidth, double zoomScale)
    {
        if (viewWidth <= 0 || zoomScale <= 0)
        {
            return 0;
        }

        return viewWidth / 360.0 * zoomScale;
    }

    /// <summary>Projects a geographic coordinate to view pixels.</summary>
    public static (double X, double Y) Project(
        double latitude,
        double longitude,
        double viewWidth,
        double viewHeight,
        MapViewport viewport)
    {
        var pixelsPerDegree = PixelsPerDegree(viewWidth, viewport.ZoomScale);
        var x = viewWidth / 2.0 + (longitude - viewport.CenterLon) * pixelsPerDegree;
        var y = viewHeight / 2.0 - (latitude - viewport.CenterLat) * pixelsPerDegree;
        return (x, y);
    }

    /// <summary>
    /// Inverse of <see cref="Project"/>: view pixels back to a geographic
    /// coordinate. The result is NOT clamped - a pixel outside the world (deep
    /// space around a zoomed-out map) unprojects beyond +/-90 / +/-180; run it
    /// through <see cref="ClampToWorld"/> before persisting.
    /// </summary>
    public static (double Latitude, double Longitude) Unproject(
        double x,
        double y,
        double viewWidth,
        double viewHeight,
        MapViewport viewport)
    {
        var pixelsPerDegree = PixelsPerDegree(viewWidth, viewport.ZoomScale);
        if (pixelsPerDegree <= 0)
        {
            return (viewport.CenterLat, viewport.CenterLon);
        }

        var longitude = viewport.CenterLon + (x - viewWidth / 2.0) / pixelsPerDegree;
        var latitude = viewport.CenterLat - (y - viewHeight / 2.0) / pixelsPerDegree;
        return (latitude, longitude);
    }

    /// <summary>Clamps a coordinate to the valid world range (poles / antimeridian).</summary>
    public static (double Latitude, double Longitude) ClampToWorld(double latitude, double longitude)
        => (Math.Clamp(latitude, -90.0, 90.0), Math.Clamp(longitude, -180.0, 180.0));
}

/// <summary>One place on the map: a located Location plus its event aggregate.</summary>
public class MapPinDto
{
    public string LocationId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>Number of events linked to this place (after any time filtering).</summary>
    public int EventCount { get; set; }

    public DateTime? EarliestEvent { get; set; }

    public DateTime? LatestEvent { get; set; }
}

/// <summary>
/// A screen-space group of pins produced by <see cref="GridClusterer"/>.
/// A group of one is a plain pin (<see cref="IsCluster"/> false).
/// </summary>
public class MapCluster
{
    public List<MapPinDto> Pins { get; } = new();

    public bool IsCluster => Pins.Count > 1;

    /// <summary>Sum of the member pins' event counts.</summary>
    public int TotalEventCount => Pins.Sum(p => p.EventCount);

    /// <summary>Mean latitude of the member pins.</summary>
    public double CentroidLatitude { get; set; }

    /// <summary>Mean longitude of the member pins.</summary>
    public double CentroidLongitude { get; set; }

    /// <summary>Projected x of the centroid at the clustering viewport.</summary>
    public double X { get; set; }

    /// <summary>Projected y of the centroid at the clustering viewport.</summary>
    public double Y { get; set; }
}

/// <summary>
/// Screen-space grid clustering: pins whose projected positions fall into the
/// same square cell merge into one <see cref="MapCluster"/>. Zooming in
/// spreads the projected positions, so clusters naturally split apart at
/// higher zoom. Pure math - no UI types.
/// </summary>
public static class GridClusterer
{
    public static List<MapCluster> Cluster(
        IEnumerable<MapPinDto> pins,
        double pixelCellSize,
        MapViewport viewport,
        double viewWidth,
        double viewHeight)
    {
        var result = new List<MapCluster>();
        if (pins == null)
        {
            return result;
        }

        var cells = new Dictionary<(long CellX, long CellY), MapCluster>();

        foreach (var pin in pins)
        {
            var (x, y) = MapProjection.Project(pin.Latitude, pin.Longitude, viewWidth, viewHeight, viewport);

            // A non-positive cell size disables clustering: every pin gets a
            // unique cell keyed on its own position.
            var key = pixelCellSize > 0
                ? ((long)Math.Floor(x / pixelCellSize), (long)Math.Floor(y / pixelCellSize))
                : ((long)Math.Round(x * 1000), (long)Math.Round(y * 1000));

            if (!cells.TryGetValue(key, out var cluster))
            {
                cluster = new MapCluster();
                cells[key] = cluster;
                result.Add(cluster);
            }

            cluster.Pins.Add(pin);
        }

        foreach (var cluster in result)
        {
            cluster.CentroidLatitude = cluster.Pins.Average(p => p.Latitude);
            cluster.CentroidLongitude = cluster.Pins.Average(p => p.Longitude);
            var (x, y) = MapProjection.Project(
                cluster.CentroidLatitude, cluster.CentroidLongitude, viewWidth, viewHeight, viewport);
            cluster.X = x;
            cluster.Y = y;
        }

        return result;
    }
}

/// <summary>
/// One leg of the travel-path animation: the line between two consecutive
/// located events of a year, in chronological order.
/// </summary>
public class TravelSegmentDto
{
    public string FromEventId { get; set; } = string.Empty;

    public string FromTitle { get; set; } = string.Empty;

    public DateTime FromDate { get; set; }

    public double FromLatitude { get; set; }

    public double FromLongitude { get; set; }

    public string ToEventId { get; set; } = string.Empty;

    public string ToTitle { get; set; } = string.Empty;

    public DateTime ToDate { get; set; }

    public double ToLatitude { get; set; }

    public double ToLongitude { get; set; }
}
