using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MemoryTimeline.Data.Models;
using MemoryTimeline.ViewModels;
using MemoryTimeline.Services;

namespace MemoryTimeline.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }
    private readonly INavigationService _navigationService;
    private Event? _contextMenuEvent;

    public SearchPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<SearchViewModel>();
        _navigationService = App.Current.Services.GetRequiredService<INavigationService>();
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

    #region Search Result Event Handlers

    private async void SearchResult_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Event clickedEvent)
        {
            await ShowEventDetailDialogAsync(clickedEvent);
        }
    }

    private async void SearchResult_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedEvent != null)
        {
            NavigateToEventOnTimeline(ViewModel.SelectedEvent);
        }
    }

    private void ViewOnTimeline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem &&
            menuItem.DataContext is Event eventData)
        {
            NavigateToEventOnTimeline(eventData);
        }
    }

    private async void EditEvent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem &&
            menuItem.DataContext is Event eventData)
        {
            await ShowEditEventDialogAsync(eventData);
        }
    }

    private async void DeleteEvent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem &&
            menuItem.DataContext is Event eventData)
        {
            _contextMenuEvent = eventData;
            DeleteConfirmDialog.XamlRoot = XamlRoot;
            await DeleteConfirmDialog.ShowAsync();
        }
    }

    private async Task ShowEventDetailDialogAsync(Event eventData)
    {
        // Populate the dialog
        EventDetailTitle.Text = eventData.Title;
        EventDetailDate.Text = eventData.StartDate.ToString("MMMM d, yyyy");
        if (eventData.EndDate.HasValue)
        {
            EventDetailDate.Text += $" - {eventData.EndDate.Value:MMMM d, yyyy}";
        }
        EventDetailDescription.Text = eventData.Description ?? "No description";
        EventDetailCategory.Text = eventData.Category ?? "Uncategorized";

        if (!string.IsNullOrEmpty(eventData.Location))
        {
            EventDetailLocation.Text = eventData.Location;
            EventDetailLocationPanel.Visibility = Visibility.Visible;
        }
        else
        {
            EventDetailLocationPanel.Visibility = Visibility.Collapsed;
        }

        _contextMenuEvent = eventData;
        EventDetailDialog.XamlRoot = XamlRoot;
        await EventDetailDialog.ShowAsync();
    }

    private void EventDetailDialog_EditClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // Don't close yet, we'll show another dialog
        _ = ShowEditEventDialogAsync(_contextMenuEvent!);
    }

    private void EventDetailDialog_ViewOnTimelineClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_contextMenuEvent != null)
        {
            NavigateToEventOnTimeline(_contextMenuEvent);
        }
    }

    private async Task ShowEditEventDialogAsync(Event eventData)
    {
        // Populate the edit dialog
        EditEventTitleBox.Text = eventData.Title;
        EditEventDatePicker.Date = new DateTimeOffset(eventData.StartDate);
        EditEventDescriptionBox.Text = eventData.Description ?? "";
        EditEventLocationBox.Text = eventData.Location ?? "";

        // Select the category
        EditEventCategoryCombo.SelectedIndex = -1;
        foreach (ComboBoxItem item in EditEventCategoryCombo.Items)
        {
            if (item.Tag?.ToString() == eventData.Category)
            {
                EditEventCategoryCombo.SelectedItem = item;
                break;
            }
        }

        _contextMenuEvent = eventData;
        EditEventDialog.XamlRoot = XamlRoot;
        await EditEventDialog.ShowAsync();
    }

    private async void EditEventDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_contextMenuEvent == null)
        {
            args.Cancel = true;
            return;
        }

        var title = EditEventTitleBox.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            args.Cancel = true;
            return;
        }

        if (!EditEventDatePicker.Date.HasValue)
        {
            args.Cancel = true;
            return;
        }

        // Update the event
        _contextMenuEvent.Title = title;
        _contextMenuEvent.StartDate = EditEventDatePicker.Date.Value.DateTime;
        _contextMenuEvent.Description = string.IsNullOrWhiteSpace(EditEventDescriptionBox.Text)
            ? null : EditEventDescriptionBox.Text;
        _contextMenuEvent.Location = string.IsNullOrWhiteSpace(EditEventLocationBox.Text)
            ? null : EditEventLocationBox.Text;

        if (EditEventCategoryCombo.SelectedItem is ComboBoxItem categoryItem)
        {
            _contextMenuEvent.Category = categoryItem.Tag?.ToString();
        }

        await ViewModel.UpdateEventAsync(_contextMenuEvent);
    }

    private async void DeleteConfirmDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_contextMenuEvent != null)
        {
            await ViewModel.DeleteEventAsync(_contextMenuEvent.EventId);
        }
    }

    private void NavigateToEventOnTimeline(Event eventData)
    {
        // Navigate to Timeline page with the event date
        // The navigation service should pass the date as a parameter
        _navigationService.NavigateTo("Timeline", eventData.StartDate);
    }

    #endregion
}
