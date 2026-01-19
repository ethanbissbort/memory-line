using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using System.Collections.ObjectModel;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for the Eras management page with Gantt-style visualization.
/// Provides CRUD operations for eras, categories, and milestones.
/// </summary>
public partial class ErasViewModel : ObservableObject
{
    private readonly IEraRepository _eraRepository;
    private readonly IEraCategoryRepository _categoryRepository;
    private readonly IMilestoneRepository _milestoneRepository;
    private readonly ILogger<ErasViewModel> _logger;

    // Data collections
    [ObservableProperty]
    private ObservableCollection<Era> _eras = new();

    [ObservableProperty]
    private ObservableCollection<EraCategory> _categories = new();

    [ObservableProperty]
    private ObservableCollection<Milestone> _milestones = new();

    // Display collections (DTOs for visualization)
    [ObservableProperty]
    private ObservableCollection<GanttEraBarDto> _ganttEraBars = new();

    [ObservableProperty]
    private ObservableCollection<MilestoneMarkerDto> _milestoneMarkers = new();

    [ObservableProperty]
    private ObservableCollection<EraCategoryDto> _categoryFilters = new();

    [ObservableProperty]
    private ObservableCollection<EraRowGroupDto> _groupedEras = new();

    [ObservableProperty]
    private ObservableCollection<TimeRulerTickDto> _timeRulerTicks = new();

    // Selection
    [ObservableProperty]
    private Era? _selectedEra;

    [ObservableProperty]
    private Milestone? _selectedMilestone;

    [ObservableProperty]
    private GanttEraBarDto? _selectedEraBar;

    [ObservableProperty]
    private MilestoneMarkerDto? _selectedMarker;

    // Viewport state
    [ObservableProperty]
    private DateTime _viewportStart;

    [ObservableProperty]
    private DateTime _viewportEnd;

    [ObservableProperty]
    private double _pixelsPerDay = 2.0;

    [ObservableProperty]
    private double _viewportWidth = 1200;

    [ObservableProperty]
    private double _viewportHeight = 600;

    // UI state
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private int _totalEraCount;

    [ObservableProperty]
    private int _totalMilestoneCount;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private bool _showMilestones = true;

    [ObservableProperty]
    private double _totalContentHeight;

    // Form fields for Add/Edit Era
    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editSubtitle = string.Empty;

    [ObservableProperty]
    private DateTime _editStartDate = DateTime.Now;

    [ObservableProperty]
    private DateTime? _editEndDate;

    [ObservableProperty]
    private string _editColorCode = "#4169E1";

    [ObservableProperty]
    private string? _editCategoryId;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private string _editNotes = string.Empty;

    [ObservableProperty]
    private bool _isEditMode;

    // Form fields for Add/Edit Milestone
    [ObservableProperty]
    private string _editMilestoneName = string.Empty;

    [ObservableProperty]
    private DateTime _editMilestoneDate = DateTime.Now;

    [ObservableProperty]
    private MilestoneType _editMilestoneType = MilestoneType.Standard;

    [ObservableProperty]
    private string? _editMilestoneLinkedEraId;

    [ObservableProperty]
    private string _editMilestoneColorOverride = string.Empty;

    [ObservableProperty]
    private string _editMilestoneDescription = string.Empty;

    [ObservableProperty]
    private bool _isMilestoneEditMode;

    private string? _editingEraId;
    private string? _editingMilestoneId;

    // Predefined color palette for eras
    public static readonly List<string> ColorPalette = new()
    {
        "#FF6B6B", "#4ECDC4", "#45B7D1", "#96CEB4", "#FFEAA7", "#DDA0DD",
        "#98D8C8", "#F7DC6F", "#BB8FCE", "#85C1E9", "#F8B500", "#27AE60",
        "#E74C3C", "#3498DB", "#9B59B6", "#1ABC9C",
    };

