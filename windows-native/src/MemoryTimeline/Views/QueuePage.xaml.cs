using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline.Views;

public sealed partial class QueuePage : Page
{
    /// <summary>
    /// Soft warning threshold for pasted text (characters). Longer inputs are
    /// still allowed - they are just slow/expensive to extract.
    /// </summary>
    private const int LongTextWarningThreshold = 20000;

    private bool _isPasteDialogOpen;

    public QueueViewModel ViewModel { get; }

    public QueuePage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<QueueViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Ctrl+Shift+V (MainWindow accelerator) navigates here with the "paste"
        // parameter so the paste-capture dialog opens on arrival.
        var openPasteDialog = e.Parameter is string parameter &&
            string.Equals(parameter, "paste", StringComparison.OrdinalIgnoreCase);

        await ViewModel.InitializeAsync();

        if (openPasteDialog)
        {
            OpenPasteDialogWhenReady();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // QueueViewModel is a transient VM that subscribes to singleton audio-service
        // events and runs a recording timer; dispose it on navigation away so the
        // subscriptions/timer are released and the VM is not leaked for the app lifetime.
        ViewModel.Dispose();
    }

    private void PasteTextButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ShowPasteDialogAsync();
    }

    /// <summary>
    /// Opens the paste dialog once the page is loaded (a ContentDialog needs a
    /// live XamlRoot), deferring the show through the DispatcherQueue so the
    /// layout pass has completed - mirrors TimelinePage's draft-dialog pattern.
    /// </summary>
    private void OpenPasteDialogWhenReady()
    {
        void ShowDeferred()
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                await ShowPasteDialogAsync();
            });
        }

        if (IsLoaded)
        {
            ShowDeferred();
            return;
        }

        RoutedEventHandler? loadedHandler = null;
        loadedHandler = (_, _) =>
        {
            Loaded -= loadedHandler;
            ShowDeferred();
        };
        Loaded += loadedHandler;
    }

    private async Task ShowPasteDialogAsync()
    {
        // ContentDialog throws if shown while another instance is open.
        if (_isPasteDialogOpen)
        {
            return;
        }

        try
        {
            _isPasteDialogOpen = true;

            // Fresh dialog state per open
            PasteTextBox.Text = string.Empty;
            PasteLabelBox.Text = string.Empty;
            PasteDialogErrorBar.IsOpen = false;
            UpdatePasteCharCount();

            PasteTextDialog.XamlRoot = XamlRoot;
            await PasteTextDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // Surface the failure on the page status bar (the VM logs enqueue
            // errors itself; this covers dialog-infrastructure failures).
            ViewModel.StatusText = $"Could not open the paste dialog: {ex.Message}";
        }
        finally
        {
            _isPasteDialogOpen = false;
        }
    }

    private void PasteTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePasteCharCount();
    }

    private void UpdatePasteCharCount()
    {
        var length = PasteTextBox.Text?.Length ?? 0;
        PasteCharCountText.Text = length == 1 ? "1 character" : $"{length:N0} characters";
        PasteLengthWarningBar.IsOpen = length > LongTextWarningThreshold;
    }

    private async void PasteTextDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var errorMessage = await ViewModel.EnqueuePastedTextAsync(PasteTextBox.Text, PasteLabelBox.Text);
            if (errorMessage != null)
            {
                // Keep the dialog open (and the pasted text intact) and show
                // the reason in the in-dialog InfoBar. The VM already logged it.
                args.Cancel = true;
                PasteDialogErrorBar.Message = errorMessage;
                PasteDialogErrorBar.IsOpen = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
