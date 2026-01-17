using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MemoryTimeline.Data.Models;
using MemoryTimeline.ViewModels;

namespace MemoryTimeline.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }

    public SearchPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await ViewModel.SearchAsync();
    }

    private async void Category_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.Tag is string category)
        {
            await ViewModel.ToggleCategoryAsync(category);
        }
    }

    private async void Category_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.Tag is string category)
        {
            await ViewModel.ToggleCategoryAsync(category);
        }
    }

    private async void HasAudio_Checked(object sender, RoutedEventArgs e)
    {
        ViewModel.HasAudio = true;
        await ViewModel.SearchAsync();
    }

    private async void HasAudio_Unchecked(object sender, RoutedEventArgs e)
    {
        ViewModel.HasAudio = null;
        await ViewModel.SearchAsync();
    }

    private async void HasTranscript_Checked(object sender, RoutedEventArgs e)
    {
        ViewModel.HasTranscript = true;
        await ViewModel.SearchAsync();
    }

    private async void HasTranscript_Unchecked(object sender, RoutedEventArgs e)
    {
        ViewModel.HasTranscript = null;
        await ViewModel.SearchAsync();
    }

    private async void SortBy_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string sortBy)
        {
            await ViewModel.ChangeSortAsync(sortBy);
        }
    }

    private async void PageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string sizeStr)
        {
            if (int.TryParse(sizeStr, out var size))
            {
                await ViewModel.ChangePageSizeAsync(size);
            }
        }
    }

    private async void PageNumber_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!double.IsNaN(args.NewValue))
        {
            await ViewModel.GoToPageAsync((int)args.NewValue);
        }
    }

    private async void SavedSearch_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SavedSearch savedSearch)
        {
            await ViewModel.LoadSavedSearchAsync(savedSearch);
        }
    }

    private async void SaveSearchDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = SaveSearchNameTextBox.Text;
        var isFavorite = SaveSearchFavoriteCheckBox.IsChecked ?? false;

        if (!string.IsNullOrWhiteSpace(name))
        {
            ViewModel.SaveSearchName = name;
            ViewModel.SaveSearchAsFavorite = isFavorite;
            await ViewModel.SaveCurrentSearchAsync();
        }
        else
        {
            args.Cancel = true;
        }
    }
}
