using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MemoryTimeline.Core.DTOs;
using Windows.UI;

namespace MemoryTimeline.Controls;

/// <summary>
/// Represents a single event on the timeline as a map pin.
/// </summary>
public sealed partial class EventBubble : UserControl
{
    public static readonly DependencyProperty EventProperty =
        DependencyProperty.Register(
            nameof(Event),
            typeof(TimelineEventDto),
            typeof(EventBubble),
            new PropertyMetadata(null, OnEventChanged));

    /// <summary>
    /// Gets or sets the event to display.
    /// </summary>
    public TimelineEventDto? Event
    {
        get => (TimelineEventDto?)GetValue(EventProperty);
        set => SetValue(EventProperty, value);
    }

    /// <summary>
    /// Gets the category brush for the event (pin color).
    /// </summary>
    public SolidColorBrush CategoryBrush
    {
        get
        {
            if (Event == null)
                return new SolidColorBrush(ConvertHexToColor("#E74C3C")); // Default red

            var colorHex = Event.GetCategoryColor();
            return new SolidColorBrush(ConvertHexToColor(colorHex));
        }
    }

    /// <summary>
    /// Gets the category glyph icon.
    /// </summary>
    public string CategoryGlyph => Event?.GetCategoryIcon() ?? "\uE707"; // Default pin icon

    public EventBubble()
    {
        InitializeComponent();
    }

    private static void OnEventChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EventBubble bubble)
        {
            bubble.OnPropertyChanged(nameof(CategoryBrush));
            bubble.OnPropertyChanged(nameof(CategoryGlyph));
        }
    }

    private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Add hover effect - scale up the pin
        PinPath.StrokeThickness = 2;
        PinPath.Stroke = new SolidColorBrush(Colors.White);

        RootGrid.RenderTransform = new CompositeTransform
        {
            ScaleX = 1.15,
            ScaleY = 1.15,
            CenterX = 15,  // Center of the pin
            CenterY = 40   // Bottom of pin (the point)
        };
    }

    private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Remove hover effect
        PinPath.StrokeThickness = 1;
        PinPath.Stroke = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"];

        RootGrid.RenderTransform = new CompositeTransform
        {
            ScaleX = 1.0,
            ScaleY = 1.0,
            CenterX = 15,
            CenterY = 40
        };
    }

    public string FormatDate(DateTime date)
    {
        return date.ToString("MMM d, yyyy");
    }

    public string FormatEndDate(DateTime? date)
    {
        return date.HasValue ? $" - {date.Value:MMM d, yyyy}" : string.Empty;
    }

    private Color ConvertHexToColor(string hex)
    {
        // Remove # if present
        hex = hex.Replace("#", string.Empty);

        if (hex.Length == 6)
        {
            // RGB format
            return Color.FromArgb(
                255,
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
        else if (hex.Length == 8)
        {
            // ARGB format
            return Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16));
        }

        // Default red (standard map pin color)
        return Color.FromArgb(255, 231, 76, 60);
    }

    private void OnPropertyChanged(string propertyName)
    {
        // Trigger binding update
        Bindings.Update();
    }
}
