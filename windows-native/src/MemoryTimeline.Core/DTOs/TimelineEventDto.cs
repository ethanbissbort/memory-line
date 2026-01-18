using MemoryTimeline.Data.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;

namespace MemoryTimeline.Core.DTOs;

/// <summary>
/// DTO for timeline event display.
/// </summary>
public class TimelineEventDto
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

    // Display properties
    public double PixelX { get; set; }
    public double PixelY { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsVisible { get; set; }
    public bool IsDurationEvent => EndDate.HasValue;

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
            EraColor = evt.Era?.ColorCode
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
    /// Gets the color as a Brush for XAML binding.
    /// </summary>
    public SolidColorBrush ColorBrush
    {
        get
        {
            try
            {
                // Remove # if present
                var hex = ColorCode.Replace("#", string.Empty);

                if (hex.Length == 6)
                {
                    // RGB format
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(
                        255,
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16)));
                }
                else if (hex.Length == 8)
                {
                    // ARGB format
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16),
                        Convert.ToByte(hex.Substring(6, 2), 16)));
                }
            }
            catch
            {
                // Fall back to gray if parsing fails
            }

            return new SolidColorBrush(Colors.Gray);
        }
    }

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
    /// Gets the color as a Brush for XAML binding.
    /// </summary>
    public SolidColorBrush ColorBrush
    {
        get
        {
            try
            {
                var hex = ColorCode.Replace("#", string.Empty);

                if (hex.Length == 6)
                {
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(
                        255,
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16)));
                }
            }
            catch { }

            return new SolidColorBrush(Colors.Gray);
        }
    }

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

    /// <summary>
    /// Gets the color as a Brush for XAML binding.
    /// </summary>
    public SolidColorBrush ColorBrush
    {
        get
        {
            try
            {
                var hex = ColorCode.Replace("#", string.Empty);

                if (hex.Length == 6)
                {
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(
                        255,
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16)));
                }
            }
            catch { }

            return new SolidColorBrush(Colors.Gray);
        }
    }
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
    /// Gets the color as a Brush for XAML binding.
    /// </summary>
    public SolidColorBrush ColorBrush => ParseColorBrush(ColorCode);

    /// <summary>
    /// Gets a darker version of the color for progress fill.
    /// </summary>
    public SolidColorBrush DarkColorBrush
    {
        get
        {
            try
            {
                var hex = ColorCode.Replace("#", string.Empty);
                if (hex.Length == 6)
                {
                    var r = (byte)(Convert.ToByte(hex.Substring(0, 2), 16) * 0.7);
                    var g = (byte)(Convert.ToByte(hex.Substring(2, 2), 16) * 0.7);
                    var b = (byte)(Convert.ToByte(hex.Substring(4, 2), 16) * 0.7);
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
                }
            }
            catch { }
            return new SolidColorBrush(Colors.DarkGray);
        }
    }

    private static SolidColorBrush ParseColorBrush(string colorCode)
    {
        try
        {
            var hex = colorCode.Replace("#", string.Empty);
            if (hex.Length == 6)
            {
                return new SolidColorBrush(Windows.UI.Color.FromArgb(
                    255,
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)));
            }
        }
        catch { }
        return new SolidColorBrush(Colors.Gray);
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
    /// Gets the color as a Brush for XAML binding.
    /// </summary>
    public SolidColorBrush ColorBrush
    {
        get
        {
            try
            {
                var hex = ColorCode.Replace("#", string.Empty);
                if (hex.Length == 6)
                {
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(
                        255,
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16)));
                }
            }
            catch { }
            return new SolidColorBrush(Colors.DodgerBlue);
        }
    }

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
    /// Gets the color as a Brush for XAML binding.
    /// </summary>
    public SolidColorBrush ColorBrush
    {
        get
        {
            try
            {
                var hex = DefaultColor.Replace("#", string.Empty);
                if (hex.Length == 6)
                {
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(
                        255,
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16)));
                }
            }
            catch { }
            return new SolidColorBrush(Colors.Gray);
        }
    }

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
