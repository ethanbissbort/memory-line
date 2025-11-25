using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MemoryTimeline.Core.DTOs;
using Windows.UI;

namespace MemoryTimeline.Controls;

/// <summary>
/// Represents a single event on the timeline.
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
    /// Gets the category brush for the event.
    /// </summary>
    public SolidColorBrush CategoryBrush
    {
        get
        {
            if (Event == null)
                return new SolidColorBrush(Colors.Gray);

            var colorHex = Event.GetCategoryColor();
            return new SolidColorBrush(ConvertHexToColor(colorHex));
        }
    }

    /// <summary>
    /// Gets the category glyph icon.
    /// </summary>
    public string CategoryGlyph => Event?.GetCategoryIcon() ?? "\uE8FB";

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
        // Add hover effect
        EventBorder.BorderThickness = new Thickness(2);
        EventBorder.BorderBrush = new SolidColorBrush(Colors.White);

        // Add scale animation
        var scaleAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 1.05,
            Duration = TimeSpan.FromMilliseconds(100)
        };

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(scaleAnimation);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(scaleAnimation, RootGrid);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(scaleAnimation, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");

        RootGrid.RenderTransform = new ScaleTransform { CenterX = Width / 2, CenterY = Height / 2 };
        storyboard.Begin();
    }

    private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Remove hover effect
        EventBorder.BorderThickness = new Thickness(0);

        // Reset scale
        var scaleAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(100)
        };

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(scaleAnimation);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(scaleAnimation, RootGrid);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(scaleAnimation, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");

        storyboard.Begin();
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

        // Default gray
        return Colors.Gray;
    }

    private void OnPropertyChanged(string propertyName)
    {
        // Trigger binding update
        Bindings.Update();
    }
}
