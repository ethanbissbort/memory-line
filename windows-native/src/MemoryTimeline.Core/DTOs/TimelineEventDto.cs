using CommunityToolkit.Mvvm.ComponentModel;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Data.Models;
using System.ComponentModel;
using System.Globalization;

namespace MemoryTimeline.Core.DTOs;

/// <summary>
/// How a timeline event is rendered: a fixed-size map pin centered on its
/// start date, or a duration-proportional span bar anchored at its start date.
/// </summary>
public enum EventRenderMode
{
    Pin = 0,
    Span = 1
}

/// <summary>
/// DTO for timeline event display.
/// </summary>
public partial class TimelineEventDto : ObservableObject
{
    public string EventId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Location { get; set; }
    public string? EraId { get; set; }
    public string? EraName { get; set; }
    public string? EraColor { get; set; }

    /// <summary>How much of <see cref="StartDate"/> to believe.</summary>
    public DatePrecision DatePrecision { get; set; } = DatePrecision.Day;

    /// <summary>
    /// Precision-honest date text for tooltips/details (e.g. "Summer 1998"
    /// instead of a fabricated exact day).
    /// </summary>
    public string DisplayDate => DateDisplay.FormatPrecise(StartDate, DatePrecision, EndDate);

    /// <summary>
    /// True when the precision is coarser than Month (Season/Year/Decade/Unknown);
    /// drives the subtle visual cue on the timeline pin.
    /// </summary>
    public bool IsApproximate => DatePrecision >= DatePrecision.Season;

    // Display properties. Observable so the timeline can reposition
    // already-rendered items in place during pan/zoom gestures (compiled
    // OneWay bindings) instead of rebuilding every item container per tick.
    [ObservableProperty]
    private double _pixelX;

    [ObservableProperty]
    private double _pixelY;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    /// <summary>
    /// Clamped X used for RENDERING a span bar: the true <see cref="PixelX"/>
    /// limited to the viewport plus TimelineService.SpanRenderMargin, so a
    /// decades-long span at Day zoom cannot materialize a multi-million-pixel
    /// XAML element. Equals <see cref="PixelX"/> for pins and unclipped spans.
    /// Set by the position calc.
    /// </summary>
    [ObservableProperty]
    private double _renderX;

    /// <summary>
    /// Clamped width paired with <see cref="RenderX"/> (equals
    /// <see cref="Width"/> for pins and unclipped spans). The true
    /// <see cref="Width"/> keeps driving the track-overlap math.
    /// </summary>
    [ObservableProperty]
    private double _renderWidth;

    /// <summary>Pin or duration-proportional span; set by the position calc.</summary>
    [ObservableProperty]
    private EventRenderMode _renderMode = EventRenderMode.Pin;

    /// <summary>
    /// True when the event's date range overlaps the viewport. Set by the
    /// position calc; combined with lane collapse into <see cref="IsVisible"/>.
    /// </summary>
    [ObservableProperty]
    private bool _isInViewport;

    /// <summary>True when the event's swimlane is collapsed (lane modes only).</summary>
    [ObservableProperty]
    private bool _isLaneCollapsed;

    /// <summary>
    /// True when the event's PRECISION WINDOW overlaps the viewport, even if
    /// the anchor date itself is outside it. Set by the position calc; gates
    /// <see cref="ShowUncertaintyBand"/> so a wider-than-viewport band keeps
    /// rendering while any part of its window is on screen.
    /// </summary>
    [ObservableProperty]
    private bool _isWindowInViewport;

    /// <summary>
    /// Whether the event should render: inside the viewport and not hidden by
    /// a collapsed lane. Drives the item's Visibility binding.
    /// </summary>
    public bool IsVisible => IsInViewport && !IsLaneCollapsed;

