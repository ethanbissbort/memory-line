using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<SettingsViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }
}
