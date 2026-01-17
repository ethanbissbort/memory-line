using MemoryTimeline.Data.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

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
