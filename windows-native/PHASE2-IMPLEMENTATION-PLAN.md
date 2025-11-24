# Phase 2: Timeline Visualization - Implementation Plan

**Status:** 🚀 **IN PROGRESS**
**Start Date:** 2025-11-24
**Branch:** `claude/windows-migration-phase-0-01FPaPzX9vsqV72TgXBRsLmA`
**Prerequisites:** ✅ Phase 0 and Phase 1 complete
**Duration:** 6 weeks (estimated)

---

## Table of Contents

1. [Overview](#overview)
2. [Objectives](#objectives)
3. [Technical Approach](#technical-approach)
4. [Implementation Tasks](#implementation-tasks)
5. [Architecture](#architecture)
6. [Performance Targets](#performance-targets)
7. [Testing Strategy](#testing-strategy)

---

## Overview

Phase 2 focuses on creating a high-performance timeline visualization component that can smoothly render 5000+ events at 60 FPS. This is the core UI component of the Memory Timeline application.

### Key Features to Implement

1. **Custom Timeline Control** - XAML UserControl with DirectX rendering
2. **Virtualization** - Only render visible events for performance
3. **Zoom System** - 4 zoom levels (Year, Month, Week, Day)
4. **Pan System** - Touch, mouse, and keyboard navigation
5. **Event Rendering** - Category-based styling, era gradients
6. **Touch/Pen Support** - Windows Ink, gestures
7. **Animations** - Smooth zoom/pan transitions

---

## Objectives

### Primary Goals

- ✅ Render 5000+ events at 60 FPS
- ✅ Smooth zoom and pan interactions
- ✅ Touch and pen input support
- ✅ Viewport-based virtualization
- ✅ Category-based event styling
- ✅ Era background rendering

### Success Criteria

| Metric | Target | Measurement |
|--------|--------|-------------|
| **FPS** | 60 FPS | GPU profiler |
| **Frame Time** | < 16.67ms | DirectX overlay |
| **Memory (5000 events)** | < 150 MB | Task Manager |
| **Zoom Transition** | < 200ms | Stopwatch |
| **Pan Smoothness** | No dropped frames | Visual inspection |
| **Touch Responsiveness** | < 50ms | Input latency tool |

---

## Technical Approach

### Rendering Strategy

**Option 1: CompositionAPI (Recommended)**
- Use Windows.UI.Composition for hardware-accelerated rendering
- Visual layer for smooth 60 FPS
- Implicit animations for zoom/pan
- Lower-level than XAML but high performance

**Option 2: Win2D**
- CanvasControl with DirectX rendering
- Custom drawing logic
- Full control over rendering
- Good for custom visualizations

**Option 3: ItemsRepeater with Virtualization**
- XAML-based with built-in virtualization
- Easier to implement
- May not hit 60 FPS with 5000 items
- Good starting point

**Decision: Start with ItemsRepeater, migrate to CompositionAPI if needed**

### Virtualization Approach

```csharp
// Only render events within visible viewport
public class TimelineViewport
{
    public DateTime VisibleStart { get; set; }
    public DateTime VisibleEnd { get; set; }
    public double ScrollPosition { get; set; }
    public ZoomLevel CurrentZoom { get; set; }

    public IEnumerable<Event> GetVisibleEvents(IEnumerable<Event> allEvents)
    {
        return allEvents
            .Where(e => e.StartDate >= VisibleStart && e.StartDate <= VisibleEnd)
            .OrderBy(e => e.StartDate);
    }
}
```

### Zoom Levels

```csharp
public enum ZoomLevel
{
    Year,   // 1 pixel = 1 month
    Month,  // 1 pixel = 1 day
    Week,   // 1 pixel = 4 hours
    Day     // 1 pixel = 30 minutes
}

public class TimelineScale
{
    public static double GetPixelsPerDay(ZoomLevel zoom)
    {
        return zoom switch
        {
            ZoomLevel.Year => 0.1,    // ~30 pixels per year
            ZoomLevel.Month => 3.0,   // ~90 pixels per month
            ZoomLevel.Week => 50.0,   // ~350 pixels per week
            ZoomLevel.Day => 800.0,   // ~800 pixels per day
            _ => 1.0
        };
    }
}
```

---

## Implementation Tasks

### Task 1: Timeline Models ✅ (Already exists)

**File:** `MemoryTimeline.Core/Models/TimelineViewport.cs`
**Status:** ✅ Partially implemented

**Extend with:**
- ZoomLevel enum
- TimelineScale helper class
- ViewportCalculator class
- TimelineEventLayout struct

### Task 2: TimelineService

**File:** `MemoryTimeline.Core/Services/ITimelineService.cs` (exists)
**New File:** `MemoryTimeline.Core/Services/TimelineService.cs`

**Interface:**
```csharp
public interface ITimelineService
{
    Task<IEnumerable<Event>> GetEventsInViewportAsync(DateTime start, DateTime end);
    Task<TimelineViewport> CalculateViewportAsync(ZoomLevel zoom, DateTime centerDate);
    Task<IEnumerable<Era>> GetErasInViewportAsync(DateTime start, DateTime end);
    Task<TimelineStatistics> GetTimelineStatisticsAsync();
}
```

**Implementation Tasks:**
- [ ] Implement GetEventsInViewportAsync with date range filtering
- [ ] Implement CalculateViewportAsync with zoom level logic
- [ ] Implement GetErasInViewportAsync for background rendering
- [ ] Add caching for viewport calculations
- [ ] Add performance logging

### Task 3: TimelineViewModel

**File:** `MemoryTimeline/ViewModels/TimelineViewModel.cs` (exists, needs expansion)

**Properties to Add:**
```csharp
// Observable collections
ObservableCollection<TimelineEventViewModel> VisibleEvents
ObservableCollection<EraViewModel> VisibleEras

// Viewport state
ZoomLevel CurrentZoom
DateTime ViewportCenter
DateTime ViewportStart
DateTime ViewportEnd
double ScrollPosition

// UI state
bool IsLoading
bool IsPanning
bool IsZooming
string StatusMessage
int TotalEventCount
int VisibleEventCount
```

**Commands to Add:**
```csharp
IRelayCommand ZoomInCommand
IRelayCommand ZoomOutCommand
IRelayCommand ZoomToYearCommand
IRelayCommand ZoomToMonthCommand
IRelayCommand ZoomToWeekCommand
IRelayCommand ZoomToDayCommand
IRelayCommand PanLeftCommand
IRelayCommand PanRightCommand
IRelayCommand GoToTodayCommand
IRelayCommand RefreshCommand
IAsyncRelayCommand<DateTime> GoToDateCommand
IAsyncRelayCommand<string> GoToEventCommand
```

**Methods to Implement:**
- [ ] LoadEventsInViewportAsync()
- [ ] UpdateViewportAsync(ZoomLevel, DateTime)
- [ ] OnZoomChanged()
- [ ] OnPan(double delta)
- [ ] OnEventSelected(Event)
- [ ] CalculateEventPositions()

### Task 4: Timeline UserControl

**New Files:**
- `MemoryTimeline/Controls/TimelineControl.xaml`
- `MemoryTimeline/Controls/TimelineControl.xaml.cs`

**XAML Structure:**
```xml
<UserControl>
    <Grid>
        <!-- Era background layer -->
        <ItemsControl ItemsSource="{x:Bind ViewModel.VisibleEras}">
            <!-- Era rectangle with gradient -->
        </ItemsControl>

        <!-- Timeline axis/grid -->
        <Canvas x:Name="TimelineAxis">
            <!-- Date labels, tick marks -->
        </Canvas>

        <!-- Event layer with virtualization -->
        <ItemsRepeater ItemsSource="{x:Bind ViewModel.VisibleEvents}"
                       Layout="{StaticResource TimelineLayout}">
            <ItemsRepeater.ItemTemplate>
                <DataTemplate x:DataType="vm:TimelineEventViewModel">
                    <controls:EventBubble Event="{x:Bind}" />
                </DataTemplate>
            </ItemsRepeater.ItemTemplate>
        </ItemsRepeater>

        <!-- Touch/Ink overlay -->
        <InkCanvas x:Name="InkOverlay" Visibility="Collapsed" />

        <!-- Zoom controls -->
        <CommandBar VerticalAlignment="Bottom">
            <AppBarButton Icon="ZoomIn" Command="{x:Bind ViewModel.ZoomInCommand}" />
            <AppBarButton Icon="ZoomOut" Command="{x:Bind ViewModel.ZoomOutCommand}" />
        </CommandBar>
    </Grid>
</UserControl>
```

**Code-Behind:**
- [ ] Implement ManipulationDelta for touch pan
- [ ] Implement PointerWheelChanged for mouse zoom
- [ ] Implement KeyDown for keyboard navigation
- [ ] Add GestureRecognizer for pinch-to-zoom
- [ ] Implement InkPresenter for pen input
- [ ] Add ScrollViewer with viewport tracking

### Task 5: Event Bubble Control

**New Files:**
- `MemoryTimeline/Controls/EventBubble.xaml`
- `MemoryTimeline/Controls/EventBubble.xaml.cs`

**Features:**
- [ ] Circular or pill-shaped bubble
- [ ] Category icon (from Segoe MDL2 Assets)
- [ ] Category color coding
- [ ] Title text (truncated if needed)
- [ ] Tooltip on hover with full details
- [ ] Selection highlight
- [ ] Duration indicator (for multi-day events)

**XAML Template:**
```xml
<UserControl>
    <Grid>
        <Border Background="{x:Bind CategoryColor}"
                CornerRadius="12"
                Padding="8,4">
            <StackPanel Orientation="Horizontal" Spacing="4">
                <FontIcon Glyph="{x:Bind CategoryIcon}" FontSize="16" />
                <TextBlock Text="{x:Bind Event.Title}" />
            </StackPanel>
        </Border>
        <ToolTipService.ToolTip>
            <ToolTip>
                <!-- Detailed event info -->
            </ToolTip>
        </ToolTipService.ToolTip>
    </Grid>
</UserControl>
```

### Task 6: Zoom System

**Implementation Steps:**

1. **Zoom Level Management**
   - [ ] Create ZoomLevel enum (Year, Month, Week, Day)
   - [ ] Implement GetPixelsPerDay() scaling function
   - [ ] Add zoom transition animations

2. **Mouse Wheel Zoom**
   - [ ] PointerWheelChanged event handler
   - [ ] Zoom toward cursor position
   - [ ] Smooth zoom factor calculation

3. **Touch Pinch Zoom**
   - [ ] GestureRecognizer with ManipulationMode.Scale
   - [ ] Track pinch center point
   - [ ] Update zoom level and viewport

4. **Keyboard Zoom**
   - [ ] '+' / '-' keys for zoom in/out
   - [ ] Number keys for direct zoom level
   - [ ] Maintain center position

5. **Zoom Buttons**
   - [ ] CommandBar with zoom buttons
   - [ ] Quick zoom presets

### Task 7: Pan System

**Implementation Steps:**

1. **Touch Pan**
   - [ ] ManipulationMode.TranslateX enabled
   - [ ] ManipulationDelta event handler
   - [ ] Inertia for momentum scrolling
   - [ ] Boundary detection

2. **Mouse Pan**
   - [ ] Click and drag to pan
   - [ ] PointerPressed/PointerMoved/PointerReleased
   - [ ] Cursor change to grab hand

3. **Keyboard Pan**
   - [ ] Arrow keys for navigation
   - [ ] Page Up/Down for larger jumps
   - [ ] Home/End for beginning/end

4. **ScrollViewer Integration**
   - [ ] Horizontal ScrollViewer
   - [ ] ViewportChanged event
   - [ ] Sync scroll position with viewport

### Task 8: Event Rendering

**Rendering Pipeline:**

```
1. Calculate visible date range from viewport
2. Query TimelineService for events in range
3. For each event:
   a. Calculate X position based on date and zoom
   b. Calculate Y position (stacking if overlapping)
   c. Determine size based on duration and zoom
   d. Apply category styling
4. Render in ItemsRepeater with virtualization
```

**Overlap Handling:**
```csharp
// Simple stacking algorithm
public class EventLayoutEngine
{
    public List<EventLayout> CalculateLayout(IEnumerable<Event> events, ZoomLevel zoom)
    {
        var layouts = new List<EventLayout>();
        var tracks = new List<List<Event>>();

        foreach (var evt in events.OrderBy(e => e.StartDate))
        {
            // Find first track where event doesn't overlap
            var track = tracks.FirstOrDefault(t => !t.Any(e => Overlaps(e, evt)));
            if (track == null)
            {
                track = new List<Event>();
                tracks.Add(track);
            }
            track.Add(evt);

            layouts.Add(new EventLayout
            {
                Event = evt,
                X = CalculateX(evt.StartDate, zoom),
                Y = tracks.IndexOf(track) * 30, // 30px per track
                Width = CalculateWidth(evt, zoom),
                Height = 24
            });
        }

        return layouts;
    }
}
```

### Task 9: Era Rendering

**Background Gradient:**
```xaml
<ItemsControl ItemsSource="{x:Bind ViewModel.VisibleEras}">
    <ItemsControl.ItemTemplate>
        <DataTemplate x:DataType="models:Era">
            <Rectangle Fill="{x:Bind ColorBrush}"
                       Opacity="0.1"
                       Canvas.Left="{x:Bind StartX}"
                       Width="{x:Bind Width}"
                       Height="2000" />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

**Era Labels:**
- [ ] Render era name at start of era
- [ ] Semi-transparent background
- [ ] Only show if era is wider than label

### Task 10: Windows Ink Support

**Implementation:**
```csharp
// In TimelineControl.xaml.cs
public void EnableInkMode()
{
    InkOverlay.Visibility = Visibility.Visible;
    InkOverlay.InkPresenter.InputDeviceTypes =
        CoreInputDeviceTypes.Pen | CoreInputDeviceTypes.Touch;

    // Configure ink attributes
    var inkAttributes = new InkDrawingAttributes
    {
        Color = Colors.Blue,
        Size = new Size(2, 2),
        IgnorePressure = false,
        FitToCurve = true
    };
    InkOverlay.InkPresenter.UpdateDefaultDrawingAttributes(inkAttributes);
}

public async Task<string> RecognizeHandwritingAsync()
{
    var recognizer = new InkRecognizerContainer();
    var strokes = InkOverlay.InkPresenter.StrokeContainer.GetStrokes();
    var results = await recognizer.RecognizeAsync(
        InkOverlay.InkPresenter.StrokeContainer,
        InkRecognitionTarget.All);

    return results.FirstOrDefault()?.GetTextCandidates().FirstOrDefault();
}
```

### Task 11: Touch Gestures

**Gestures to Implement:**
1. **Pinch to Zoom** - GestureRecognizer with Scale
2. **Two-Finger Pan** - ManipulationMode.TranslateX
3. **Swipe** - For quick navigation
4. **Long Press** - Context menu
5. **Double Tap** - Zoom to event

**Implementation:**
```csharp
private readonly GestureRecognizer _gestureRecognizer = new();

private void InitializeGestures()
{
    _gestureRecognizer.GestureSettings =
        GestureSettings.ManipulationTranslateX |
        GestureSettings.ManipulationScale |
        GestureSettings.ManipulationTranslateInertia |
        GestureSettings.Tap |
        GestureSettings.DoubleTap |
        GestureSettings.Hold;

    _gestureRecognizer.ManipulationUpdated += OnManipulationUpdated;
    _gestureRecognizer.Tapped += OnTapped;
    _gestureRecognizer.Holding += OnHolding;
}
```

### Task 12: Animations

**Zoom Animation:**
```csharp
public async Task AnimateZoomAsync(ZoomLevel newZoom, DateTime centerDate)
{
    var duration = TimeSpan.FromMilliseconds(200);

    // Animate zoom level
    var zoomAnimation = compositor.CreateScalarKeyFrameAnimation();
    zoomAnimation.InsertKeyFrame(1.0f, (float)GetZoomScale(newZoom));
    zoomAnimation.Duration = duration;

    await visual.StartAnimationAsync("Scale.X", zoomAnimation);

    // Update viewport
    CurrentZoom = newZoom;
    await UpdateViewportAsync(centerDate);
}
```

**Pan Animation:**
```csharp
public async Task AnimatePanAsync(double deltaX)
{
    var duration = TimeSpan.FromMilliseconds(150);

    var panAnimation = compositor.CreateScalarKeyFrameAnimation();
    panAnimation.InsertKeyFrame(1.0f, (float)(ScrollPosition + deltaX));
    panAnimation.Duration = duration;

    await visual.StartAnimationAsync("Offset.X", panAnimation);
}
```

### Task 13: Performance Optimization

**Virtualization:**
- [ ] ItemsRepeater with custom layout
- [ ] Only render visible + buffer events
- [ ] Recycle event controls

**Caching:**
- [ ] Cache viewport calculations
- [ ] Cache event positions
- [ ] Cache category brushes

**Rendering:**
- [ ] Use CompositionVisual for era backgrounds
- [ ] Hardware acceleration enabled
- [ ] Reduce XAML complexity

**Memory:**
- [ ] WeakReference for large collections
- [ ] Dispose unused visuals
- [ ] Object pooling for event controls

### Task 14: High-DPI Support

**Implementation:**
```csharp
// Get current DPI scale
var displayInfo = DisplayInformation.GetForCurrentView();
var dpiScale = displayInfo.LogicalDpi / 96.0;

// Scale coordinates
public double ScaleForDpi(double value)
{
    return value * dpiScale;
}

// Use vector graphics
- SVG icons converted to XAML geometry
- Path-based shapes instead of raster images
- Adaptive sizing based on DPI
```

---

## Architecture

### Component Diagram

```
┌─────────────────────────────────────────────────┐
│               TimelinePage.xaml                 │
└─────────────────┬───────────────────────────────┘
                  │
                  ↓
┌─────────────────────────────────────────────────┐
│            TimelineViewModel                    │
│  - VisibleEvents: ObservableCollection         │
│  - CurrentZoom: ZoomLevel                       │
│  - ViewportCenter: DateTime                     │
│  + ZoomInCommand, PanCommand, etc.              │
└─────────────────┬───────────────────────────────┘
                  │
                  ↓
┌─────────────────────────────────────────────────┐
│             TimelineService                     │
│  + GetEventsInViewportAsync()                   │
│  + CalculateViewportAsync()                     │
│  + GetErasInViewportAsync()                     │
└─────────────────┬───────────────────────────────┘
                  │
                  ↓
┌─────────────────────────────────────────────────┐
│           EventRepository                       │
│  + GetByDateRangeAsync()                        │
│  + GetPagedAsync()                              │
└─────────────────────────────────────────────────┘
```

### Data Flow

```
1. User Action (zoom, pan, etc.)
   ↓
2. TimelineViewModel handles command
   ↓
3. Calculate new viewport (zoom level, date range)
   ↓
4. TimelineService.GetEventsInViewportAsync()
   ↓
5. EventRepository.GetByDateRangeAsync()
   ↓
6. Return events to ViewModel
   ↓
7. ViewModel updates VisibleEvents
   ↓
8. UI updates via data binding
   ↓
9. ItemsRepeater renders visible events
```

---

## Performance Targets

### Rendering Performance

| Scenario | Target | How to Measure |
|----------|--------|----------------|
| **5000 events loaded** | < 2 seconds | Stopwatch |
| **Viewport rendering** | < 50ms | Performance profiler |
| **Frame rate** | 60 FPS | GPU profiler |
| **Frame time** | < 16.67ms | DirectX overlay |
| **Zoom transition** | < 200ms | Stopwatch |
| **Pan scroll** | No jank | Visual inspection |

### Memory Performance

| Scenario | Target | How to Measure |
|----------|--------|----------------|
| **Baseline (empty)** | < 50 MB | Task Manager |
| **100 events** | < 70 MB | Task Manager |
| **1000 events** | < 100 MB | Task Manager |
| **5000 events** | < 150 MB | dotMemory |

### Responsiveness

| Interaction | Target | How to Measure |
|-------------|--------|----------------|
| **Touch response** | < 50ms | Input latency tool |
| **Mouse wheel zoom** | Instant | User perception |
| **Keyboard navigation** | Instant | User perception |
| **Event selection** | < 100ms | Stopwatch |

---

## Testing Strategy

### Unit Tests

**TimelineService Tests:**
```csharp
[Fact]
public async Task GetEventsInViewportAsync_ReturnsOnlyEventsInRange()

[Fact]
public async Task CalculateViewportAsync_YearZoom_ReturnsCorrectRange()

[Fact]
public async Task CalculateViewportAsync_DayZoom_ReturnsCorrectRange()

[Fact]
public async Task GetErasInViewportAsync_ReturnsOverlappingEras()
```

**TimelineViewModel Tests:**
```csharp
[Fact]
public async Task ZoomInCommand_IncreasesZoomLevel()

[Fact]
public async Task PanLeftCommand_MovesViewportLeft()

[Fact]
public async Task UpdateViewportAsync_LoadsVisibleEvents()

[Fact]
public void CalculateEventPositions_HandlesOverlaps()
```

### Integration Tests

```csharp
[Fact]
public async Task TimelinePage_LoadsEventsOnNavigation()

[Fact]
public async Task TimelinePage_ZoomUpdatesVisibleEvents()

[Fact]
public async Task TimelinePage_PanUpdatesVisibleEvents()
```

### Performance Tests

```csharp
[Fact]
public async Task Rendering_5000Events_CompletesUnder2Seconds()

[Fact]
public async Task Viewport_100Events_RendersUnder50Milliseconds()

[Fact]
public void Memory_5000Events_StaysUnder150MB()
```

### UI Tests (Manual)

- [ ] Touch gestures work smoothly
- [ ] Pinch to zoom is responsive
- [ ] Pan has momentum
- [ ] Zoom animations are smooth
- [ ] Events render correctly at all zoom levels
- [ ] Era backgrounds display properly
- [ ] Ink mode works with pen
- [ ] Keyboard shortcuts work

---

## Deliverables

### Code Files

1. **Models:**
   - [x] TimelineViewport.cs (partial)
   - [ ] ZoomLevel.cs
   - [ ] TimelineScale.cs
   - [ ] EventLayout.cs
   - [ ] TimelineStatistics.cs

2. **Services:**
   - [ ] TimelineService.cs
   - [ ] TimelineServiceTests.cs

3. **ViewModels:**
   - [ ] TimelineViewModel.cs (expand existing)
   - [ ] TimelineEventViewModel.cs
   - [ ] EraViewModel.cs

4. **Controls:**
   - [ ] TimelineControl.xaml/xaml.cs
   - [ ] EventBubble.xaml/xaml.cs
   - [ ] TimelineAxis.xaml/xaml.cs

5. **Pages:**
   - [ ] TimelinePage.xaml (expand existing)
   - [ ] TimelinePage.xaml.cs (expand existing)

### Documentation

- [ ] Timeline control usage guide
- [ ] Gesture support documentation
- [ ] Performance optimization notes
- [ ] Phase 2 completion report

### Tests

- [ ] 15+ unit tests for TimelineService
- [ ] 10+ unit tests for TimelineViewModel
- [ ] 5+ integration tests
- [ ] 3+ performance tests

---

## Next Steps

1. ✅ Create this implementation plan
2. ⏳ Implement TimelineService
3. ⏳ Create timeline models
4. ⏳ Expand TimelineViewModel
5. ⏳ Create Timeline UserControl
6. ⏳ Implement event rendering
7. ⏳ Add zoom functionality
8. ⏳ Add pan functionality
9. ⏳ Implement virtualization
10. ⏳ Add Windows Ink support
11. ⏳ Implement touch gestures
12. ⏳ Add animations
13. ⏳ Write tests
14. ⏳ Document Phase 2

---

**Plan Status:** ✅ **COMPLETE**
**Ready to Begin Implementation:** ✅ **YES**

**Next Action:** Start implementing TimelineService

---

**Created By:** Claude AI Assistant
**Date:** 2025-11-24
**Document Version:** 1.0
