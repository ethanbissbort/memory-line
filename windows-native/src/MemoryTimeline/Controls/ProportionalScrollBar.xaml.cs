using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using System;

namespace MemoryTimeline.Controls;

/// <summary>
/// A Premiere Pro-style proportional scrollbar where the thumb size represents
/// the viewport as a proportion of the full sequence, and dragging the edges
/// allows zooming.
/// </summary>
public sealed partial class ProportionalScrollBar : UserControl
{
    #region Dependency Properties

    public static readonly DependencyProperty SequenceDurationProperty =
        DependencyProperty.Register(
            nameof(SequenceDuration),
            typeof(double),
            typeof(ProportionalScrollBar),
            new PropertyMetadata(1.0, OnViewportPropertyChanged));

    public static readonly DependencyProperty ScrollOffsetProperty =
        DependencyProperty.Register(
            nameof(ScrollOffset),
            typeof(double),
            typeof(ProportionalScrollBar),
            new PropertyMetadata(0.0, OnViewportPropertyChanged));

    public static readonly DependencyProperty ViewDurationProperty =
        DependencyProperty.Register(
            nameof(ViewDuration),
            typeof(double),
            typeof(ProportionalScrollBar),
            new PropertyMetadata(1.0, OnViewportPropertyChanged));

    public static readonly DependencyProperty MinThumbWidthProperty =
        DependencyProperty.Register(
            nameof(MinThumbWidth),
            typeof(double),
            typeof(ProportionalScrollBar),
            new PropertyMetadata(24.0));

    /// <summary>
    /// Total duration of the sequence (in days for timeline).
    /// </summary>
    public double SequenceDuration
    {
        get => (double)GetValue(SequenceDurationProperty);
        set => SetValue(SequenceDurationProperty, value);
    }

    /// <summary>
    /// Current scroll offset - the start of the visible viewport (in days).
    /// </summary>
    public double ScrollOffset
    {
        get => (double)GetValue(ScrollOffsetProperty);
        set => SetValue(ScrollOffsetProperty, value);
    }

    /// <summary>
    /// Duration of the visible viewport (in days).
    /// </summary>
    public double ViewDuration
    {
        get => (double)GetValue(ViewDurationProperty);
        set => SetValue(ViewDurationProperty, value);
    }

    /// <summary>
    /// Minimum thumb width in pixels for usability.
    /// </summary>
    public double MinThumbWidth
    {
        get => (double)GetValue(MinThumbWidthProperty);
        set => SetValue(MinThumbWidthProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Fired when the user scrolls (drags the thumb body).
    /// </summary>
    public event EventHandler<ScrollOffsetChangedEventArgs>? ScrollOffsetChanged;

    /// <summary>
    /// Fired when the user zooms (drags the thumb edges).
    /// </summary>
    public event EventHandler<ViewDurationChangedEventArgs>? ViewDurationChanged;

    #endregion

    #region Private Fields

    private enum DragZone { None, Track, ThumbBody, ThumbLeftEdge, ThumbRightEdge }

    private DragZone _dragZone = DragZone.None;
    private double _dragStartX;
    private double _dragStartScrollOffset;
    private double _dragStartViewDuration;
    private double _dragStartThumbLeft;
    private double _dragStartThumbWidth;
    private bool _isDragging;

    #endregion

    public ProportionalScrollBar()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
    }

    #region Thumb Calculations

    /// <summary>
    /// Gets the effective track width (accounting for margins).
    /// </summary>
    private double TrackWidth => Math.Max(0, ThumbCanvas.ActualWidth);

    /// <summary>
    /// Calculates the thumb width based on the viewport/sequence ratio.
    /// </summary>
    private double GetThumbWidth()
    {
        if (SequenceDuration <= 0 || TrackWidth <= 0)
            return MinThumbWidth;

        double ratio = ViewDuration / SequenceDuration;
        double rawWidth = ratio * TrackWidth;

        // Enforce minimum thumb size for usability
        return Math.Max(rawWidth, MinThumbWidth);
    }

    /// <summary>
    /// Calculates the thumb left position based on scroll offset.
    /// </summary>
    private double GetThumbLeft()
    {
        double thumbWidth = GetThumbWidth();
        double availableTrackSpace = TrackWidth - thumbWidth;

        if (availableTrackSpace <= 0)
            return 0; // Can't scroll - entire sequence fits in viewport

        // How far can we scroll? (in time units)
        double maxScrollOffset = SequenceDuration - ViewDuration;

        if (maxScrollOffset <= 0)
            return 0;

        // Position ratio: where are we in the scrollable range?
        double positionRatio = ScrollOffset / maxScrollOffset;

        // Map to pixel position
        return positionRatio * availableTrackSpace;
    }

    /// <summary>
    /// Converts thumb left position to scroll offset.
    /// </summary>
    private double ThumbLeftToScrollOffset(double thumbLeft)
    {
        double thumbWidth = GetThumbWidth();
        double availableTrackSpace = TrackWidth - thumbWidth;

        if (availableTrackSpace <= 0)
            return 0; // No scrolling possible

        // Clamp thumb position to valid range
        thumbLeft = Math.Clamp(thumbLeft, 0, availableTrackSpace);

        // Calculate position ratio
        double positionRatio = thumbLeft / availableTrackSpace;

        // Map to scroll offset
        double maxScrollOffset = SequenceDuration - ViewDuration;
        return positionRatio * maxScrollOffset;
    }

    /// <summary>
    /// Updates the thumb visual position and size.
    /// </summary>
    private void UpdateThumbVisual()
    {
        if (Thumb == null || ThumbCanvas == null)
            return;

        double thumbWidth = GetThumbWidth();
        double thumbLeft = GetThumbLeft();

        // Clamp to valid range
        thumbWidth = Math.Min(thumbWidth, TrackWidth);
        thumbLeft = Math.Clamp(thumbLeft, 0, TrackWidth - thumbWidth);

        Thumb.Width = thumbWidth;
        Canvas.SetLeft(Thumb, thumbLeft);
    }

    #endregion

    #region Event Handlers

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateThumbVisual();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateThumbVisual();
    }

