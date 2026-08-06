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

    /// <summary>
    /// Pin opacity: approximate dates (precision coarser than Month) render
    /// slightly faded as a subtle uncertainty cue.
    /// </summary>
    public double PinOpacity => Event?.IsApproximate == true ? 0.55 : 1.0;

    // NOTE (F2 spec, deliberate deferral): the spec also asks for a single
    // attached image to be used as the bubble FILL at Day/Week zoom. That is
    // intentionally not implemented: the pin is ~30x40 device pixels, so a
    // photo fill at that size is illegible, and decoding/rendering per-bubble
    // thumbnails adds per-frame timeline cost (the audit's Appendix E risk 1,
    // "thumbnails on bubbles increase per-frame timeline cost"). The
    // photo-count badge below carries the "has photos" signal instead.

    /// <summary>Photo-count badge text ("3", capped at "9+").</summary>
    public string MediaBadgeText
    {
        get
        {
            var count = Event?.MediaCount ?? 0;
            return count > 9 ? "9+" : count.ToString();
        }
    }

    /// <summary>The badge is shown only when the event has media attachments.</summary>
    public Visibility MediaBadgeVisibility =>
        Event?.HasMedia == true ? Visibility.Visible : Visibility.Collapsed;

    public EventBubble()
    {
        InitializeComponent();
    }

    private static void OnEventChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EventBubble bubble)
        {
            // The photo-count badge can change while the pin is on screen
            // (photos attached from the details panel), so track the DTO's
            // change notifications for the lifetime of the assignment.
            if (e.OldValue is TimelineEventDto oldEvent)
            {
                oldEvent.PropertyChanged -= bubble.OnEventDtoPropertyChanged;
            }

            if (e.NewValue is TimelineEventDto newEvent)
            {
                newEvent.PropertyChanged += bubble.OnEventDtoPropertyChanged;
            }

            bubble.OnPropertyChanged(nameof(CategoryBrush));
            bubble.OnPropertyChanged(nameof(CategoryGlyph));
            bubble.OnPropertyChanged(nameof(PinOpacity));
            bubble.OnPropertyChanged(nameof(MediaBadgeVisibility));
            bubble.OnPropertyChanged(nameof(MediaBadgeText));
            bubble.UpdateToolTip();
        }
    }

    private void OnEventDtoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineEventDto.MediaCount))
        {
            // MediaCount is only mutated on the UI thread (the ViewModel
            // marshals), so refreshing the compiled bindings here is safe.
            Bindings.Update();
        }
    }

    /// <summary>
    /// Sets a hover tooltip with the event's title, precision-honest date
    /// ("Summer 1998" rather than a fabricated exact day), and linked people
    /// (the people line appears only when person names have been loaded).
    /// </summary>
    private void UpdateToolTip()
    {
        if (Event == null)
        {
            ToolTipService.SetToolTip(this, null);
            return;
        }

        var text = $"{Event.Title}\n{Event.DisplayDate}";
        if (Event.HasPeople)
        {
            text += $"\nWith: {Event.PeopleDisplay}";
        }

        ToolTipService.SetToolTip(this, text);
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
        PinPath.Stroke = Application.Current.Resources.TryGetValue(
                "SystemControlForegroundBaseMediumBrush", out var strokeResource)
            && strokeResource is Brush strokeBrush
                ? strokeBrush
                : new SolidColorBrush(Colors.Gray);

        RootGrid.RenderTransform = new CompositeTransform
        {
            ScaleX = 1.0,
            ScaleY = 1.0,
            CenterX = 15,
            CenterY = 40
        };
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
