using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using System.Collections.ObjectModel;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for the advanced search page with faceted filtering and autocomplete.
/// </summary>
public partial class SearchViewModel : ObservableObject
{
    private readonly IAdvancedSearchService _searchService;
    private readonly IEventService _eventService;
    private readonly ILogger<SearchViewModel> _logger;

    #region Observable Properties

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    partial void OnSearchTermChanged(string value)
    {
        _ = LoadAutocompleteSuggestionsAsync();
    }

    [ObservableProperty]
    private ObservableCollection<Event> _searchResults = new();

    [ObservableProperty]
    private ObservableCollection<AutocompleteSuggestion> _autocompleteSuggestions = new();

    [ObservableProperty]
    private bool _showAutocompleteSuggestions;

    [ObservableProperty]
    private SearchFacets _facets = new();

    [ObservableProperty]
    private ObservableCollection<SavedSearch> _savedSearches = new();

    [ObservableProperty]
    private ObservableCollection<SavedSearch> _recentSearches = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _totalResults;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private int _pageSize = 25;

    [ObservableProperty]
    private string _sortBy = SearchSortOptions.DateDesc;

    [ObservableProperty]
    private TimeSpan _searchDuration;

    [ObservableProperty]
    private Event? _selectedEvent;

    // Filter selections
    [ObservableProperty]
    private ObservableCollection<string> _selectedCategories = new();

    [ObservableProperty]
    private ObservableCollection<string> _selectedTagIds = new();

    [ObservableProperty]
    private ObservableCollection<string> _selectedPersonIds = new();

    [ObservableProperty]
    private ObservableCollection<string> _selectedLocationIds = new();

    [ObservableProperty]
    private ObservableCollection<string> _selectedEraIds = new();

    [ObservableProperty]
    private DateTimeOffset? _startDate;

    [ObservableProperty]
    private DateTimeOffset? _endDate;

    [ObservableProperty]
    private bool? _hasAudio;

    [ObservableProperty]
    private bool? _hasTranscript;

    [ObservableProperty]
    private double? _minConfidence;

    // Save search dialog
    [ObservableProperty]
    private bool _showSaveSearchDialog;

    [ObservableProperty]
    private string _saveSearchName = string.Empty;

    [ObservableProperty]
    private bool _saveSearchAsFavorite;

    #endregion

    #region Computed Properties

    public bool HasFilters => !string.IsNullOrWhiteSpace(SearchTerm) ||
        SelectedCategories.Any() ||
        SelectedTagIds.Any() ||
        SelectedPersonIds.Any() ||
        SelectedLocationIds.Any() ||
        SelectedEraIds.Any() ||
        StartDate.HasValue ||
        EndDate.HasValue ||
        HasAudio.HasValue ||
        HasTranscript.HasValue;

    public bool CanGoToPreviousPage => CurrentPage > 1;
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    public string ResultsRangeText
    {
        get
        {
            if (TotalResults == 0) return "No results";
            var start = (CurrentPage - 1) * PageSize + 1;
            var end = Math.Min(CurrentPage * PageSize, TotalResults);
            return $"Showing {start}-{end} of {TotalResults}";
        }
    }

    public static int[] AvailablePageSizes => MemoryTimeline.Core.Models.PageSizeOptions.Options;
    public static string[] SortOptions => SearchSortOptions.AllOptions;

    #endregion

