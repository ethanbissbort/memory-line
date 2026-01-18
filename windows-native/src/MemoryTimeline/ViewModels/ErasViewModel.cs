using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using System.Collections.ObjectModel;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for the Eras management page.
/// Provides CRUD operations for era/life phase management.
/// </summary>
public partial class ErasViewModel : ObservableObject
{
    private readonly IEraRepository _eraRepository;
    private readonly ILogger<ErasViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<Era> _eras = new();

    [ObservableProperty]
    private Era? _selectedEra;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private int _totalEraCount;

    // Form fields for Add/Edit
    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private DateTime _editStartDate = DateTime.Now;

    [ObservableProperty]
    private DateTime? _editEndDate;

    [ObservableProperty]
    private string _editColorCode = "#4169E1";

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private bool _isEditMode;

    private string? _editingEraId;

    // Predefined color palette for eras
    public static readonly List<string> ColorPalette = new()
    {
        "#FF6B6B", // Red
        "#4ECDC4", // Teal
        "#45B7D1", // Sky Blue
        "#96CEB4", // Sage Green
        "#FFEAA7", // Pale Yellow
        "#DDA0DD", // Plum
        "#98D8C8", // Mint
        "#F7DC6F", // Gold
        "#BB8FCE", // Purple
        "#85C1E9", // Light Blue
        "#F8B500", // Amber
        "#27AE60", // Green
        "#E74C3C", // Crimson
        "#3498DB", // Blue
        "#9B59B6", // Violet
        "#1ABC9C", // Turquoise
    };

    public ErasViewModel(
        IEraRepository eraRepository,
        ILogger<ErasViewModel> logger)
    {
        _eraRepository = eraRepository;
        _logger = logger;
    }

    /// <summary>
    /// Loads all eras from the database.
    /// </summary>
    [RelayCommand]
    public async Task LoadErasAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            StatusMessage = "Loading eras...";

            var eras = await _eraRepository.GetOrderedByDateAsync();
            Eras.Clear();
            foreach (var era in eras)
            {
                Eras.Add(era);
            }

            TotalEraCount = Eras.Count;
            StatusMessage = $"Loaded {TotalEraCount} eras";
            _logger.LogInformation("Loaded {Count} eras", TotalEraCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading eras");
            StatusMessage = "Error loading eras";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Prepares the form for adding a new era.
    /// </summary>
    [RelayCommand]
    public void PrepareAddEra()
    {
        _editingEraId = null;
        IsEditMode = false;
        EditName = string.Empty;
        EditStartDate = DateTime.Now;
        EditEndDate = null;
        EditColorCode = ColorPalette[Eras.Count % ColorPalette.Count]; // Cycle through colors
        EditDescription = string.Empty;
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
        EditStartDate = era.StartDate;
        EditEndDate = era.EndDate;
        EditColorCode = era.ColorCode;
        EditDescription = era.Description ?? string.Empty;
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
                // Create new era
                var newEra = new Era
                {
                    Name = EditName.Trim(),
                    StartDate = EditStartDate,
                    EndDate = EditEndDate,
                    ColorCode = EditColorCode,
                    Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim()
                };

                await _eraRepository.AddAsync(newEra);
                Eras.Add(newEra);
                TotalEraCount = Eras.Count;
                StatusMessage = $"Created era: {newEra.Name}";
                _logger.LogInformation("Created era: {Name}", newEra.Name);
            }
            else
            {
                // Update existing era
                var existingEra = await _eraRepository.GetByIdAsync(_editingEraId);
                if (existingEra == null)
                {
                    StatusMessage = "Era not found";
                    return;
                }

                existingEra.Name = EditName.Trim();
                existingEra.StartDate = EditStartDate;
                existingEra.EndDate = EditEndDate;
                existingEra.ColorCode = EditColorCode;
                existingEra.Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim();
                existingEra.UpdatedAt = DateTime.UtcNow;

                await _eraRepository.UpdateAsync(existingEra);

                // Update in collection
                var index = Eras.ToList().FindIndex(e => e.EraId == _editingEraId);
                if (index >= 0)
                {
                    Eras[index] = existingEra;
                }

                StatusMessage = $"Updated era: {existingEra.Name}";
                _logger.LogInformation("Updated era: {EraId} - {Name}", existingEra.EraId, existingEra.Name);
            }

            // Reload to get proper sorting
            await LoadErasAsync();
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
            Eras.Remove(era);
            TotalEraCount = Eras.Count;

            if (SelectedEra?.EraId == era.EraId)
            {
                SelectedEra = null;
            }

            StatusMessage = $"Deleted era: {era.Name}";
            _logger.LogInformation("Deleted era: {EraId} - {Name}", era.EraId, era.Name);
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
    }

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
