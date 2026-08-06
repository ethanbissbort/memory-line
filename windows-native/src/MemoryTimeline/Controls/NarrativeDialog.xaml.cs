using MemoryTimeline.Core.Services;
using MemoryTimeline.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace MemoryTimeline.Controls;

/// <summary>
/// Shared narrative-generation dialog: voice/length options, generate with
/// progress, section preview, and Markdown/HTML export. Reused by ErasPage
/// (era scope) and TimelineControl (viewport date-range scope) — construct
/// with the scope request, set <c>XamlRoot</c>, then <c>ShowAsync</c>.
/// Closing the dialog cancels any in-flight generation.
/// </summary>
public sealed partial class NarrativeDialog : ContentDialog
{
    public NarrativeDialogViewModel ViewModel { get; }

    public NarrativeDialog(NarrativeRequest request, string scopeDisplayName)
    {
        InitializeComponent();

        // Transient ViewModel: fresh generation/cancellation state per dialog.
        ViewModel = App.Current.Services.GetRequiredService<NarrativeDialogViewModel>();
        ViewModel.Initialize(request, scopeDisplayName);

        Closed += NarrativeDialog_Closed;
    }

    /// <summary>
    /// Cancel-on-close: an in-flight generation must not keep running (or
    /// keep spending tokens) after the user dismisses the dialog.
    /// </summary>
    private void NarrativeDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.CancelPendingGeneration();
    }

    /// <summary>
    /// Clears the ViewModel error when the bar is dismissed so the OneWay
    /// IsOpen binding doesn't immediately reopen it (same pattern as the
    /// timeline error bar).
    /// </summary>
    private void NarrativeErrorBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.ErrorMessage = null;
    }
}