    public SearchViewModel(
        IAdvancedSearchService searchService,
        IEventService eventService,
        ILogger<SearchViewModel> logger)
    {
        _searchService = searchService;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the search view.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading...";

            // Load facets for empty filter
            Facets = await _searchService.GetFacetsAsync();

            // Load saved searches
            await LoadSavedSearchesAsync();

            StatusMessage = "Ready to search";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing search view");
            StatusMessage = "Error loading search";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Execute search with current filters.
    /// </summary>
    [RelayCommand]
    public async Task SearchAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            ShowAutocompleteSuggestions = false;
            StatusMessage = "Searching...";

            var filter = BuildSearchFilter();
            var results = await _searchService.SearchAsync(filter);

            SearchResults.Clear();
            foreach (var evt in results.Events)
            {
                SearchResults.Add(evt);
            }

            TotalResults = results.TotalCount;
            TotalPages = results.TotalPages;
            SearchDuration = results.SearchDuration;
            Facets = results.Facets;

            StatusMessage = TotalResults > 0
                ? $"Found {TotalResults} results in {SearchDuration.TotalMilliseconds:F0}ms"
                : "No results found";

            OnPropertyChanged(nameof(ResultsRangeText));
            OnPropertyChanged(nameof(CanGoToPreviousPage));
            OnPropertyChanged(nameof(CanGoToNextPage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing search");
            StatusMessage = "Search error";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Clear all search filters.
    /// </summary>
    [RelayCommand]
    public async Task ClearFiltersAsync()
    {
        SearchTerm = string.Empty;
        SelectedCategories.Clear();
        SelectedTagIds.Clear();
        SelectedPersonIds.Clear();
        SelectedLocationIds.Clear();
        SelectedEraIds.Clear();
        StartDate = null;
        EndDate = null;
        HasAudio = null;
        HasTranscript = null;
        MinConfidence = null;
        CurrentPage = 1;

        OnPropertyChanged(nameof(HasFilters));

        await SearchAsync();
    }

    /// <summary>
    /// Go to previous page.
    /// </summary>
    [RelayCommand]
    public async Task PreviousPageAsync()
    {
        if (CanGoToPreviousPage)
        {
            CurrentPage--;
            await SearchAsync();
        }
    }

    /// <summary>
    /// Go to next page.
    /// </summary>
    [RelayCommand]
    public async Task NextPageAsync()
    {
        if (CanGoToNextPage)
        {
            CurrentPage++;
            await SearchAsync();
        }
    }

    /// <summary>
    /// Go to specific page.
    /// </summary>
    [RelayCommand]
    public async Task GoToPageAsync(int page)
    {
        if (page >= 1 && page <= TotalPages)
        {
            CurrentPage = page;
            await SearchAsync();
        }
    }

    /// <summary>
    /// Change page size.
    /// </summary>
    [RelayCommand]
    public async Task ChangePageSizeAsync(int newSize)
    {
        PageSize = newSize;
        CurrentPage = 1;
        await SearchAsync();
    }

    /// <summary>
    /// Change sort order.
    /// </summary>
    [RelayCommand]
    public async Task ChangeSortAsync(string newSort)
    {
        SortBy = newSort;
        CurrentPage = 1;
        await SearchAsync();
    }

    /// <summary>
    /// Toggle category filter.
    /// </summary>
    [RelayCommand]
    public async Task ToggleCategoryAsync(string category)
    {
        if (SelectedCategories.Contains(category))
            SelectedCategories.Remove(category);
        else
            SelectedCategories.Add(category);

        CurrentPage = 1;
        OnPropertyChanged(nameof(HasFilters));
        await SearchAsync();
    }

    /// <summary>
    /// Toggle tag filter.
    /// </summary>
    [RelayCommand]
    public async Task ToggleTagAsync(string tagId)
    {
        if (SelectedTagIds.Contains(tagId))
            SelectedTagIds.Remove(tagId);
        else
            SelectedTagIds.Add(tagId);

        CurrentPage = 1;
        OnPropertyChanged(nameof(HasFilters));
        await SearchAsync();
    }

    /// <summary>
    /// Toggle person filter.
    /// </summary>
    [RelayCommand]
    public async Task TogglePersonAsync(string personId)
    {
        if (SelectedPersonIds.Contains(personId))
            SelectedPersonIds.Remove(personId);
        else
            SelectedPersonIds.Add(personId);

        CurrentPage = 1;
        OnPropertyChanged(nameof(HasFilters));
        await SearchAsync();
    }

    /// <summary>
    /// Toggle location filter.
    /// </summary>
    [RelayCommand]
    public async Task ToggleLocationAsync(string locationId)
    {
        if (SelectedLocationIds.Contains(locationId))
            SelectedLocationIds.Remove(locationId);
        else
            SelectedLocationIds.Add(locationId);

        CurrentPage = 1;
        OnPropertyChanged(nameof(HasFilters));
        await SearchAsync();
    }

    /// <summary>
    /// Toggle era filter.
    /// </summary>
    [RelayCommand]
    public async Task ToggleEraAsync(string eraId)
    {
        if (SelectedEraIds.Contains(eraId))
            SelectedEraIds.Remove(eraId);
        else
            SelectedEraIds.Add(eraId);

        CurrentPage = 1;
        OnPropertyChanged(nameof(HasFilters));
        await SearchAsync();
    }

    /// <summary>
    /// Apply date range filter.
    /// </summary>
    [RelayCommand]
    public async Task ApplyDateRangeAsync()
    {
        CurrentPage = 1;
        OnPropertyChanged(nameof(HasFilters));
        await SearchAsync();
    }

    /// <summary>
    /// Apply autocomplete suggestion.
    /// </summary>
    [RelayCommand]
    public async Task ApplySuggestionAsync(AutocompleteSuggestion suggestion)
    {
        ShowAutocompleteSuggestions = false;

        switch (suggestion.Type)
        {
            case "event":
                SearchTerm = suggestion.Text;
                break;
            case "tag":
                if (!string.IsNullOrEmpty(suggestion.Id) && !SelectedTagIds.Contains(suggestion.Id))
                    SelectedTagIds.Add(suggestion.Id);
                break;
            case "person":
                if (!string.IsNullOrEmpty(suggestion.Id) && !SelectedPersonIds.Contains(suggestion.Id))
                    SelectedPersonIds.Add(suggestion.Id);
                break;
            case "location":
                if (!string.IsNullOrEmpty(suggestion.Id) && !SelectedLocationIds.Contains(suggestion.Id))
                    SelectedLocationIds.Add(suggestion.Id);
                break;
            case "era":
                if (!string.IsNullOrEmpty(suggestion.Id) && !SelectedEraIds.Contains(suggestion.Id))
                    SelectedEraIds.Add(suggestion.Id);
                break;
        }

        OnPropertyChanged(nameof(HasFilters));
        await SearchAsync();
    }

    /// <summary>
    /// Show save search dialog.
    /// </summary>
    [RelayCommand]
    public void ShowSaveSearch()
    {
        SaveSearchName = string.Empty;
        SaveSearchAsFavorite = false;
        ShowSaveSearchDialog = true;
    }

    /// <summary>
    /// Save current search.
    /// </summary>
    [RelayCommand]
    public async Task SaveCurrentSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SaveSearchName))
            return;

        try
        {
            var filter = BuildSearchFilter();
            await _searchService.SaveSearchAsync(SaveSearchName, filter, SaveSearchAsFavorite);

            ShowSaveSearchDialog = false;
            await LoadSavedSearchesAsync();

            StatusMessage = $"Search saved: {SaveSearchName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving search");
            StatusMessage = "Error saving search";
        }
    }

    /// <summary>
    /// Cancel save search.
    /// </summary>
    [RelayCommand]
    public void CancelSaveSearch()
    {
        ShowSaveSearchDialog = false;
    }

    /// <summary>
    /// Load a saved search.
    /// </summary>
    [RelayCommand]
    public async Task LoadSavedSearchAsync(SavedSearch savedSearch)
    {
        try
        {
            var filter = _searchService.ConvertToFilter(savedSearch);
            ApplyFilter(filter);

            await _searchService.MarkSearchAsUsedAsync(savedSearch.SavedSearchId);
            await SearchAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading saved search");
            StatusMessage = "Error loading saved search";
        }
    }

    /// <summary>
    /// Delete a saved search.
    /// </summary>
    [RelayCommand]
    public async Task DeleteSavedSearchAsync(SavedSearch savedSearch)
    {
        try
        {
            await _searchService.DeleteSavedSearchAsync(savedSearch.SavedSearchId);
            await LoadSavedSearchesAsync();
            StatusMessage = "Saved search deleted";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting saved search");
            StatusMessage = "Error deleting saved search";
        }
    }

    /// <summary>
    /// Toggle saved search favorite status.
    /// </summary>
    [RelayCommand]
    public async Task ToggleFavoriteAsync(SavedSearch savedSearch)
    {
        try
        {
            savedSearch.IsFavorite = !savedSearch.IsFavorite;
            await _searchService.UpdateSavedSearchAsync(savedSearch);
            await LoadSavedSearchesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating saved search");
        }
    }

    /// <summary>
    /// Select an event.
    /// </summary>
    [RelayCommand]
    public void SelectEvent(Event? evt)
    {
        SelectedEvent = evt;
    }

    /// <summary>
    /// Update an event and refresh the search results.
    /// </summary>
    public async Task UpdateEventAsync(Event evt)
    {
        try
        {
            IsLoading = true;
            await _eventService.UpdateEventAsync(evt);
            StatusMessage = "Event updated successfully";

            // Refresh search results
            await SearchAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event {EventId}", evt.EventId);
            StatusMessage = "Error updating event";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Delete an event and refresh the search results.
    /// </summary>
    public async Task DeleteEventAsync(string eventId)
    {
        try
        {
            IsLoading = true;
            await _eventService.DeleteEventAsync(eventId);
            StatusMessage = "Event deleted successfully";

            // Refresh search results
            await SearchAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event {EventId}", eventId);
            StatusMessage = "Error deleting event";
        }
        finally
        {
            IsLoading = false;
        }
    }

    #region Private Methods

    private async Task LoadAutocompleteSuggestionsAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchTerm) || SearchTerm.Length < 2)
        {
            AutocompleteSuggestions.Clear();
            ShowAutocompleteSuggestions = false;
            return;
        }

        try
        {
            var suggestions = await _searchService.GetAutocompleteSuggestionsAsync(SearchTerm);
            AutocompleteSuggestions.Clear();
            foreach (var suggestion in suggestions)
            {
                AutocompleteSuggestions.Add(suggestion);
            }
            ShowAutocompleteSuggestions = suggestions.Any();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading autocomplete suggestions");
        }
    }

    private async Task LoadSavedSearchesAsync()
    {
        try
        {
            var all = await _searchService.GetAllSavedSearchesAsync();
            SavedSearches.Clear();
            foreach (var search in all)
            {
                SavedSearches.Add(search);
            }

            var recent = await _searchService.GetRecentSavedSearchesAsync(5);
            RecentSearches.Clear();
            foreach (var search in recent)
            {
                RecentSearches.Add(search);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading saved searches");
        }
    }

    private SearchFilter BuildSearchFilter()
    {
        return new SearchFilter
        {
            SearchTerm = SearchTerm,
            Categories = SelectedCategories.ToList(),
            TagIds = SelectedTagIds.ToList(),
            PersonIds = SelectedPersonIds.ToList(),
            LocationIds = SelectedLocationIds.ToList(),
            EraIds = SelectedEraIds.ToList(),
            StartDate = StartDate?.DateTime,
            EndDate = EndDate?.DateTime,
            HasAudio = HasAudio,
            HasTranscript = HasTranscript,
            MinConfidence = MinConfidence,
            SortBy = SortBy,
            PageNumber = CurrentPage,
            PageSize = PageSize
        };
    }

    private void ApplyFilter(SearchFilter filter)
    {
        SearchTerm = filter.SearchTerm ?? string.Empty;

        SelectedCategories.Clear();
        foreach (var cat in filter.Categories)
            SelectedCategories.Add(cat);

        SelectedTagIds.Clear();
        foreach (var id in filter.TagIds)
            SelectedTagIds.Add(id);

        SelectedPersonIds.Clear();
        foreach (var id in filter.PersonIds)
            SelectedPersonIds.Add(id);

        SelectedLocationIds.Clear();
        foreach (var id in filter.LocationIds)
            SelectedLocationIds.Add(id);

        SelectedEraIds.Clear();
        foreach (var id in filter.EraIds)
            SelectedEraIds.Add(id);

        StartDate = filter.StartDate.HasValue ? new DateTimeOffset(filter.StartDate.Value) : null;
        EndDate = filter.EndDate.HasValue ? new DateTimeOffset(filter.EndDate.Value) : null;
        HasAudio = filter.HasAudio;
        HasTranscript = filter.HasTranscript;
        MinConfidence = filter.MinConfidence;
        SortBy = filter.SortBy;
        PageSize = filter.PageSize;
        CurrentPage = 1;

        OnPropertyChanged(nameof(HasFilters));
    }

    #endregion
}
