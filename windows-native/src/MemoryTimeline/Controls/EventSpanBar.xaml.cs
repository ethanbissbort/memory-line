using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Models;
using Windows.UI;

namespace MemoryTimeline.Controls;

/// <summary>
/// Renders a duration event as a rounded span bar: category/era color at
/// reduced saturation, inline title when wide enough, end-cap chevrons when
/// the span extends past a viewport edge, and a tooltip with the full precise
/// date range. Follows the EventBubble pattern: an Event dependency property
/// plus explicit Bindings.Update() refreshes on relevant DTO changes.
/// </summary>
public sealed partial class EventSpanBar : UserControl
{
    public static readonly DependencyProperty EventProperty =
        DependencyProperty.Register(
            nameof(Event),
            typeof(TimelineEventDto),
            typeof(EventSpanBar),
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
    /// Bar fill: the category/era color desaturated and slightly translucent,
    /// so spans read as background context under the pins.
    /// </summary>
    public SolidColorBrush BarBrush
    {
        get
        {
            var color = Desaturate(GetEventColor(), 0.45);
            return new SolidColorBrush(Color.FromArgb(0xD9, color.R, color.G, color.B));
        }
    }

    /// <summary>Bar outline: the original (undesaturated) color.</summary>
    public SolidColorBrush BarBorderBrush
    {
        get
        {
            var color = GetEventColor();
            return new SolidColorBrush(Color.FromArgb(0xC0, color.R, color.G, color.B));
        }
    }

    /// <summary>Title/chevron color chosen for contrast against the fill.</summary>
    public SolidColorBrush TitleBrush
    {
        get
        {
            var fill = Desaturate(GetEventColor(), 0.45);
            var luma = 0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B;
            return new SolidColorBrush(luma > 150 ? Color.FromArgb(255, 20, 20, 20) : Colors.White);
        }
    }

    /// <summary>
    /// Approximate dates (precision coarser than Month) render slightly faded,
    /// matching the pin's uncertainty cue.
    /// </summary>
    public double BarOpacity => Event?.IsApproximate == true ? 0.75 : 1.0;

    /// <summary>The event title (empty until an event is assigned).</summary>
    public string TitleTextValue => Event?.Title ?? string.Empty;

    /// <summary>The inline title shows only when the bar is wide enough (&gt; ~80px).</summary>
    public Visibility TitleVisibility =>
        Event?.ShowSpanTitle == true ? Visibility.Visible : Visibility.Collapsed;

    public EventSpanBar()
    {
        InitializeComponent();
    }

    private static void OnEventChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EventSpanBar bar)
        {
            // Width/overhang change during pan/zoom shifts while the bar stays
            // on screen, so track the DTO's change notifications for the
            // lifetime of the assignment (containers are recycled).
            if (e.OldValue is TimelineEventDto oldEvent)
            {
                oldEvent.PropertyChanged -= bar.OnEventDtoPropertyChanged;
            }

            if (e.NewValue is TimelineEventDto newEvent)
            {
                newEvent.PropertyChanged += bar.OnEventDtoPropertyChanged;
            }

            bar.RefreshVisuals();
        }
    }

    private void OnEventDtoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The DTO's display properties are only mutated on the UI thread (the
        // ViewModel recalculates positions there), so updating directly is safe.
        switch (e.PropertyName)
        {
            case nameof(TimelineEventDto.Width):
            case nameof(TimelineEventDto.PixelX):
            case nameof(TimelineEventDto.RenderWidth):
            case nameof(TimelineEventDto.RenderX):
            case nameof(TimelineEventDto.SpanLeftOverhang):
            case nameof(TimelineEventDto.SpanRightOverhang):
                RefreshVisuals();
                break;
        }
    }

    /// <summary>Re-evaluates the compiled bindings and imperative layout bits.</summary>
    private void RefreshVisuals()
    {
        Bindings.Update();
        UpdateOverhangVisuals();
        UpdateToolTip();
    }

    /// <summary>
    /// Positions the end-cap chevrons at the VISIBLE viewport edges. When a
    /// span sticks out past an edge, its element edge is off screen, so the
    /// chevron is inset by the overhang to sit just inside the viewport. The
    /// title is inset the same way so it never hides under the gutterless
    /// off-screen part of the bar.
    /// </summary>
    private void UpdateOverhangVisuals()
    {
        var evt = Event;
        if (evt == null)
        {
            LeftClipChevron.Visibility = Visibility.Collapsed;
            RightClipChevron.Visibility = Visibility.Collapsed;
            return;
        }

        var clipsLeft = evt.ClipsViewportLeft;
        var clipsRight = evt.ClipsViewportRight;

        LeftClipChevron.Visibility = clipsLeft ? Visibility.Visible : Visibility.Collapsed;
        RightClipChevron.Visibility = clipsRight ? Visibility.Visible : Visibility.Collapsed;

        if (clipsLeft)
        {
            LeftClipChevron.Margin = new Thickness(evt.SpanLeftOverhang + 6, 0, 0, 0);
        }

        if (clipsRight)
        {
            RightClipChevron.Margin = new Thickness(0, 0, evt.SpanRightOverhang + 6, 0);
        }

        TitleText.Margin = new Thickness(
            evt.SpanLeftOverhang + (clipsLeft ? 20 : 10),
            0,
            evt.SpanRightOverhang + (clipsRight ? 20 : 10),
            0);
    }

    /// <summary>
    /// Tooltip with the full precise range: both ends formatted through
    /// DateDisplay at the event's precision ("Summer 1998 - Summer 2001",
    /// "14 Mar 2019 - 20 Mar 2019"), plus linked people when loaded.
    /// </summary>
    private void UpdateToolTip()
    {
        var evt = Event;
        if (evt == null)
        {
            ToolTipService.SetToolTip(this, null);
            return;
        }

        var startText = DateDisplay.FormatPrecise(evt.StartDate, evt.DatePrecision);
        var rangeText = startText;
        if (evt.EndDate.HasValue)
        {
            var endText = DateDisplay.FormatPrecise(evt.EndDate.Value, evt.DatePrecision);
            if (!string.Equals(endText, startText, StringComparison.Ordinal))
            {
                rangeText = $"{startText} - {endText}";
            }
        }

        var text = $"{evt.Title}\n{rangeText}";
        if (evt.HasPeople)
        {
            text += $"\nWith: {evt.PeopleDisplay}";
        }

        ToolTipService.SetToolTip(this, text);
    }

    private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        BarBorder.BorderThickness = new Thickness(2);
    }

    private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        BarBorder.BorderThickness = new Thickness(1);
    }

    /// <summary>The event's category/era color (EraColor wins when set).</summary>
    private Color GetEventColor()
    {
        var hex = Event?.GetCategoryColor() ?? "#808080";
        return ConvertHexToColor(hex);
    }

    /// <summary>
    /// Reduces saturation by blending each channel toward the color's
    /// luminance gray. amount 0 = unchanged, 1 = fully gray.
    /// </summary>
    private static Color Desaturate(Color color, double amount)
    {
        var gray = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        byte Blend(byte channel) => (byte)Math.Clamp(channel + (gray - channel) * amount, 0, 255);
        return Color.FromArgb(color.A, Blend(color.R), Blend(color.G), Blend(color.B));
    }

    private static Color ConvertHexToColor(string hex)
    {
        try
        {
            hex = hex.Replace("#", string.Empty);

            if (hex.Length == 6)
            {
                return Color.FromArgb(
                    255,
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
            }

            if (hex.Length == 8)
            {
                return Color.FromArgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16),
                    Convert.ToByte(hex.Substring(6, 2), 16));
            }
        }
        catch
        {
            // Fall through to gray.
        }

        return Color.FromArgb(255, 128, 128, 128);
    }
}
