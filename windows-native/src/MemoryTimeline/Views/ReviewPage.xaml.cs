using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline.Views;

public sealed partial class ReviewPage : Page
{
    public ReviewViewModel ViewModel { get; }

    public ReviewPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<ReviewViewModel>();

        // The page owns confirmation dialogs (it has a XamlRoot); the ViewModel
        // requests confirmation before destructive reject/delete operations.
        ViewModel.ConfirmDestructiveActionAsync = ShowConfirmDialogAsync;

        // The page-local converter resolves each pending-event card's cached
        // person-suggestions state from the ViewModel, so the suggestions
        // expander always binds to a non-null state object.
        if (Resources["ReviewSuggestionsStateConverter"] is ReviewSuggestionsStateConverter converter)
        {
            converter.ViewModel = ViewModel;
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Shows a confirmation ContentDialog for destructive actions.
    /// Returns true only when the user explicitly confirms.
    /// </summary>
    private async Task<bool> ShowConfirmDialogAsync(string title, string message, string confirmLabel)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmLabel,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Lazily loads a card's person suggestions the first time its
    /// "People suggestions" expander is opened (results are cached per card).
    /// </summary>
    private void OnPeopleSuggestionsExpanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        var state = (sender.Content as ContentControl)?.Content as PersonSuggestionsState;

        // Fallback in case the content binding has not produced a state yet.
        if (state == null && sender.DataContext is PendingEventDto dto)
        {
            state = ViewModel.GetOrCreateSuggestionsState(dto.PendingEventId);
            if (sender.Content is ContentControl host)
            {
                host.Content = state;
            }
        }

        // Fire-and-forget is safe: EnsureSuggestionsLoadedAsync never throws.
        _ = ViewModel.EnsureSuggestionsLoadedAsync(state);
    }

    /// <summary>
    /// Applies a single person suggestion from its Apply button.
    /// </summary>
    private async void OnApplySuggestionClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PersonSuggestionDto suggestion)
        {
            // ApplySuggestionAsync never throws (errors surface via the card's InfoBar).
            await ViewModel.ApplySuggestionAsync(suggestion);
        }
    }

    /// <summary>
    /// Resumes a draft in its owning editor (Timeline / Eras / People page).
    /// </summary>
    private void OnResumeDraftClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DraftDto draft)
        {
            ViewModel.ResumeDraft(draft);
        }
    }

    /// <summary>
    /// Deletes a draft after confirmation (the ViewModel routes the
    /// confirmation back through this page's ContentDialog).
    /// </summary>
    private async void OnDeleteDraftClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DraftDto draft)
        {
            // DeleteDraftAsync never throws (errors surface via the drafts InfoBar).
            await ViewModel.DeleteDraftAsync(draft);
        }
    }
}

/// <summary>
/// Page-local converter mapping a pending event id to its cached
/// <see cref="PersonSuggestionsState"/> from the <see cref="ReviewViewModel"/>,
/// so each card's suggestions expander binds to a non-null state object.
/// </summary>
public sealed class ReviewSuggestionsStateConverter : IValueConverter
{
    /// <summary>
    /// The ViewModel owning the per-card suggestion state cache; assigned by
    /// <see cref="ReviewPage"/> after InitializeComponent.
    /// </summary>
    public ReviewViewModel? ViewModel { get; set; }

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string pendingEventId && ViewModel != null)
        {
            return ViewModel.GetOrCreateSuggestionsState(pendingEventId);
        }

        // Inert fallback (empty id never loads); keeps the template's data root non-null.
        return new PersonSuggestionsState(string.Empty);
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
