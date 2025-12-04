using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MemoryTimeline.ViewModels;
using MemoryTimeline.Core.DTOs;

namespace MemoryTimeline.Controls;

/// <summary>
/// Custom timeline visualization control with pen/touch navigation support.
/// Supports pinch-to-zoom, touch panning, momentum scrolling, and double-tap gestures.
/// </summary>
public sealed partial class TimelineControl : UserControl
{
    private TimelineViewModel? _viewModel;
    private double _lastScrollPosition;
    private bool _isScrolling;
    private double _lastScale = 1.0;
    private Windows.Foundation.Point _lastManipulationPosition;
    private bool _isManipulating;

    /// <summary>
    /// Gets or sets the ViewModel for the timeline.
    /// </summary>
    public TimelineViewModel ViewModel
    {
        get => _viewModel!;
        set
        {
            _viewModel = value;
            InitializeTimeline();
        }
    }

    public TimelineControl()
    {
        InitializeComponent();
        Loaded += TimelineControl_Loaded;
        SizeChanged += TimelineControl_SizeChanged;
    }

    private async void TimelineControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        // Initialize with actual control size
        var width = (int)ActualWidth;
        var height = (int)ActualHeight;

        if (width > 0 && height > 0)
        {
            await _viewModel.InitializeAsync(width, height);
            UpdateTimelineSize();
        }
    }

    private async void TimelineControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_viewModel == null || e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            return;

        await _viewModel.UpdateViewportDimensionsAsync(
            (int)e.NewSize.Width,
            (int)e.NewSize.Height);

        UpdateTimelineSize();
    }

    private void InitializeTimeline()
    {
        if (_viewModel == null)
            return;

        // Set up property changed listeners
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TimelineViewModel.IsLoading))
            {
                LoadingOverlay.Visibility = _viewModel.IsLoading
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            else if (e.PropertyName == nameof(TimelineViewModel.Viewport))
            {
                UpdateTimelineSize();
                DrawDateMarkers();
            }
        };
    }

    private void UpdateTimelineSize()
    {
        if (_viewModel?.Viewport == null)
            return;

        // Calculate total timeline width based on date range
        var viewport = _viewModel.Viewport;
        var totalDays = Math.Abs((viewport.EndDate - viewport.StartDate).TotalDays);
        var totalWidth = Math.Max(10000, totalDays * viewport.PixelsPerDay);

        TimelineCanvas.Width = totalWidth;

        // Calculate height based on number of event tracks
        var maxY = _viewModel.Events.Any()
            ? _viewModel.Events.Max(e => e.PixelY + e.Height)
            : 600;

        TimelineCanvas.Height = Math.Max(1000, maxY + 100);
    }

    private void DrawDateMarkers()
    {
        if (_viewModel?.Viewport == null)
            return;

        // Clear existing markers
        AxisCanvas.Children.Clear();

        // Redraw the timeline axis
        var axis = new Microsoft.UI.Xaml.Shapes.Line
        {
            X1 = 0,
            Y1 = 50,
            X2 = TimelineCanvas.Width,
            Y2 = 50,
            Stroke = (Microsoft.UI.Xaml.Media.Brush)Resources["SystemControlForegroundBaseMediumBrush"],
            StrokeThickness = 2
        };
        AxisCanvas.Children.Add(axis);

        // Draw date markers based on zoom level
        var viewport = _viewModel.Viewport;
        var intervalDays = Core.Models.TimelineScale.GetGridInterval(viewport.ZoomLevel);
        var startDate = new DateTime(viewport.StartDate.Year, viewport.StartDate.Month, 1);

        for (var date = startDate; date <= viewport.EndDate; date = date.AddDays(intervalDays))
        {
            var x = Core.Models.TimelineScale.GetPixelPosition(
                date,
                viewport.StartDate,
                viewport.ZoomLevel);

            // Draw tick mark
            var tick = new Microsoft.UI.Xaml.Shapes.Line
            {
                X1 = x,
                Y1 = 50,
                X2 = x,
                Y2 = 60,
                Stroke = (Microsoft.UI.Xaml.Media.Brush)Resources["SystemControlForegroundBaseMediumBrush"],
                StrokeThickness = 1
            };
            Canvas.SetLeft(tick, 0);
            Canvas.SetTop(tick, 0);
            AxisCanvas.Children.Add(tick);

            // Draw date label
            var label = new TextBlock
            {
                Text = FormatDateLabel(date, viewport.ZoomLevel),
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Resources["SystemControlForegroundBaseMediumBrush"]
            };
            Canvas.SetLeft(label, x - 30);
            Canvas.SetTop(label, 25);
            AxisCanvas.Children.Add(label);
        }
    }

    private string FormatDateLabel(DateTime date, Core.Models.ZoomLevel zoom)
    {
        return zoom switch
        {
            Core.Models.ZoomLevel.Year => date.ToString("yyyy"),
            Core.Models.ZoomLevel.Month => date.ToString("MMM yyyy"),
            Core.Models.ZoomLevel.Week => date.ToString("MMM d"),
            Core.Models.ZoomLevel.Day => date.ToString("MMM d, h:mm tt"),
            _ => date.ToString("MMM yyyy")
        };
    }

    private async void TimelineScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_viewModel == null || _isScrolling)
            return;

        _isScrolling = true;

        try
        {
            var scrollViewer = (ScrollViewer)sender;
            var newScrollPosition = scrollViewer.HorizontalOffset;
            var delta = newScrollPosition - _lastScrollPosition;

            // Only update viewport if scrolled significantly
            if (Math.Abs(delta) > 50 && !e.IsIntermediate)
            {
                await _viewModel.PanAsync(-delta);
                _lastScrollPosition = newScrollPosition;
            }
        }
        finally
        {
            _isScrolling = false;
        }
    }

    private void ZoomLevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null || e.AddedItems.Count == 0)
            return;

        var selectedItem = e.AddedItems[0] as ComboBoxItem;
        var tag = selectedItem?.Tag?.ToString();

        if (tag != null)
        {
            _viewModel.SetZoomLevelCommand.Execute(tag);
        }
    }

    private void EventBubble_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TimelineEventDto eventDto)
        {
            _viewModel?.SelectEventCommand.Execute(eventDto);
        }
    }

    #region Touch Gesture Handlers

    /// <summary>
    /// Handles pinch-to-zoom and touch panning gestures.
    /// </summary>
    private async void TimelineCanvas_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (_viewModel == null || _isManipulating)
            return;

        _isManipulating = true;

        try
        {
            // Handle pinch-to-zoom
            if (e.Delta.Scale != 1.0)
            {
                var scaleDelta = e.Delta.Scale;
                var cumulativeScale = e.Cumulative.Scale;

                // Determine zoom direction
                if (scaleDelta > 1.0 && _viewModel.CanZoomIn)
                {
                    // Zoom in - scale > 1.0 means pinch out
                    await _viewModel.ZoomInAsync();
                    _lastScale = cumulativeScale;
                }
                else if (scaleDelta < 1.0 && _viewModel.CanZoomOut)
                {
                    // Zoom out - scale < 1.0 means pinch in
                    await _viewModel.ZoomOutAsync();
                    _lastScale = cumulativeScale;
                }
            }

            // Handle touch panning
            if (e.Delta.Translation.X != 0 || e.Delta.Translation.Y != 0)
            {
                // Pan the timeline based on touch movement
                var panDelta = e.Delta.Translation.X;
                if (Math.Abs(panDelta) > 2)
                {
                    await _viewModel.PanAsync(-panDelta);
                }

                _lastManipulationPosition = e.Position;
            }
        }
        finally
        {
            _isManipulating = false;
        }
    }

    /// <summary>
    /// Handles momentum/inertia at the end of a manipulation gesture.
    /// </summary>
    private async void TimelineCanvas_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        if (_viewModel == null)
            return;

        // Apply inertia/momentum for smooth deceleration
        var velocityX = e.Velocities.Linear.X;

        if (Math.Abs(velocityX) > 0.5)
        {
            // Calculate momentum pan distance based on velocity
            var momentumDistance = velocityX * 50; // Scale factor for momentum

            // Apply momentum pan
            await _viewModel.PanAsync(-momentumDistance);
        }

        _lastScale = 1.0;
        _lastManipulationPosition = default;
    }

    /// <summary>
    /// Handles double-tap to zoom in on a specific location.
    /// </summary>
    private async void TimelineCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_viewModel == null || !_viewModel.CanZoomIn)
            return;

        // Get the position where user double-tapped
        var position = e.GetPosition(TimelineCanvas);

        // Calculate the date at the tapped position
        if (_viewModel.Viewport != null)
        {
            var viewport = _viewModel.Viewport;
            var dateAtPosition = Core.Models.TimelineScale.GetDateFromPixel(
                position.X,
                viewport.StartDate,
                viewport.ZoomLevel);

            // Zoom in centered on the tapped date
            await _viewModel.ZoomInAsync();

            // Pan to center the tapped location (optional - provides better UX)
            var centerOffsetX = position.X - (ActualWidth / 2);
            if (Math.Abs(centerOffsetX) > 50)
            {
                await _viewModel.PanAsync(-centerOffsetX);
            }
        }
    }

    #endregion
}
