using Microsoft.UI.Xaml.Controls;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline.Views;

/// <summary>
/// Timeline visualization page.
/// </summary>
public sealed partial class TimelinePage : Page
{
    private readonly TimelineViewModel _viewModel;

    public TimelinePage(TimelineViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;

        // Set the ViewModel on the TimelineControl
        TimelineControl.ViewModel = _viewModel;
    }
}