    partial void OnIsInViewportChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVisible));
    }

    partial void OnIsWindowInViewportChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUncertaintyBand));
    }

    partial void OnIsLaneCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(ShowUncertaintyBand));
    }

    partial void OnWidthChanged(double value)
    {
        OnPropertyChanged(nameof(ShowSpanTitle));
    }

    public bool IsDurationEvent => EndDate.HasValue;

    /// <summary>Span bars show their title inline once they are wide enough.</summary>
    public bool ShowSpanTitle => Width > 80;

    /// <summary>Swimlane assignment (null in Auto mode); set by LaneAssignment.</summary>
    public string? LaneKey { get; set; }

    /// <summary>Index of the event's swimlane (0 in Auto mode); set by LaneAssignment.</summary>
    public int LaneIndex { get; set; }

    /// <summary>
    /// Names of the tags linked to this event, ordered case-insensitively.
    /// Populated from the EventTags navigation when it is loaded (the timeline
    /// viewport query includes it for tag-lane grouping).
    /// </summary>
    public List<string> TagNames { get; set; } = new();

    /// <summary>
    /// Left pixel bound of the DatePrecision uncertainty window
    /// (<see cref="DatePrecisionExtensions.GetWindow"/> at the current scale),
    /// clamped to the viewport. 0 when the event is not approximate.
    /// </summary>
    [ObservableProperty]
    private double _windowStartX;

    /// <summary>Right pixel bound of the uncertainty window (viewport-clamped).</summary>
    [ObservableProperty]
    private double _windowEndX;

    partial void OnWindowStartXChanged(double value)
    {
        OnPropertyChanged(nameof(WindowWidth));
        OnPropertyChanged(nameof(HasUncertaintyWindow));
        OnPropertyChanged(nameof(ShowUncertaintyBand));
    }

    partial void OnWindowEndXChanged(double value)
    {
        OnPropertyChanged(nameof(WindowWidth));
        OnPropertyChanged(nameof(HasUncertaintyWindow));
        OnPropertyChanged(nameof(ShowUncertaintyBand));
    }

    /// <summary>Pixel width of the uncertainty underlay.</summary>
    public double WindowWidth => Math.Max(0, WindowEndX - WindowStartX);

    /// <summary>True when a non-degenerate uncertainty window exists on screen.</summary>
    public bool HasUncertaintyWindow => IsApproximate && WindowEndX > WindowStartX;

    /// <summary>
    /// The underlay renders only when non-degenerate, its WINDOW overlaps the
    /// viewport (anchor visibility is irrelevant - the band must not pop off
    /// mid-pan while the window still covers the screen), and the lane is not
    /// collapsed.
    /// </summary>
    public bool ShowUncertaintyBand => HasUncertaintyWindow && IsWindowInViewport && !IsLaneCollapsed;

    /// <summary>
    /// How many pixels of the RENDERED span element stick out past the LEFT
    /// viewport edge (0 when fully visible; caps at the span render margin
    /// because the render geometry is clamped). Drives the span's left
    /// end-cap chevron, whose inset is measured from the rendered edge.
    /// </summary>
    [ObservableProperty]
    private double _spanLeftOverhang;

    /// <summary>Rendered-element pixels past the RIGHT viewport edge (0 when fully visible; caps at the render margin).</summary>
    [ObservableProperty]
    private double _spanRightOverhang;

    partial void OnSpanLeftOverhangChanged(double value)
    {
        OnPropertyChanged(nameof(ClipsViewportLeft));
    }

    partial void OnSpanRightOverhangChanged(double value)
    {
        OnPropertyChanged(nameof(ClipsViewportRight));
    }

    /// <summary>True when the span extends past the left viewport edge.</summary>
    public bool ClipsViewportLeft => SpanLeftOverhang > 0;

    /// <summary>True when the span extends past the right viewport edge.</summary>
    public bool ClipsViewportRight => SpanRightOverhang > 0;

    /// <summary>
    /// Category/era color as a "#RRGGBB" token, for the uncertainty underlay.
    /// The view turns this into the soft edge-faded gradient via
    /// HexToUncertaintyBrushConverter; Core stays UI-framework-neutral.
    /// </summary>
    public string CategoryColor => GetCategoryColor();

    /// <summary>
    /// Names of the persons linked to this event, ordered by name. Populated
    /// from the EventPeople navigation when it is loaded, or later by the UI
    /// (e.g. when the event is selected).
    /// </summary>
    [ObservableProperty]
    private List<string> _peopleNames = new();

    /// <summary>True when at least one person is linked to this event.</summary>
    public bool HasPeople => PeopleNames.Count > 0;

    /// <summary>Comma-separated list of linked person names for display.</summary>
    public string PeopleDisplay => string.Join(", ", PeopleNames);

    partial void OnPeopleNamesChanged(List<string> value)
    {
        OnPropertyChanged(nameof(HasPeople));
        OnPropertyChanged(nameof(PeopleDisplay));
    }

    /// <summary>
    /// Number of media attachments; drives the pin's photo-count badge.
    /// Populated in the timeline load path via one batched count query, and
    /// kept live by the ViewModel when attachments change.
    /// </summary>
    [ObservableProperty]
    private int _mediaCount;

    /// <summary>True when the event has at least one media attachment.</summary>
    public bool HasMedia => MediaCount > 0;

    partial void OnMediaCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMedia));
    }

    /// <summary>
    /// Creates a DTO from an Event entity.
    /// </summary>
    public static TimelineEventDto FromEvent(Event evt)
    {
        return new TimelineEventDto
        {
            EventId = evt.EventId,
            Title = evt.Title,
            StartDate = evt.StartDate,
            EndDate = evt.EndDate,
            Description = evt.Description,
            Category = evt.Category,
            Location = evt.Location,
            EraId = evt.EraId,
            EraName = evt.Era?.Name,
            EraColor = evt.Era?.ColorCode,
            DatePrecision = evt.DatePrecision,
            PeopleNames = evt.EventPeople
                .Select(ep => ep.Person?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TagNames = evt.EventTags
                .Select(et => et.Tag?.TagName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    /// <summary>
    /// Gets the category color for display.
    /// </summary>
    public string GetCategoryColor()
    {
        if (!string.IsNullOrEmpty(EraColor))
            return EraColor;

        return Category switch
        {
            EventCategory.Milestone => "#FFD700",
            EventCategory.Work => "#4169E1",
            EventCategory.Education => "#32CD32",
            EventCategory.Relationship => "#FF69B4",
            EventCategory.Travel => "#FF8C00",
            EventCategory.Achievement => "#9370DB",
            EventCategory.Challenge => "#DC143C",
            EventCategory.Era => "#808080",
            _ => "#A9A9A9"
        };
    }

    /// <summary>
    /// Gets the category icon (symbol).
    /// </summary>
    public string GetCategoryIcon()
    {
        return Category switch
        {
            EventCategory.Milestone => "\uE735", // Flag
            EventCategory.Work => "\uE821", // Briefcase
            EventCategory.Education => "\uE7BE", // Education
            EventCategory.Relationship => "\uE77B", // People
            EventCategory.Travel => "\uE707", // Globe
            EventCategory.Achievement => "\uE734", // Trophy
            EventCategory.Challenge => "\uE7BA", // Alert
            EventCategory.Era => "\uE787", // Clock
            _ => "\uE8FB" // Circle
        };
    }
}

/// <summary>
/// DTO for timeline era background.
/// </summary>
public class TimelineEraDto
{
    public string EraId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string ColorCode { get; set; } = "#000000";

    // Display properties
    public double PixelX { get; set; }
    public double Width { get; set; }
    public bool IsVisible { get; set; }

    /// <summary>
    /// Creates a DTO from an Era entity.
    /// </summary>
    public static TimelineEraDto FromEra(Era era)
    {
        return new TimelineEraDto
        {
            EraId = era.EraId,
            Name = era.Name,
            StartDate = era.StartDate,
            EndDate = era.EndDate,
            ColorCode = era.ColorCode
        };
    }
}

/// <summary>
/// DTO for time ruler tick marks.
/// Follows Adobe Premiere's adaptive tick density model.
/// </summary>
public class TimeRulerTickDto
{
    public DateTime Date { get; set; }
    public double PixelX { get; set; }
    public bool IsMajor { get; set; }
    public string? Label { get; set; }
    public double TickHeight => IsMajor ? 15.0 : 8.0;
    public double LabelOpacity => IsMajor ? 1.0 : 0.0;
}

/// <summary>
/// DTO for era bar display - thin horizontal colored lines showing time spans.
/// </summary>
public class EraBarDto
{
    public string EraId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string ColorCode { get; set; } = "#808080";

    // Display properties
    public double PixelX { get; set; }
    public double Width { get; set; }
    public double TrackY { get; set; } // Y position within era bars area (stacked)
    public int TrackIndex { get; set; }
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Creates an EraBarDto from a TimelineEraDto.
    /// </summary>
    public static EraBarDto FromEraDto(TimelineEraDto era)
    {
        return new EraBarDto
        {
            EraId = era.EraId,
            Name = era.Name,
            StartDate = era.StartDate,
            EndDate = era.EndDate,
            ColorCode = era.ColorCode,
            PixelX = era.PixelX,
            Width = era.Width,
            IsVisible = era.IsVisible
        };
    }
}

/// <summary>
/// DTO for era visibility filtering in the filter panel.
/// </summary>
public class EraFilterDto : INotifyPropertyChanged
{
    public string EraId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ColorCode { get; set; } = "#808080";

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// DTO for Gantt-style era bar display with full category support.
/// </summary>
public class GanttEraBarDto : INotifyPropertyChanged
{
    public string EraId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string ColorCode { get; set; } = "#808080";
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryIconGlyph { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsOngoing => EndDate == null;

    // Display properties
    public double PixelX { get; set; }
    public double Width { get; set; }
    public double RowY { get; set; }
    public int RowIndex { get; set; }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the duration text for the era.
    /// </summary>
    public string DurationText
    {
        get
        {
            if (EndDate.HasValue)
            {
                var duration = EndDate.Value - StartDate;
                if (duration.TotalDays >= 365)
                {
                    var years = (int)(duration.TotalDays / 365);
                    var months = (int)((duration.TotalDays % 365) / 30);
                    return months > 0 ? $"{years}y {months}m" : $"{years}y";
                }
                else if (duration.TotalDays >= 30)
                {
                    var months = (int)(duration.TotalDays / 30);
                    return $"{months}mo";
                }
                else
                {
                    return $"{(int)duration.TotalDays}d";
                }
            }
            return "Ongoing";
        }
    }

    /// <summary>
    /// A darker shade of <see cref="ColorCode"/> ("#RRGGBB", each channel at
    /// 70%) for progress fill. Falls back to dark gray for a malformed code.
    /// </summary>
    public string DarkColorCode
    {
        get
        {
            var hex = ColorCode.TrimStart('#');
            if (hex.Length == 6
                && byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
                && byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
                && byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                return $"#{(byte)(r * 0.7):X2}{(byte)(g * 0.7):X2}{(byte)(b * 0.7):X2}";
            }

            return "#A9A9A9"; // DarkGray
        }
    }

    /// <summary>
    /// Creates a GanttEraBarDto from an Era entity.
    /// </summary>
    public static GanttEraBarDto FromEra(Era era)
    {
        return new GanttEraBarDto
        {
            EraId = era.EraId,
            Name = era.Name,
            Subtitle = era.Subtitle,
            StartDate = era.StartDate,
            EndDate = era.EndDate,
            ColorCode = era.EffectiveColor,
            CategoryId = era.CategoryId,
            CategoryName = era.Category?.Name,
            CategoryIconGlyph = era.Category?.IconGlyph,
            DisplayOrder = era.DisplayOrder
        };
    }
}

/// <summary>
/// DTO for milestone marker display above the timeline.
/// </summary>
public class MilestoneMarkerDto : INotifyPropertyChanged
{
    public string MilestoneId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public MilestoneType Type { get; set; }
    public string? LinkedEraId { get; set; }
    public string? LinkedEraName { get; set; }
    public string ColorCode { get; set; } = "#0078D4";
    public string? Description { get; set; }

    // Display properties
    public double PixelX { get; set; }
    public double PixelY { get; set; }
    public int StackTier { get; set; } // For collision avoidance

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the icon shape path data based on milestone type.
    /// </summary>
    public string IconPathData => Type switch
    {
        MilestoneType.Standard => "M 10,0 L 20,10 L 10,20 L 0,10 Z", // Diamond
        MilestoneType.Checkpoint => "M 10,0 L 12,7 L 20,7 L 14,12 L 16,20 L 10,15 L 4,20 L 6,12 L 0,7 L 8,7 Z", // Star
        MilestoneType.Kickoff => "M 0,0 L 20,0 L 10,17 Z", // Triangle down
        MilestoneType.Signoff => "M 0,0 L 0,20 L 15,10 Z", // Arrow right
        _ => "M 10,0 L 20,10 L 10,20 L 0,10 Z"
    };

    /// <summary>
    /// Creates a MilestoneMarkerDto from a Milestone entity.
    /// </summary>
    public static MilestoneMarkerDto FromMilestone(Milestone milestone)
    {
        return new MilestoneMarkerDto
        {
            MilestoneId = milestone.MilestoneId,
            Name = milestone.Name,
            Date = milestone.Date,
            Type = milestone.Type,
            LinkedEraId = milestone.LinkedEraId,
            LinkedEraName = milestone.LinkedEra?.Name,
            ColorCode = milestone.DisplayColor,
            Description = milestone.Description
        };
    }
}

/// <summary>
/// DTO for era category display and filtering.
/// </summary>
public class EraCategoryDto : INotifyPropertyChanged
{
    public string CategoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "\uE7C3";
    public string DefaultColor { get; set; } = "#808080";
    public int SortOrder { get; set; }
    public int EraCount { get; set; }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }
    }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChevronGlyph)));
            }
        }
    }

    public string ChevronGlyph => IsExpanded ? "\uE70D" : "\uE70E"; // ChevronDown : ChevronRight

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Creates an EraCategoryDto from an EraCategory entity.
    /// </summary>
    public static EraCategoryDto FromCategory(EraCategory category, int eraCount = 0)
    {
        return new EraCategoryDto
        {
            CategoryId = category.CategoryId,
            Name = category.Name,
            IconGlyph = category.IconGlyph ?? "\uE7C3",
            DefaultColor = category.DefaultColor,
            SortOrder = category.SortOrder,
            IsVisible = category.IsVisible,
            EraCount = eraCount
        };
    }
}

/// <summary>
/// Groups eras by category for Gantt-style display.
/// </summary>
public class EraRowGroupDto
{
    public EraCategoryDto Category { get; set; } = new();
    public List<GanttEraBarDto> Eras { get; set; } = new();

    public EraRowGroupDto() { }

    public EraRowGroupDto(EraCategoryDto category, List<GanttEraBarDto> eras)
    {
        Category = category;
        Eras = eras;
    }
}

/// <summary>
/// Layout constants for era bar visualization.
/// </summary>
public static class EraLayoutConstants
{
    // Vertical dimensions
    public const double MilestoneZoneHeight = 80;
    public const double TimeRulerHeight = 36;
    public const double CategoryHeaderHeight = 28;
    public const double EraRowHeight = 32;
    public const double EraBarHeight = 20;
    public const double RowSpacing = 4;

    // Horizontal dimensions
    public const double RowLabelWidth = 140;
    public const double DateLabelPadding = 8;
    public const double MinBarWidth = 24;

    // Milestone markers
    public const double MilestoneIconSize = 20;
    public const double MilestoneStackOffset = 24;

    // Visual styling
    public const double BarCornerRadius = 3;
    public const double OngoingIndicatorWidth = 12;
}
