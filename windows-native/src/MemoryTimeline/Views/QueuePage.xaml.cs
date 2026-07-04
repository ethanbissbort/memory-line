using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline.Views;

public sealed partial class QueuePage : Page
{
    public QueueViewModel ViewModel { get; }

    public QueuePage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<QueueViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // QueueViewModel is a transient VM that subscribes to singleton audio-service
        // events and runs a recording timer; dispose it on navigation away so the
        // subscriptions/timer are released and the VM is not leaked for the app lifetime.
        ViewModel.Dispose();
    }
}
