using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MemoryTimeline.ViewModels;
using MemoryTimeline.Core.DTOs;
using Microsoft.UI.Input.Inking;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Storage.Pickers;

namespace MemoryTimeline.Controls;

/// <summary>
/// Custom timeline visualization control with zoom and pan support.
/// </summary>
public sealed partial class TimelineControl : UserControl
{
    private TimelineViewModel? _viewModel;
    private double _lastScrollPosition;
    private bool _isScrolling;
    private double _lastScale = 1.0;
    private Windows.Foundation.Point _lastManipulationPosition;
    private bool _isManipulating;
    private bool _isInkModeEnabled;
    private InkPresenter? _inkPresenter;

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

        // Initialize ink presenter
        if (TimelineInkCanvas != null)
        {
            _inkPresenter = TimelineInkCanvas.InkPresenter;
            _inkPresenter.InputDeviceTypes = Windows.UI.Core.CoreInputDeviceTypes.Pen |
                                             Windows.UI.Core.CoreInputDeviceTypes.Mouse |
                                             Windows.UI.Core.CoreInputDeviceTypes.Touch;

            // Configure default ink attributes
            var inkAttributes = new InkDrawingAttributes
            {
                Color = Windows.UI.Color.FromArgb(255, 0, 120, 215), // Blue
                Size = new Windows.Foundation.Size(2, 2),
                IgnorePressure = false,
                FitToCurve = true
            };
            _inkPresenter.UpdateDefaultDrawingAttributes(inkAttributes);
        }
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
            var dateAtPosition = Core.Models.TimelineScale.GetDateFromPixelPosition(
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

    #region Windows Ink Handlers

    /// <summary>
    /// Toggles ink mode on/off.
    /// </summary>
    private void InkModeToggle_Click(object sender, RoutedEventArgs e)
    {
        _isInkModeEnabled = InkModeToggle.IsChecked == true;
        ToggleInkMode(_isInkModeEnabled);
    }

    /// <summary>
    /// Toggles ink mode from toolbar button.
    /// </summary>
    private void InkToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isInkModeEnabled = !_isInkModeEnabled;
        InkModeToggle.IsChecked = _isInkModeEnabled;
        ToggleInkMode(_isInkModeEnabled);
    }

    /// <summary>
    /// Helper method to toggle ink mode visibility and interaction.
    /// </summary>
    private void ToggleInkMode(bool enabled)
    {
        TimelineInkCanvas.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        TimelineInkToolbar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        // Disable timeline gestures when in ink mode
        if (enabled)
        {
            TimelineCanvas.ManipulationMode = ManipulationModes.None;
            TimelineScrollViewer.ManipulationMode = ManipulationModes.None;
        }
        else
        {
            TimelineCanvas.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY |
                                             ManipulationModes.Scale | ManipulationModes.TranslateInertia;
            TimelineScrollViewer.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY |
                                                   ManipulationModes.Scale | ManipulationModes.TranslateInertia;
        }
    }

    /// <summary>
    /// Clears all ink strokes from the canvas.
    /// </summary>
    private void InkClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inkPresenter != null)
        {
            var strokes = _inkPresenter.StrokeContainer.GetStrokes();
            foreach (var stroke in strokes)
            {
                stroke.Selected = true;
            }
            _inkPresenter.StrokeContainer.DeleteSelected();
        }
    }

    /// <summary>
    /// Saves ink strokes to a file.
    /// </summary>
    private async void InkSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inkPresenter == null || _inkPresenter.StrokeContainer.GetStrokes().Count == 0)
        {
            await ShowMessageDialog("No Ink", "There are no ink strokes to save.");
            return;
        }

        try
        {
            // Create file save picker
            var savePicker = new FileSavePicker();

            // Initialize with window handle (required for WinUI 3)
            var window = (Application.Current as App)?.Window as MainWindow;
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
            }

            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Ink File", new List<string> { ".gif" });
            savePicker.SuggestedFileName = $"timeline-ink-{DateTime.Now:yyyyMMdd-HHmmss}";

            var file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
                await _inkPresenter.StrokeContainer.SaveAsync(stream);
                await ShowMessageDialog("Success", $"Ink strokes saved to {file.Name}");
            }
        }
        catch (Exception ex)
        {
            await ShowMessageDialog("Error", $"Failed to save ink strokes: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a message dialog.
    /// </summary>
    private async Task ShowMessageDialog(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    #endregion
}
