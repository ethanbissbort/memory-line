using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using MemoryTimeline.ViewModels;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Models;

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
    private Windows.Foundation.Point _lastRightTapPosition;
    private TimelineEventDto? _contextMenuEvent;
    private string? _editingEventId;

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

        // Wire up pointer wheel for cursor-anchored zoom
        TimelineCanvas.PointerWheelChanged += TimelineCanvas_PointerWheelChanged;
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
            // Only initialize if not already initialized (preserves state across navigation)
            if (_viewModel.Viewport == null)
            {
                await _viewModel.InitializeAsync(width, height);
            }
            else
            {
                // Just update viewport dimensions if already initialized
                await _viewModel.UpdateViewportDimensionsAsync(width, height);
            }
            UpdateTimelineSize();
            DrawDateMarkers();
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
        var intervalDays = TimelineScale.GetGridInterval(viewport.ZoomLevel);
        var startDate = new DateTime(viewport.StartDate.Year, viewport.StartDate.Month, 1);

        for (var date = startDate; date <= viewport.EndDate; date = date.AddDays(intervalDays))
        {
            var x = TimelineScale.GetPixelPosition(
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

    private string FormatDateLabel(DateTime date, ZoomLevel zoom)
    {
        return zoom switch
        {
            ZoomLevel.Year => date.ToString("yyyy"),
            ZoomLevel.Month => date.ToString("MMM yyyy"),
            ZoomLevel.Week => date.ToString("MMM d"),
            ZoomLevel.Day => date.ToString("MMM d, h:mm tt"),
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

        // Snap back to boundaries if panned beyond limits
        _viewModel.Viewport?.SnapToBoundaries();

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
            var dateAtPosition = TimelineScale.GetDateFromPixel(
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

    #region Right-Click Context Menu Handlers

    private void TimelineCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Store the position for "Add Event Here" functionality
        _lastRightTapPosition = e.GetPosition(TimelineCanvas);
    }

    private void EventBubble_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TimelineEventDto eventDto)
        {
            _contextMenuEvent = eventDto;
        }
    }

    #endregion

    #region Pointer/Cursor Handlers

    /// <summary>
    /// Height of the time ruler zone (in pixels from top).
    /// Wheel zoom over this area triggers cursor-anchored zoom.
    /// </summary>
    private const double TimeRulerZoneHeight = 70.0;

    /// <summary>
    /// Handles mouse wheel for cursor-anchored zoom (Premiere-style).
    /// When cursor is over time ruler: cursor-anchored zoom
    /// When cursor is over track area: horizontal scroll (Alt+wheel = cursor-anchored zoom)
    /// </summary>
    private async void TimelineCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel?.Viewport == null)
            return;

        var point = e.GetCurrentPoint(TimelineCanvas);
        var position = point.Position;
        var wheelDelta = point.Properties.MouseWheelDelta;

        // Check if cursor is in the time ruler zone (top area)
        bool isInTimeRulerZone = position.Y <= TimeRulerZoneHeight;

        // Check for Alt key (alternative zoom modifier for track area)
        var keyState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
        bool isAltPressed = (keyState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (isInTimeRulerZone || isAltPressed)
        {
            // Cursor-anchored zoom: timecode under cursor stays fixed
            await _viewModel.CursorAnchoredZoomAsync(position.X, wheelDelta);
            e.Handled = true;
        }
        else
        {
            // Default: horizontal scroll
            // Let the ScrollViewer handle it, or implement custom scroll
            // For now, do center-anchored zoom for consistency
            await _viewModel.CenterAnchoredZoomAsync(wheelDelta);
            e.Handled = true;
        }
    }

    private void TimelineCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel?.Viewport == null)
            return;

        // Get current pointer position
        var point = e.GetCurrentPoint(TimelineCanvas);
        var position = point.Position;

        // Calculate date at pointer position
        var dateAtPointer = TimelineScale.GetDateFromPixel(
            position.X,
            _viewModel.Viewport.StartDate,
            _viewModel.Viewport.ZoomLevel);

        // Update hover date display
        HoverDateText.Text = dateAtPointer.ToString("MMMM d, yyyy");
        HoverDateText.Visibility = Visibility.Visible;
    }

    private void EventBubble_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Change cursor to hand when hovering over events
        if (sender is FrameworkElement element)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        }
    }

    private void EventBubble_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Reset cursor when leaving events
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    #endregion

    #region Date Navigation Helpers

    /// <summary>
    /// Parses a date string in mmddyy, mmddyyyy, or yyyy format.
    /// Returns null if parsing fails.
    /// Note: No artificial date restrictions - any valid DateTime year (1-9999) is accepted.
    /// </summary>
    private (DateTime? date, bool isYearOnly) ParseDateString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (null, false);

        input = input.Trim();

        // Check for 4-digit year only (e.g., "2006", "1850")
        if (input.Length == 4 && int.TryParse(input, out int year))
        {
            // Accept any valid DateTime year (1-9999)
            if (year >= 1 && year <= 9999)
            {
                return (new DateTime(year, 1, 1), true);
            }
        }

        // Check for 6-digit mmddyy format (e.g., "010190")
        if (input.Length == 6 && int.TryParse(input, out _))
        {
            if (int.TryParse(input.Substring(0, 2), out int month) &&
                int.TryParse(input.Substring(2, 2), out int day) &&
                int.TryParse(input.Substring(4, 2), out int yy))
            {
                // Convert 2-digit year to 4-digit
                // 00-29 = 2000-2029, 30-99 = 1930-1999
                int fullYear = yy <= 29 ? 2000 + yy : 1900 + yy;

                try
                {
                    return (new DateTime(fullYear, month, day), false);
                }
                catch
                {
                    return (null, false);
                }
            }
        }

        // Check for 8-digit mmddyyyy format (e.g., "01011990")
        if (input.Length == 8 && int.TryParse(input, out _))
        {
            if (int.TryParse(input.Substring(0, 2), out int month) &&
                int.TryParse(input.Substring(2, 2), out int day) &&
                int.TryParse(input.Substring(4, 4), out int fullYear))
            {
                try
                {
                    return (new DateTime(fullYear, month, day), false);
                }
                catch
                {
                    return (null, false);
                }
            }
        }

        return (null, false);
    }

    private async void QuickDateBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter || _viewModel == null)
            return;

        var (date, isYearOnly) = ParseDateString(QuickDateBox.Text);
        if (date.HasValue)
        {
            if (isYearOnly)
            {
                // Switch to Year view for year-only input
                _viewModel.SetZoomLevelCommand.Execute("Year");
            }
            await _viewModel.GoToDateCommand.ExecuteAsync(date.Value);
            QuickDateBox.Text = "";
        }
    }

    private async void GoToDateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        GoToDateTextBox.Text = "";
        GoToDateDialog.XamlRoot = XamlRoot;
        await GoToDateDialog.ShowAsync();
    }

    private async void GoToDateDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_viewModel == null)
        {
            args.Cancel = true;
            return;
        }

        var (date, isYearOnly) = ParseDateString(GoToDateTextBox.Text);
        if (!date.HasValue)
        {
            args.Cancel = true;
            return;
        }

        if (isYearOnly)
        {
            // Switch to Year view for year-only input
            _viewModel.SetZoomLevelCommand.Execute("Year");
        }
        await _viewModel.GoToDateCommand.ExecuteAsync(date.Value);
    }

    private void GoToDateTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Allow Enter to trigger the primary button
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            // The dialog will handle the primary button click
        }
    }

    #endregion

    #region Event CRUD Handlers

    private bool _isSyncingDateFields;

    private void EventDateTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingDateFields)
            return;

        var (date, _) = ParseDateString(EventDateTextBox.Text);
        if (date.HasValue)
        {
            _isSyncingDateFields = true;
            EventDatePicker.Date = new DateTimeOffset(date.Value);
            _isSyncingDateFields = false;
        }
    }

    private void EventDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_isSyncingDateFields || !args.NewDate.HasValue)
            return;

        _isSyncingDateFields = true;
        EventDateTextBox.Text = args.NewDate.Value.ToString("MMddyy");
        _isSyncingDateFields = false;
    }

    private async void AddEventButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowAddEventDialogAsync(DateTime.Now);
    }

    private async void AddEventMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.Viewport == null)
            return;

        // Calculate the date at the right-click position
        var dateAtPosition = TimelineScale.GetDateFromPixel(
            _lastRightTapPosition.X,
            _viewModel.Viewport.StartDate,
            _viewModel.Viewport.ZoomLevel);

        await ShowAddEventDialogAsync(dateAtPosition);
    }

    private async Task ShowAddEventDialogAsync(DateTime defaultDate)
    {
        _editingEventId = null;
        EventDialog.Title = "Add Event";

        // Clear and set defaults
        EventTitleBox.Text = "";
        _isSyncingDateFields = true;
        EventDateTextBox.Text = defaultDate.ToString("MMddyy");
        EventDatePicker.Date = new DateTimeOffset(defaultDate);
        _isSyncingDateFields = false;
        EventDescriptionBox.Text = "";
        EventCategoryCombo.SelectedIndex = -1;
        EventLocationBox.Text = "";

        EventDialog.XamlRoot = XamlRoot;
        await EventDialog.ShowAsync();
    }

    private void ViewEventMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuEvent != null)
        {
            _viewModel?.SelectEventCommand.Execute(_contextMenuEvent);
        }
    }

    private async void EditEventMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuEvent != null)
        {
            await ShowEditEventDialogAsync(_contextMenuEvent);
        }
    }

    private async void EditSelectedEvent_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedEvent != null)
        {
            await ShowEditEventDialogAsync(_viewModel.SelectedEvent);
        }
    }

    private async Task ShowEditEventDialogAsync(TimelineEventDto eventDto)
    {
        _editingEventId = eventDto.EventId;
        EventDialog.Title = "Edit Event";

        // Populate with existing values
        EventTitleBox.Text = eventDto.Title ?? "";
        _isSyncingDateFields = true;
        EventDateTextBox.Text = eventDto.StartDate.ToString("MMddyy");
        EventDatePicker.Date = new DateTimeOffset(eventDto.StartDate);
        _isSyncingDateFields = false;
        EventDescriptionBox.Text = eventDto.Description ?? "";
        EventLocationBox.Text = eventDto.Location ?? "";

        // Select the category
        var category = eventDto.Category ?? "other";
        for (int i = 0; i < EventCategoryCombo.Items.Count; i++)
        {
            if (EventCategoryCombo.Items[i] is ComboBoxItem item &&
                item.Tag?.ToString() == category)
            {
                EventCategoryCombo.SelectedIndex = i;
                break;
            }
        }

        EventDialog.XamlRoot = XamlRoot;
        await EventDialog.ShowAsync();
    }

    private async void DeleteEventMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuEvent != null)
        {
            await ConfirmAndDeleteEventAsync(_contextMenuEvent);
        }
    }

    private async void DeleteSelectedEvent_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedEvent != null)
        {
            await ConfirmAndDeleteEventAsync(_viewModel.SelectedEvent);
        }
    }

    private async Task ConfirmAndDeleteEventAsync(TimelineEventDto eventDto)
    {
        DeleteConfirmDialog.XamlRoot = XamlRoot;
        var result = await DeleteConfirmDialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await _viewModel!.DeleteEventAsync(eventDto.EventId);
        }
    }

    private async void EventDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(EventTitleBox.Text))
        {
            args.Cancel = true;
            // Could show validation error here
            return;
        }

        if (!EventDatePicker.Date.HasValue)
        {
            args.Cancel = true;
            return;
        }

        var category = "Other";
        if (EventCategoryCombo.SelectedItem is ComboBoxItem selectedCategory)
        {
            category = selectedCategory.Tag?.ToString() ?? "Other";
        }

        if (_editingEventId == null)
        {
            // Create new event
            await _viewModel!.CreateEventAsync(
                EventTitleBox.Text,
                EventDatePicker.Date.Value.DateTime,
                EventDescriptionBox.Text,
                category,
                EventLocationBox.Text);
        }
        else
        {
            // Update existing event
            await _viewModel!.UpdateEventAsync(
                _editingEventId,
                EventTitleBox.Text,
                EventDatePicker.Date.Value.DateTime,
                EventDescriptionBox.Text,
                category,
                EventLocationBox.Text);
        }
    }

    #endregion
}
