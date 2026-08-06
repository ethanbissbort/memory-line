using MemoryTimeline.Core.Models;
using MemoryTimeline.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System.ComponentModel;
using Windows.Foundation;

namespace MemoryTimeline.Views;

/// <summary>
/// Offline map page (F10): pins over a custom equirectangular projection
/// drawn on plain XAML (graticule + dark backdrop) - deliberately NO WinUI
/// MapControl, which would require an Azure Maps token and network tiles.
/// Code-behind owns the view-only concerns: pointer pan/zoom routing,
/// graticule drawing, the travel polyline and the map clip.
/// </summary>
public sealed partial class MapPage : Page
{
    public MapViewModel ViewModel { get; }

    private bool _isPanning;
    private Point _lastPanPoint;
    private bool _isSyncingTravelYear;

    public MapPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<MapViewModel>();

        // Transient VM created per page instance - both die together, so these
        // subscriptions cannot root anything beyond the page's own lifetime.
        ViewModel.TravelPathChanged += OnTravelPathChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
        RedrawGraticule();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Cleanup(); // stop the travel timer
    }

    /// <summary>
    /// Clears the error when the InfoBar is dismissed so the OneWay IsOpen
    /// binding does not immediately reopen it.
    /// </summary>
    private void ErrorBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.ErrorMessage = null;
    }

    private void StatusBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.StatusMessage = null;
    }

    #region Map surface: size, pan, zoom, pin drop

    private void MapHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep pins/graticule inside the rounded map surface.
        MapHost.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
        };

        ViewModel.SetViewSize(e.NewSize.Width, e.NewSize.Height);
        RedrawGraticule();
    }

    private void MapHost_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(MapHost);
        // Touch contacts report IsLeftButtonPressed too, so this covers mouse
        // drag and touch drag alike.
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isPanning = MapHost.CapturePointer(e.Pointer);
        _lastPanPoint = point.Position;
    }

    private void MapHost_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var position = e.GetCurrentPoint(MapHost).Position;
        var deltaX = position.X - _lastPanPoint.X;
        var deltaY = position.Y - _lastPanPoint.Y;
        if (Math.Abs(deltaX) < 0.01 && Math.Abs(deltaY) < 0.01)
        {
            return;
        }

        _lastPanPoint = position;
        ViewModel.PanBy(deltaX, deltaY);
        RedrawGraticule();
    }

    private void MapHost_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            MapHost.ReleasePointerCapture(e.Pointer);
        }
    }

    private void MapHost_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isPanning = false;
    }

    private void MapHost_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isPanning = false;
    }

    private void MapHost_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(MapHost);
        var wheelDelta = point.Properties.MouseWheelDelta;
        if (wheelDelta == 0)
        {
            return;
        }

        // Zoom about the cursor: the place under the pointer stays put.
        var factor = wheelDelta > 0 ? 1.25 : 0.8;
        ViewModel.ZoomAboutPoint(factor, point.Position.X, point.Position.Y);
        RedrawGraticule();
        e.Handled = true;
    }

    /// <summary>
    /// Pin-drop completion: while a drop is armed, the next map click
    /// unprojects to coordinates (pins are hit-test-disabled meanwhile, so
    /// the click always lands here). Errors surface via the VM's InfoBar.
    /// </summary>
    private async void MapHost_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!ViewModel.IsPinDropMode)
        {
            return;
        }

        var position = e.GetPosition(MapHost);
        e.Handled = true;
        await ViewModel.CompletePinDropAsync(position.X, position.Y);
    }

    private void ClusterBadge_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MapDisplayPin pin && pin.IsCluster)
        {
            ViewModel.ZoomIntoCluster(pin);
            RedrawGraticule();
        }
    }

    #endregion

    #region Travel animation glue

    /// <summary>
    /// ComboBox.SelectedItem is object-typed, so the year selection is synced
    /// in code-behind instead of an x:Bind cast (same reason the event dialog
    /// syncs its date fields manually).
    /// </summary>
    private void TravelYearCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingTravelYear)
        {
            return;
        }

        _isSyncingTravelYear = true;
        ViewModel.SelectedTravelYear = TravelYearCombo.SelectedItem as string;
        _isSyncingTravelYear = false;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MapViewModel.SelectedTravelYear) || _isSyncingTravelYear)
        {
            return;
        }

        _isSyncingTravelYear = true;
        TravelYearCombo.SelectedItem = ViewModel.SelectedTravelYear;
        _isSyncingTravelYear = false;
    }

    private void OnTravelPathChanged(object? sender, IReadOnlyList<Point> points)
    {
        var collection = new PointCollection();
        foreach (var point in points)
        {
            collection.Add(point);
        }

        TravelPolyline.Points = collection;
    }

    #endregion

    #region Graticule

    /// <summary>
    /// Redraws the 30-degree graticule (equator / prime meridian slightly
    /// brighter) plus the world edge, clipped to the world's projected extent.
    /// Cheap enough to run per pan/zoom tick: at most ~20 Line elements.
    /// </summary>
    private void RedrawGraticule()
    {
        GraticuleCanvas.Children.Clear();

        var width = MapHost.ActualWidth;
        var height = MapHost.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var viewport = ViewModel.Viewport;

        var minorBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 255, 255, 255));
        var majorBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 255, 255, 255));

        // The world's projected bounding box, clamped to the view.
        var topY = Math.Max(0, MapProjection.Project(90, viewport.CenterLon, width, height, viewport).Y);
        var bottomY = Math.Min(height, MapProjection.Project(-90, viewport.CenterLon, width, height, viewport).Y);
        var leftX = Math.Max(0, MapProjection.Project(viewport.CenterLat, -180, width, height, viewport).X);
        var rightX = Math.Min(width, MapProjection.Project(viewport.CenterLat, 180, width, height, viewport).X);
        if (bottomY <= topY || rightX <= leftX)
        {
            return;
        }

        for (var longitude = -180; longitude <= 180; longitude += 30)
        {
            var x = MapProjection.Project(0, longitude, width, height, viewport).X;
            if (x < 0 || x > width)
            {
                continue;
            }

            GraticuleCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = topY,
                Y2 = bottomY,
                Stroke = longitude == 0 ? majorBrush : minorBrush,
                StrokeThickness = 1
            });
        }

        for (var latitude = -90; latitude <= 90; latitude += 30)
        {
            var y = MapProjection.Project(latitude, 0, width, height, viewport).Y;
            if (y < 0 || y > height)
            {
                continue;
            }

            GraticuleCanvas.Children.Add(new Line
            {
                X1 = leftX,
                X2 = rightX,
                Y1 = y,
                Y2 = y,
                Stroke = latitude == 0 ? majorBrush : minorBrush,
                StrokeThickness = 1
            });
        }
    }

    #endregion
}