    public ErasViewModel(
        IEraRepository eraRepository,
        IEraCategoryRepository categoryRepository,
        IMilestoneRepository milestoneRepository,
        ILogger<ErasViewModel> logger)
    {
        _eraRepository = eraRepository;
        _categoryRepository = categoryRepository;
        _milestoneRepository = milestoneRepository;
        _logger = logger;

        // Initialize viewport to show last 10 years
        var now = DateTime.Now;
        ViewportStart = now.AddYears(-10);
        ViewportEnd = now.AddYears(1);
    }

    /// <summary>
    /// Initializes the eras page, loading all data.
    /// </summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        await LoadCategoriesAsync();
        await LoadErasAsync();
        await LoadMilestonesAsync();
        CalculateViewport();
        GenerateGanttLayout();
    }

    /// <summary>
    /// Loads all categories from the database.
    /// </summary>
    [RelayCommand]
    public async Task LoadCategoriesAsync()
    {
        try
        {
            // Ensure default categories exist
            await _categoryRepository.EnsureDefaultCategoriesAsync();

            var categories = await _categoryRepository.GetOrderedAsync();
            Categories.Clear();
            CategoryFilters.Clear();

            foreach (var category in categories)
            {
                Categories.Add(category);
                CategoryFilters.Add(EraCategoryDto.FromCategory(category));
            }

            _logger.LogInformation("Loaded {Count} categories", Categories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading categories");
            StatusMessage = "Error loading categories";
        }
    }

    /// <summary>
    /// Loads all eras from the database.
    /// </summary>
    [RelayCommand]
    public async Task LoadErasAsync()
    {
        try
        {
            StatusMessage = "Loading eras...";

            var eras = await _eraRepository.GetOrderedByDateAsync();
            Eras.Clear();
            foreach (var era in eras)
            {
                Eras.Add(era);
            }

            TotalEraCount = Eras.Count;

            // Update category era counts
            foreach (var filter in CategoryFilters)
            {
                filter.EraCount = Eras.Count(e => e.CategoryId == filter.CategoryId);
            }

            StatusMessage = $"Loaded {TotalEraCount} eras";
            _logger.LogInformation("Loaded {Count} eras", TotalEraCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading eras");
            StatusMessage = "Error loading eras";
        }
    }

    /// <summary>
    /// Loads all milestones from the database.
    /// </summary>
    [RelayCommand]
    public async Task LoadMilestonesAsync()
    {
        try
        {
            var milestones = await _milestoneRepository.GetOrderedByDateAsync();
            Milestones.Clear();
            foreach (var milestone in milestones)
            {
                Milestones.Add(milestone);
            }

            TotalMilestoneCount = Milestones.Count;
            _logger.LogInformation("Loaded {Count} milestones", TotalMilestoneCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading milestones");
            StatusMessage = "Error loading milestones";
        }
    }

    /// <summary>
    /// Calculates optimal viewport based on era data.
    /// </summary>
    private void CalculateViewport()
    {
        if (!Eras.Any())
        {
            var now = DateTime.Now;
            ViewportStart = now.AddYears(-10);
            ViewportEnd = now.AddYears(1);
            return;
        }

        var earliestDate = Eras.Min(e => e.StartDate);
        var latestDate = Eras.Max(e => e.EndDate ?? DateTime.Now);

        // Add padding
        ViewportStart = earliestDate.AddMonths(-6);
        ViewportEnd = latestDate.AddMonths(6);

        // Calculate pixels per day based on viewport width
        var totalDays = (ViewportEnd - ViewportStart).TotalDays;
        PixelsPerDay = ViewportWidth / totalDays;
    }

    /// <summary>
    /// Generates the Gantt-style layout with grouped eras and milestones.
    /// </summary>
    [RelayCommand]
    public void GenerateGanttLayout()
    {
        GenerateEraBars();
        GenerateMilestoneMarkers();
        GenerateTimeRulerTicks();
        GroupErasByCategory();
        CalculateTotalContentHeight();
    }

    /// <summary>
    /// Generates era bars for the Gantt chart.
    /// </summary>
    private void GenerateEraBars()
    {
        GanttEraBars.Clear();

        // Filter by visible categories and search
        var visibleCategoryIds = CategoryFilters
            .Where(c => c.IsVisible)
            .Select(c => c.CategoryId)
            .ToHashSet();

        var filteredEras = Eras
            .Where(e => e.CategoryId == null || visibleCategoryIds.Contains(e.CategoryId))
            .Where(e => string.IsNullOrEmpty(SearchFilter) ||
                        e.Name.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Category?.SortOrder ?? int.MaxValue)
            .ThenBy(e => e.StartDate);

        foreach (var era in filteredEras)
        {
            var dto = GanttEraBarDto.FromEra(era);
            CalculateEraBarPosition(dto);
            GanttEraBars.Add(dto);
        }
    }

    /// <summary>
    /// Calculates the pixel position and width for an era bar.
    /// </summary>
    private void CalculateEraBarPosition(GanttEraBarDto era)
    {
        var startDays = (era.StartDate - ViewportStart).TotalDays;
        era.PixelX = startDays * PixelsPerDay;

        var endDate = era.EndDate ?? ViewportEnd;
        var endDays = (endDate - ViewportStart).TotalDays;
        var endPixelX = endDays * PixelsPerDay;

        era.Width = Math.Max(EraLayoutConstants.MinBarWidth, endPixelX - era.PixelX);
        era.IsVisible = era.PixelX + era.Width >= 0 && era.PixelX <= ViewportWidth;
    }

    /// <summary>
    /// Generates milestone markers for the timeline.
    /// </summary>
    private void GenerateMilestoneMarkers()
    {
        MilestoneMarkers.Clear();

        if (!ShowMilestones) return;

        var markers = Milestones
            .OrderBy(m => m.Date)
            .Select(m => MilestoneMarkerDto.FromMilestone(m))
            .ToList();

        // Calculate positions and handle collision/stacking
        var occupiedRanges = new List<(double left, double right, int tier)>();

        foreach (var marker in markers)
        {
            var days = (marker.Date - ViewportStart).TotalDays;
            marker.PixelX = days * PixelsPerDay;

            // Calculate label width (estimate)
            double labelWidth = marker.Name.Length * 7 + EraLayoutConstants.MilestoneIconSize + 8;
            double totalWidth = labelWidth;
            double left = marker.PixelX - totalWidth / 2;
            double right = marker.PixelX + totalWidth / 2;

            // Find lowest tier that doesn't overlap
            int tier = 0;
            while (occupiedRanges.Any(r => r.tier == tier &&
                                          !(right < r.left || left > r.right)))
            {
                tier++;
            }

            occupiedRanges.Add((left, right, tier));
            marker.StackTier = tier;
            marker.PixelY = EraLayoutConstants.MilestoneZoneHeight -
                           (tier + 1) * EraLayoutConstants.MilestoneStackOffset;

            marker.IsVisible = marker.PixelX >= 0 && marker.PixelX <= ViewportWidth;
            MilestoneMarkers.Add(marker);
        }
    }

    /// <summary>
    /// Generates time ruler ticks for the Gantt chart.
    /// </summary>
    private void GenerateTimeRulerTicks()
    {
        TimeRulerTicks.Clear();

        // Determine tick interval based on pixels per day
        var (majorInterval, minorInterval, labelFormat) = GetTickIntervals();

        // Generate major ticks
        var currentDate = RoundToInterval(ViewportStart, majorInterval);
        while (currentDate <= ViewportEnd)
        {
            var days = (currentDate - ViewportStart).TotalDays;
            var pixelX = days * PixelsPerDay;

            if (pixelX >= 0 && pixelX <= ViewportWidth)
            {
                TimeRulerTicks.Add(new TimeRulerTickDto
                {
                    Date = currentDate,
                    PixelX = pixelX,
                    IsMajor = true,
                    Label = currentDate.ToString(labelFormat)
                });
            }

            currentDate = AddInterval(currentDate, majorInterval);
        }

        // Generate minor ticks
        currentDate = RoundToInterval(ViewportStart, minorInterval);
        while (currentDate <= ViewportEnd)
        {
            var days = (currentDate - ViewportStart).TotalDays;
            var pixelX = days * PixelsPerDay;

            // Skip if already a major tick
            if (!TimeRulerTicks.Any(t => Math.Abs(t.PixelX - pixelX) < 5) &&
                pixelX >= 0 && pixelX <= ViewportWidth)
            {
                TimeRulerTicks.Add(new TimeRulerTickDto
                {
                    Date = currentDate,
                    PixelX = pixelX,
                    IsMajor = false,
                    Label = null
                });
            }

            currentDate = AddInterval(currentDate, minorInterval);
        }
    }

    private (string major, string minor, string format) GetTickIntervals()
    {
        return PixelsPerDay switch
        {
            < 0.5 => ("year", "quarter", "yyyy"),
            < 2 => ("quarter", "month", "MMM yyyy"),
            < 7 => ("month", "week", "MMM yyyy"),
            < 30 => ("week", "day", "MMM d"),
            _ => ("day", "day", "MMM d")
        };
    }

    private DateTime RoundToInterval(DateTime date, string interval)
    {
        return interval switch
        {
            "year" => new DateTime(date.Year, 1, 1),
            "quarter" => new DateTime(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1),
            "month" => new DateTime(date.Year, date.Month, 1),
            "week" => date.AddDays(-(int)date.DayOfWeek),
            "day" => date.Date,
            _ => date
        };
    }

    private DateTime AddInterval(DateTime date, string interval)
    {
        return interval switch
        {
            "year" => date.AddYears(1),
            "quarter" => date.AddMonths(3),
            "month" => date.AddMonths(1),
            "week" => date.AddDays(7),
            "day" => date.AddDays(1),
            _ => date.AddDays(1)
        };
    }

    /// <summary>
    /// Groups eras by category for display.
    /// </summary>
    private void GroupErasByCategory()
    {
        GroupedEras.Clear();

        var groups = GanttEraBars
            .GroupBy(e => e.CategoryId ?? "uncategorized")
            .OrderBy(g => CategoryFilters.FirstOrDefault(c => c.CategoryId == g.Key)?.SortOrder ?? int.MaxValue);

        double currentY = EraLayoutConstants.MilestoneZoneHeight + EraLayoutConstants.TimeRulerHeight;

        foreach (var group in groups)
        {
            var categoryDto = CategoryFilters.FirstOrDefault(c => c.CategoryId == group.Key)
                ?? new EraCategoryDto { CategoryId = group.Key, Name = "Uncategorized" };

            var eras = group.ToList();

            // Calculate row positions within category
            int rowIndex = 0;
            foreach (var era in eras.OrderBy(e => e.StartDate))
            {
                era.RowIndex = rowIndex;
                era.RowY = currentY + EraLayoutConstants.CategoryHeaderHeight +
                          (rowIndex * (EraLayoutConstants.EraRowHeight + EraLayoutConstants.RowSpacing));
                rowIndex++;
            }

            var groupDto = new EraRowGroupDto(categoryDto, eras);
            GroupedEras.Add(groupDto);

            currentY += EraLayoutConstants.CategoryHeaderHeight +
                       (eras.Count * (EraLayoutConstants.EraRowHeight + EraLayoutConstants.RowSpacing));
        }
    }

    /// <summary>
    /// Calculates the total content height for scrolling.
    /// </summary>
    private void CalculateTotalContentHeight()
    {
        TotalContentHeight = EraLayoutConstants.MilestoneZoneHeight +
                            EraLayoutConstants.TimeRulerHeight +
                            GroupedEras.Sum(g => EraLayoutConstants.CategoryHeaderHeight +
                                                 g.Eras.Count * (EraLayoutConstants.EraRowHeight + EraLayoutConstants.RowSpacing));
    }

    #region Era CRUD Operations

    /// <summary>
    /// Prepares the form for adding a new era.
    /// </summary>
    [RelayCommand]
    public void PrepareAddEra()
    {
        _editingEraId = null;
        IsEditMode = false;
        EditName = string.Empty;
        EditSubtitle = string.Empty;
        EditStartDate = DateTime.Now;
        EditEndDate = null;
        EditColorCode = ColorPalette[Eras.Count % ColorPalette.Count];
        EditCategoryId = Categories.FirstOrDefault()?.CategoryId;
        EditDescription = string.Empty;
        EditNotes = string.Empty;
    }

    /// <summary>
    /// Prepares the form for editing an existing era.
    /// </summary>
    [RelayCommand]
    public void PrepareEditEra(Era? era)
    {
        if (era == null) return;

        _editingEraId = era.EraId;
        IsEditMode = true;
        EditName = era.Name;
        EditSubtitle = era.Subtitle ?? string.Empty;
        EditStartDate = era.StartDate;
        EditEndDate = era.EndDate;
        EditColorCode = era.ColorCode;
        EditCategoryId = era.CategoryId;
        EditDescription = era.Description ?? string.Empty;
        EditNotes = era.Notes ?? string.Empty;
    }

    /// <summary>
    /// Saves the current era (create or update).
    /// </summary>
    [RelayCommand]
    public async Task SaveEraAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            StatusMessage = "Era name is required";
            return;
        }

        try
        {
            IsLoading = true;

            if (_editingEraId == null)
            {
                var newEra = new Era
                {
                    Name = EditName.Trim(),
                    Subtitle = string.IsNullOrWhiteSpace(EditSubtitle) ? null : EditSubtitle.Trim(),
                    StartDate = EditStartDate,
                    EndDate = EditEndDate,
                    ColorCode = EditColorCode,
                    CategoryId = EditCategoryId,
                    Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim(),
                    Notes = string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes.Trim()
                };

                await _eraRepository.AddAsync(newEra);
                StatusMessage = $"Created era: {newEra.Name}";
                _logger.LogInformation("Created era: {Name}", newEra.Name);
            }
            else
            {
                var existingEra = await _eraRepository.GetByIdAsync(_editingEraId);
                if (existingEra == null)
                {
                    StatusMessage = "Era not found";
                    return;
                }

                existingEra.Name = EditName.Trim();
                existingEra.Subtitle = string.IsNullOrWhiteSpace(EditSubtitle) ? null : EditSubtitle.Trim();
                existingEra.StartDate = EditStartDate;
                existingEra.EndDate = EditEndDate;
                existingEra.ColorCode = EditColorCode;
                existingEra.CategoryId = EditCategoryId;
                existingEra.Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim();
                existingEra.Notes = string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes.Trim();
                existingEra.UpdatedAt = DateTime.UtcNow;

                await _eraRepository.UpdateAsync(existingEra);
                StatusMessage = $"Updated era: {existingEra.Name}";
                _logger.LogInformation("Updated era: {EraId} - {Name}", existingEra.EraId, existingEra.Name);
            }

            await LoadErasAsync();
            CalculateViewport();
            GenerateGanttLayout();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving era");
            StatusMessage = "Error saving era";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Deletes the specified era.
    /// </summary>
    [RelayCommand]
    public async Task DeleteEraAsync(Era? era)
    {
        if (era == null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Deleting era...";

            await _eraRepository.DeleteAsync(era);

            if (SelectedEra?.EraId == era.EraId)
            {
                SelectedEra = null;
            }

            StatusMessage = $"Deleted era: {era.Name}";
            _logger.LogInformation("Deleted era: {EraId} - {Name}", era.EraId, era.Name);

            await LoadErasAsync();
            CalculateViewport();
            GenerateGanttLayout();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting era: {EraId}", era.EraId);
            StatusMessage = "Error deleting era";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Selects an era for viewing/editing.
    /// </summary>
    [RelayCommand]
    public void SelectEra(Era? era)
    {
        SelectedEra = era;

        // Update selection state in DTOs
        foreach (var bar in GanttEraBars)
        {
            bar.IsSelected = bar.EraId == era?.EraId;
        }
    }

    #endregion

    #region Milestone CRUD Operations

    /// <summary>
    /// Prepares the form for adding a new milestone.
    /// </summary>
    [RelayCommand]
    public void PrepareAddMilestone()
    {
        _editingMilestoneId = null;
        IsMilestoneEditMode = false;
        EditMilestoneName = string.Empty;
        EditMilestoneDate = DateTime.Now;
        EditMilestoneType = MilestoneType.Standard;
        EditMilestoneLinkedEraId = null;
        EditMilestoneColorOverride = string.Empty;
        EditMilestoneDescription = string.Empty;
    }

    /// <summary>
    /// Prepares the form for editing an existing milestone.
    /// </summary>
    [RelayCommand]
    public void PrepareEditMilestone(Milestone? milestone)
    {
        if (milestone == null) return;

        _editingMilestoneId = milestone.MilestoneId;
        IsMilestoneEditMode = true;
        EditMilestoneName = milestone.Name;
        EditMilestoneDate = milestone.Date;
        EditMilestoneType = milestone.Type;
        EditMilestoneLinkedEraId = milestone.LinkedEraId;
        EditMilestoneColorOverride = milestone.ColorOverride ?? string.Empty;
        EditMilestoneDescription = milestone.Description ?? string.Empty;
    }

    /// <summary>
    /// Saves the current milestone (create or update).
    /// </summary>
    [RelayCommand]
    public async Task SaveMilestoneAsync()
    {
        if (string.IsNullOrWhiteSpace(EditMilestoneName))
        {
            StatusMessage = "Milestone name is required";
            return;
        }

        try
        {
            IsLoading = true;

            if (_editingMilestoneId == null)
            {
                var newMilestone = new Milestone
                {
                    Name = EditMilestoneName.Trim(),
                    Date = EditMilestoneDate,
                    Type = EditMilestoneType,
                    LinkedEraId = EditMilestoneLinkedEraId,
                    ColorOverride = string.IsNullOrWhiteSpace(EditMilestoneColorOverride) ? null : EditMilestoneColorOverride,
                    Description = string.IsNullOrWhiteSpace(EditMilestoneDescription) ? null : EditMilestoneDescription.Trim()
                };

                await _milestoneRepository.AddAsync(newMilestone);
                StatusMessage = $"Created milestone: {newMilestone.Name}";
                _logger.LogInformation("Created milestone: {Name}", newMilestone.Name);
            }
            else
            {
                var existingMilestone = await _milestoneRepository.GetByIdAsync(_editingMilestoneId);
                if (existingMilestone == null)
                {
                    StatusMessage = "Milestone not found";
                    return;
                }

                existingMilestone.Name = EditMilestoneName.Trim();
                existingMilestone.Date = EditMilestoneDate;
                existingMilestone.Type = EditMilestoneType;
                existingMilestone.LinkedEraId = EditMilestoneLinkedEraId;
                existingMilestone.ColorOverride = string.IsNullOrWhiteSpace(EditMilestoneColorOverride) ? null : EditMilestoneColorOverride;
                existingMilestone.Description = string.IsNullOrWhiteSpace(EditMilestoneDescription) ? null : EditMilestoneDescription.Trim();
                existingMilestone.UpdatedAt = DateTime.UtcNow;

                await _milestoneRepository.UpdateAsync(existingMilestone);
                StatusMessage = $"Updated milestone: {existingMilestone.Name}";
                _logger.LogInformation("Updated milestone: {MilestoneId} - {Name}", existingMilestone.MilestoneId, existingMilestone.Name);
            }

            await LoadMilestonesAsync();
            GenerateGanttLayout();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving milestone");
            StatusMessage = "Error saving milestone";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Deletes the specified milestone.
    /// </summary>
    [RelayCommand]
    public async Task DeleteMilestoneAsync(Milestone? milestone)
    {
        if (milestone == null) return;

        try
        {
            IsLoading = true;
            await _milestoneRepository.DeleteAsync(milestone);

            if (SelectedMilestone?.MilestoneId == milestone.MilestoneId)
            {
                SelectedMilestone = null;
            }

            StatusMessage = $"Deleted milestone: {milestone.Name}";
            _logger.LogInformation("Deleted milestone: {MilestoneId} - {Name}", milestone.MilestoneId, milestone.Name);

            await LoadMilestonesAsync();
            GenerateGanttLayout();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting milestone: {MilestoneId}", milestone.MilestoneId);
            StatusMessage = "Error deleting milestone";
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Filtering and Navigation

    /// <summary>
    /// Toggles visibility of a category.
    /// </summary>
    [RelayCommand]
    public void ToggleCategoryVisibility(string categoryId)
    {
        var category = CategoryFilters.FirstOrDefault(c => c.CategoryId == categoryId);
        if (category != null)
        {
            category.IsVisible = !category.IsVisible;
            GenerateGanttLayout();
        }
    }

    /// <summary>
    /// Shows all categories.
    /// </summary>
    [RelayCommand]
    public void ShowAllCategories()
    {
        foreach (var category in CategoryFilters)
        {
            category.IsVisible = true;
        }
        GenerateGanttLayout();
    }

    /// <summary>
    /// Hides all categories.
    /// </summary>
    [RelayCommand]
    public void HideAllCategories()
    {
        foreach (var category in CategoryFilters)
        {
            category.IsVisible = false;
        }
        GenerateGanttLayout();
    }

    /// <summary>
    /// Applies the search filter.
    /// </summary>
    partial void OnSearchFilterChanged(string value)
    {
        GenerateGanttLayout();
    }

    /// <summary>
    /// Zooms in on the timeline.
    /// </summary>
    [RelayCommand]
    public void ZoomIn()
    {
        PixelsPerDay = Math.Min(PixelsPerDay * 1.5, 100);
        GenerateGanttLayout();
    }

    /// <summary>
    /// Zooms out on the timeline.
    /// </summary>
    [RelayCommand]
    public void ZoomOut()
    {
        PixelsPerDay = Math.Max(PixelsPerDay / 1.5, 0.1);
        GenerateGanttLayout();
    }

    /// <summary>
    /// Navigates to the earliest era.
    /// </summary>
    [RelayCommand]
    public void NavigateToStart()
    {
        if (Eras.Any())
        {
            var earliest = Eras.Min(e => e.StartDate);
            CenterOnDate(earliest);
        }
    }

    /// <summary>
    /// Navigates to the latest era or today.
    /// </summary>
    [RelayCommand]
    public void NavigateToEnd()
    {
        if (Eras.Any())
        {
            var latest = Eras.Max(e => e.EndDate ?? DateTime.Now);
            CenterOnDate(latest);
        }
        else
        {
            CenterOnDate(DateTime.Now);
        }
    }

    /// <summary>
    /// Centers the viewport on a specific date.
    /// </summary>
    public void CenterOnDate(DateTime date)
    {
        var visibleDays = ViewportWidth / PixelsPerDay;
        ViewportStart = date.AddDays(-visibleDays / 2);
        ViewportEnd = date.AddDays(visibleDays / 2);
        GenerateGanttLayout();
    }

    #endregion

    /// <summary>
    /// Gets the duration text for an era.
    /// </summary>
    public static string GetDurationText(Era era)
    {
        if (era.EndDate.HasValue)
        {
            var duration = era.EndDate.Value - era.StartDate;
            if (duration.TotalDays >= 365)
            {
                var years = (int)(duration.TotalDays / 365);
                return $"{years} year{(years != 1 ? "s" : "")}";
            }
            else if (duration.TotalDays >= 30)
            {
                var months = (int)(duration.TotalDays / 30);
                return $"{months} month{(months != 1 ? "s" : "")}";
            }
            else
            {
                return $"{(int)duration.TotalDays} day{((int)duration.TotalDays != 1 ? "s" : "")}";
            }
        }
        return "Ongoing";
    }
}
