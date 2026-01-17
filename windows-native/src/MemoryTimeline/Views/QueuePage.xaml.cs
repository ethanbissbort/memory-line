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
}
