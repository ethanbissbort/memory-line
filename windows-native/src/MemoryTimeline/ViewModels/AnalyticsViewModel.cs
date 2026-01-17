using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Core.Services;
using System.Collections.ObjectModel;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for the analytics and visualizations page.
/// </summary>
public partial class AnalyticsViewModel : ObservableObject
{
    private readonly IAnalyticsService _analyticsService;
    private readonly ILogger<AnalyticsViewModel> _logger;

    #region Observable Properties

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Overview stats
    [ObservableProperty]
    private int _totalEvents;

    [ObservableProperty]
    private int _totalEras;

    [ObservableProperty]
    private int _totalTags;

    [ObservableProperty]
    private int _totalPeople;

    [ObservableProperty]
    private int _totalLocations;

    [ObservableProperty]
    private int _eventsWithAudio;

    [ObservableProperty]
    private int _eventsWithTranscript;

    [ObservableProperty]
    private DateTime? _earliestDate;

    [ObservableProperty]
    private DateTime? _latestDate;

    [ObservableProperty]
    private string _timelineSpanText = string.Empty;

    // Category distribution
    [ObservableProperty]
    private ObservableCollection<CategoryDistribution> _categoryDistribution = new();

    // Timeline density
    [ObservableProperty]
    private ObservableCollection<TimelineDensityPoint> _monthlyDensity = new();

    [ObservableProperty]
    private ObservableCollection<TimelineDensityPoint> _yearlyDensity = new();

    [ObservableProperty]
    private DensityGranularity _selectedDensityGranularity = DensityGranularity.Monthly;

    // Tag cloud
    [ObservableProperty]
    private ObservableCollection<TagCloudItem> _tagCloud = new();

    // People network
    [ObservableProperty]
    private PeopleNetwork _peopleNetwork = new();

    [ObservableProperty]
    private ObservableCollection<PersonNode> _networkNodes = new();

    [ObservableProperty]
    private ObservableCollection<PersonEdge> _networkEdges = new();

    // Activity heatmap
    [ObservableProperty]
    private ObservableCollection<ActivityHeatmapCell> _activityByDayOfWeek = new();

    [ObservableProperty]
    private ObservableCollection<ActivityHeatmapCell> _activityByMonth = new();

    [ObservableProperty]
    private ObservableCollection<ActivityHeatmapCell> _activityByYear = new();

    [ObservableProperty]
    private HeatmapType _selectedHeatmapType = HeatmapType.MonthOfYear;

    // Selected items
    [ObservableProperty]
    private CategoryDistribution? _selectedCategory;

    [ObservableProperty]
    private TagCloudItem? _selectedTag;

    [ObservableProperty]
    private PersonNode? _selectedPerson;

    // View state
    [ObservableProperty]
    private bool _showCategoryChart = true;

    [ObservableProperty]
    private bool _showDensityChart = true;

    [ObservableProperty]
    private bool _showTagCloud = true;

    [ObservableProperty]
    private bool _showPeopleNetwork = true;

    [ObservableProperty]
    private bool _showActivityHeatmap = true;

    #endregion

    #region Computed Properties

    public string AudioPercentageText =>
        TotalEvents > 0 ? $"{(double)EventsWithAudio / TotalEvents * 100:F1}%" : "0%";

    public string TranscriptPercentageText =>
        TotalEvents > 0 ? $"{(double)EventsWithTranscript / TotalEvents * 100:F1}%" : "0%";

    public ObservableCollection<TimelineDensityPoint> CurrentDensity =>
        SelectedDensityGranularity == DensityGranularity.Yearly ? YearlyDensity : MonthlyDensity;

    public ObservableCollection<ActivityHeatmapCell> CurrentHeatmap => SelectedHeatmapType switch
    {
        HeatmapType.DayOfWeek => ActivityByDayOfWeek,
        HeatmapType.YearOverTime => ActivityByYear,
        _ => ActivityByMonth
    };

    #endregion

    public AnalyticsViewModel(IAnalyticsService analyticsService, ILogger<AnalyticsViewModel> logger)
    {
        _analyticsService = analyticsService;
        _logger = logger;
    }

