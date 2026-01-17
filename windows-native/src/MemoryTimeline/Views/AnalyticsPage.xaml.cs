using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MemoryTimeline.Core.Services;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline.Views;

public sealed partial class AnalyticsPage : Page
{
    public AnalyticsViewModel ViewModel { get; }

    public AnalyticsPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<AnalyticsViewModel>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private void MonthlyDensity_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ChangeDensityGranularity(DensityGranularity.Monthly);
    }

    private void YearlyDensity_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ChangeDensityGranularity(DensityGranularity.Yearly);
    }

    private void DayOfWeekHeatmap_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ChangeHeatmapType(HeatmapType.DayOfWeek);
    }

    private void MonthHeatmap_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ChangeHeatmapType(HeatmapType.MonthOfYear);
    }

    private void YearHeatmap_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ChangeHeatmapType(HeatmapType.YearOverTime);
    }
}
