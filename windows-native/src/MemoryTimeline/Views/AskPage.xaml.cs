using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline.Views;

/// <summary>
/// "Ask your timeline" page: conversational retrieval over the memory archive.
/// </summary>
public sealed partial class AskPage : Page
{
    public AskViewModel ViewModel { get; }

    public AskPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<AskViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // AskViewModel is transient; dispose it on navigation away so any
        // in-flight ask is cancelled instead of streaming into a dead page.
        ViewModel.Dispose();
    }

    /// <summary>
    /// Enter submits the question (the TextBox binding updates on every
    /// keystroke, so the ViewModel already holds the current text).
    /// </summary>
    private void QuestionBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (ViewModel.AskCommand.CanExecute(null))
            {
                ViewModel.AskCommand.Execute(null);
            }

            e.Handled = true;
        }
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