    private static void OnViewportPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProportionalScrollBar scrollBar && !scrollBar._isDragging)
        {
            scrollBar.UpdateThumbVisual();
        }
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(ThumbCanvas).Position;
        _dragZone = HitTest(position);

        if (_dragZone == DragZone.None)
            return;

        _dragStartX = position.X;
        _dragStartScrollOffset = ScrollOffset;
        _dragStartViewDuration = ViewDuration;
        _dragStartThumbLeft = GetThumbLeft();
        _dragStartThumbWidth = GetThumbWidth();
        _isDragging = true;

        ThumbCanvas.CapturePointer(e.Pointer);
        VisualStateManager.GoToState(this, "Pressed", true);
        e.Handled = true;
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(ThumbCanvas).Position;

        if (!_isDragging)
        {
            // Update cursor based on hit zone
            var zone = HitTest(position);
            UpdateCursor(zone);
            return;
        }

        double deltaX = position.X - _dragStartX;

        switch (_dragZone)
        {
            case DragZone.ThumbBody:
                HandleThumbDrag(deltaX);
                break;
            case DragZone.ThumbLeftEdge:
                HandleLeftEdgeDrag(position.X);
                break;
            case DragZone.ThumbRightEdge:
                HandleRightEdgeDrag(position.X);
                break;
            case DragZone.Track:
                HandleTrackClick(position.X);
                break;
        }

        e.Handled = true;
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag(e.Pointer);
        e.Handled = true;
    }

    private void RootGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndDrag(null);
    }

    private void EndDrag(Pointer? pointer)
    {
        _isDragging = false;
        _dragZone = DragZone.None;
        VisualStateManager.GoToState(this, "Normal", true);

        if (pointer != null)
        {
            ThumbCanvas.ReleasePointerCapture(pointer);
        }
    }

    #endregion

    #region Hit Testing

    private DragZone HitTest(Windows.Foundation.Point position)
    {
        double thumbLeft = GetThumbLeft();
        double thumbWidth = GetThumbWidth();
        double thumbRight = thumbLeft + thumbWidth;

        double x = position.X;
        const double edgeHitWidth = 8.0;

        // Check if within thumb bounds
        if (x >= thumbLeft && x <= thumbRight)
        {
            // Check left edge
            if (x <= thumbLeft + edgeHitWidth)
                return DragZone.ThumbLeftEdge;

            // Check right edge
            if (x >= thumbRight - edgeHitWidth)
                return DragZone.ThumbRightEdge;

            // Body
            return DragZone.ThumbBody;
        }

        // Click on track (outside thumb)
        return DragZone.Track;
    }

    private void UpdateCursor(DragZone zone)
    {
        // WinUI3 cursor handling through ProtectedCursor
        switch (zone)
        {
            case DragZone.ThumbLeftEdge:
            case DragZone.ThumbRightEdge:
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
                break;
            case DragZone.ThumbBody:
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
                break;
            default:
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
                break;
        }
    }

    #endregion

    #region Drag Handlers

    private void HandleThumbDrag(double deltaX)
    {
        double availableTrackSpace = TrackWidth - _dragStartThumbWidth;
        if (availableTrackSpace <= 0) return;

        // Calculate new thumb left position
        double newThumbLeft = _dragStartThumbLeft + deltaX;
        newThumbLeft = Math.Clamp(newThumbLeft, 0, availableTrackSpace);

        // Convert to scroll offset
        double newScrollOffset = ThumbLeftToScrollOffset(newThumbLeft);

        // Update and notify
        ScrollOffset = newScrollOffset;
        UpdateThumbVisual();
        ScrollOffsetChanged?.Invoke(this, new ScrollOffsetChangedEventArgs(newScrollOffset));
    }

    private void HandleLeftEdgeDrag(double newLeftX)
    {
        // Right edge stays fixed in pixel space during drag
        double thumbRight = _dragStartThumbLeft + _dragStartThumbWidth;

        // Calculate new thumb width
        double newThumbWidth = thumbRight - newLeftX;
        newThumbWidth = Math.Clamp(newThumbWidth, MinThumbWidth, TrackWidth);

        // Derive new view duration from thumb width
        double newThumbWidthRatio = newThumbWidth / TrackWidth;
        double newViewDuration = newThumbWidthRatio * SequenceDuration;

        // Clamp view duration to reasonable limits
        double minViewDuration = SequenceDuration * (MinThumbWidth / TrackWidth);
        newViewDuration = Math.Clamp(newViewDuration, minViewDuration, SequenceDuration);

        // Calculate what time the right edge represents (stays constant)
        double rightEdgeTime = _dragStartScrollOffset + _dragStartViewDuration;

        // New scroll offset = right edge time - new view duration
        double newScrollOffset = rightEdgeTime - newViewDuration;
        newScrollOffset = Math.Clamp(newScrollOffset, 0, SequenceDuration - newViewDuration);

        // Update and notify
        ViewDuration = newViewDuration;
        ScrollOffset = newScrollOffset;
        UpdateThumbVisual();
        ViewDurationChanged?.Invoke(this, new ViewDurationChangedEventArgs(newViewDuration, newScrollOffset));
    }

    private void HandleRightEdgeDrag(double newRightX)
    {
        // Left edge (scroll offset) stays fixed
        double thumbLeft = _dragStartThumbLeft;

        // Calculate new thumb width
        double newThumbWidth = newRightX - thumbLeft;
        newThumbWidth = Math.Clamp(newThumbWidth, MinThumbWidth, TrackWidth - thumbLeft);

        // Derive new view duration
        double newThumbWidthRatio = newThumbWidth / TrackWidth;
        double newViewDuration = newThumbWidthRatio * SequenceDuration;

        // Clamp view duration
        double minViewDuration = SequenceDuration * (MinThumbWidth / TrackWidth);
        newViewDuration = Math.Clamp(newViewDuration, minViewDuration, SequenceDuration);

        // Scroll offset doesn't change - left edge is anchored
        double newScrollOffset = _dragStartScrollOffset;
        newScrollOffset = Math.Clamp(newScrollOffset, 0, SequenceDuration - newViewDuration);

        // Update and notify
        ViewDuration = newViewDuration;
        ScrollOffset = newScrollOffset;
        UpdateThumbVisual();
        ViewDurationChanged?.Invoke(this, new ViewDurationChangedEventArgs(newViewDuration, newScrollOffset));
    }

    private void HandleTrackClick(double clickX)
    {
        // Page scroll toward click position
        double thumbLeft = GetThumbLeft();
        double thumbWidth = GetThumbWidth();
        double thumbCenter = thumbLeft + thumbWidth / 2;

        // Move thumb so its center is at click position
        double newThumbLeft = clickX - thumbWidth / 2;
        double availableTrackSpace = TrackWidth - thumbWidth;
        newThumbLeft = Math.Clamp(newThumbLeft, 0, availableTrackSpace);

        double newScrollOffset = ThumbLeftToScrollOffset(newThumbLeft);

        ScrollOffset = newScrollOffset;
        UpdateThumbVisual();
        ScrollOffsetChanged?.Invoke(this, new ScrollOffsetChangedEventArgs(newScrollOffset));
    }

    #endregion
}

/// <summary>
/// Event args for scroll offset changes.
/// </summary>
public class ScrollOffsetChangedEventArgs : EventArgs
{
    public double NewScrollOffset { get; }

    public ScrollOffsetChangedEventArgs(double newScrollOffset)
    {
        NewScrollOffset = newScrollOffset;
    }
}

/// <summary>
/// Event args for view duration (zoom) changes.
/// </summary>
public class ViewDurationChangedEventArgs : EventArgs
{
    public double NewViewDuration { get; }
    public double NewScrollOffset { get; }

    public ViewDurationChangedEventArgs(double newViewDuration, double newScrollOffset)
    {
        NewViewDuration = newViewDuration;
        NewScrollOffset = newScrollOffset;
    }
}
