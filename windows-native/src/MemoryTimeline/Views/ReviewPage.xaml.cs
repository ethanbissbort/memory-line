using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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
        // requests confirmation before destructive reject operations.
        ViewModel.ConfirmDestructiveActionAsync = ShowConfirmDialogAsync;
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
}
