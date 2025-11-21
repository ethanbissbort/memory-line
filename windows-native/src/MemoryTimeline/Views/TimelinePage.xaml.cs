using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MemoryTimeline.ViewModels;
using Windows.Foundation;

namespace MemoryTimeline.Views;

/// <summary>
/// Timeline visualization page with pan and zoom support.
/// </summary>
public sealed partial class TimelinePage : Page
{
    private readonly TimelineViewModel _viewModel;
    private Point? _lastPointerPosition;
    private bool _isPanning;

    public TimelinePage()
    {
        InitializeComponent();

        // ViewModel is created via XAML DataContext for x:Bind support
        // Get reference to it for code-behind usage
        _viewModel = ViewModel;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Initialize viewport with page dimensions
            var width = (int)TimelineScrollViewer.ActualWidth;
            var height = (int)TimelineScrollViewer.ActualHeight;

            if (width > 0 && height > 0)
            {
                await _viewModel.InitializeAsync(width, height);
                UpdateTodayMarker();
            }
        }
        catch (Exception ex)
        {
            // Log error (in production, use proper logging)
            System.Diagnostics.Debug.WriteLine($"Error loading timeline: {ex.Message}");
        }
    }

    private async void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_viewModel.Viewport != null && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            await _viewModel.UpdateViewportDimensionsAsync((int)e.NewSize.Width, (int)e.NewSize.Height);
            UpdateTodayMarker();
        }
    }

    #region Pan Support

    private void TimelineScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(TimelineScrollViewer);

        // Start panning on left mouse button or touch
        if (pointer.Properties.IsLeftButtonPressed || pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
        {
            _lastPointerPosition = pointer.Position;
            _isPanning = true;
            TimelineScrollViewer.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private async void TimelineScrollViewer_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning || _lastPointerPosition == null || _viewModel.Viewport == null)
            return;

        var currentPosition = e.GetCurrentPoint(TimelineScrollViewer).Position;
        var deltaX = currentPosition.X - _lastPointerPosition.Value.X;

        if (Math.Abs(deltaX) > 2) // Minimum movement threshold
        {
            // Pan the viewport (negative because we're moving the canvas)
            await _viewModel.PanAsync(-deltaX);
            _lastPointerPosition = currentPosition;
            UpdateTodayMarker();
            e.Handled = true;
        }
    }

    private void TimelineScrollViewer_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            _lastPointerPosition = null;
            TimelineScrollViewer.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    #endregion

    #region Zoom Support

    private async void TimelineScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel.Viewport == null)
            return;

        var pointer = e.GetCurrentPoint(TimelineScrollViewer);
        var delta = pointer.Properties.MouseWheelDelta;

        // Ctrl + Wheel = Zoom
        if (e.KeyModifiers == Windows.System.VirtualKeyModifiers.Control)
        {
            // Get the date at the pointer position for zoom center
            var pointerX = pointer.Position.X + TimelineScrollViewer.HorizontalOffset;
            var centerDate = _viewModel.Viewport.PixelToDate(pointerX);

            if (delta > 0)
            {
                await _viewModel.ZoomInAsync();
            }
            else if (delta < 0)
            {
                await _viewModel.ZoomOutAsync();
            }

            UpdateTodayMarker();
            e.Handled = true;
        }
        // Regular wheel = Vertical scroll (default behavior)
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Updates the "Today" marker position on the timeline.
    /// </summary>
    private void UpdateTodayMarker()
    {
        if (_viewModel.Viewport == null)
            return;

        var today = DateTime.Today;

        if (_viewModel.Viewport.IsDateVisible(today))
        {
            var todayX = _viewModel.Viewport.DateToPixel(today);
            Canvas.SetLeft(TodayMarker, todayX);
            TodayMarker.Visibility = Visibility.Visible;
        }
        else
        {
            TodayMarker.Visibility = Visibility.Collapsed;
        }
    }

    #endregion
}
