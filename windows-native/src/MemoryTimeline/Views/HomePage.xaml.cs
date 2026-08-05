using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline.Views;

/// <summary>
/// Home page: On This Day memories, a random (neglected-biased) memory,
/// recent additions, the pending-review count, the weekly digest, and the
/// first-run empty state.
/// </summary>
public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<HomeViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Clears the error when the InfoBar is dismissed so the OneWay IsOpen
    /// binding does not immediately reopen it.
    /// </summary>
    private void ErrorBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.ErrorMessage = null;
    }
}