    /// <summary>
    /// Initialize and load analytics data.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadAnalyticsAsync();
    }

    /// <summary>
    /// Refresh all analytics data.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadAnalyticsAsync();
    }

    /// <summary>
    /// Load all analytics data.
    /// </summary>
    private async Task LoadAnalyticsAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Loading analytics...";

            var analytics = await _analyticsService.GetTimelineAnalyticsAsync();

            // Update overview stats
            TotalEvents = analytics.TotalEvents;
            TotalEras = analytics.TotalEras;
            TotalTags = analytics.TotalTags;
            TotalPeople = analytics.TotalPeople;
            TotalLocations = analytics.TotalLocations;
            EventsWithAudio = analytics.EventsWithAudio;
            EventsWithTranscript = analytics.EventsWithTranscript;
            EarliestDate = analytics.EarliestEventDate;
            LatestDate = analytics.LatestEventDate;

            if (analytics.TimelineSpan.HasValue)
            {
                var span = analytics.TimelineSpan.Value;
                var years = (int)(span.TotalDays / 365);
                var months = (int)((span.TotalDays % 365) / 30);
                TimelineSpanText = years > 0
                    ? $"{years} years, {months} months"
                    : $"{months} months";
            }
            else
            {
                TimelineSpanText = "N/A";
            }

            OnPropertyChanged(nameof(AudioPercentageText));
            OnPropertyChanged(nameof(TranscriptPercentageText));

            // Update category distribution
            CategoryDistribution.Clear();
            foreach (var item in analytics.CategoryDistribution)
            {
                CategoryDistribution.Add(item);
            }

            // Update timeline density
            MonthlyDensity.Clear();
            foreach (var point in analytics.MonthlyDensity)
            {
                MonthlyDensity.Add(point);
            }

            YearlyDensity.Clear();
            foreach (var point in analytics.YearlyDensity)
            {
                YearlyDensity.Add(point);
            }
            OnPropertyChanged(nameof(CurrentDensity));

            // Update tag cloud
            TagCloud.Clear();
            foreach (var tag in analytics.TagCloud)
            {
                TagCloud.Add(tag);
            }

            // Update people network
            PeopleNetwork = analytics.PeopleNetwork;
            NetworkNodes.Clear();
            foreach (var node in analytics.PeopleNetwork.Nodes)
            {
                NetworkNodes.Add(node);
            }
            NetworkEdges.Clear();
            foreach (var edge in analytics.PeopleNetwork.Edges)
            {
                NetworkEdges.Add(edge);
            }

            // Update activity heatmaps
            ActivityByDayOfWeek.Clear();
            foreach (var cell in analytics.ActivityByDayOfWeek)
            {
                ActivityByDayOfWeek.Add(cell);
            }

            ActivityByMonth.Clear();
            foreach (var cell in analytics.ActivityByMonth)
            {
                ActivityByMonth.Add(cell);
            }

            ActivityByYear.Clear();
            foreach (var cell in analytics.ActivityByYear)
            {
                ActivityByYear.Add(cell);
            }
            OnPropertyChanged(nameof(CurrentHeatmap));

            StatusMessage = $"Analytics loaded: {TotalEvents} events analyzed";
            _logger.LogInformation("Analytics loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analytics");
            StatusMessage = "Error loading analytics";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Change density chart granularity.
    /// </summary>
    [RelayCommand]
    public void ChangeDensityGranularity(DensityGranularity granularity)
    {
        SelectedDensityGranularity = granularity;
        OnPropertyChanged(nameof(CurrentDensity));
    }

    /// <summary>
    /// Change heatmap type.
    /// </summary>
    [RelayCommand]
    public void ChangeHeatmapType(HeatmapType type)
    {
        SelectedHeatmapType = type;
        OnPropertyChanged(nameof(CurrentHeatmap));
    }

    /// <summary>
    /// Select a category for drill-down.
    /// </summary>
    [RelayCommand]
    public void SelectCategory(CategoryDistribution? category)
    {
        SelectedCategory = category;
        if (category != null)
        {
            _logger.LogInformation("Selected category: {Category}", category.Category);
        }
    }

    /// <summary>
    /// Select a tag for drill-down.
    /// </summary>
    [RelayCommand]
    public void SelectTag(TagCloudItem? tag)
    {
        SelectedTag = tag;
        if (tag != null)
        {
            _logger.LogInformation("Selected tag: {TagId}", tag.TagId);
        }
    }

    /// <summary>
    /// Select a person for drill-down.
    /// </summary>
    [RelayCommand]
    public void SelectPerson(PersonNode? person)
    {
        SelectedPerson = person;
        if (person != null)
        {
            _logger.LogInformation("Selected person: {PersonId}", person.PersonId);
        }
    }

    /// <summary>
    /// Toggle chart visibility.
    /// </summary>
    [RelayCommand]
    public void ToggleCategoryChart() => ShowCategoryChart = !ShowCategoryChart;

    [RelayCommand]
    public void ToggleDensityChart() => ShowDensityChart = !ShowDensityChart;

    [RelayCommand]
    public void ToggleTagCloud() => ShowTagCloud = !ShowTagCloud;

    [RelayCommand]
    public void TogglePeopleNetwork() => ShowPeopleNetwork = !ShowPeopleNetwork;

    [RelayCommand]
    public void ToggleActivityHeatmap() => ShowActivityHeatmap = !ShowActivityHeatmap;

    /// <summary>
    /// Export analytics as JSON.
    /// </summary>
    [RelayCommand]
    public async Task ExportAnalyticsAsync()
    {
        try
        {
            StatusMessage = "Export functionality coming soon...";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting analytics");
            StatusMessage = "Error exporting analytics";
        }
    }
}
