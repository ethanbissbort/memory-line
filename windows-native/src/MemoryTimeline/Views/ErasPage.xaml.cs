using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Data.Models;
using MemoryTimeline.ViewModels;
using Windows.UI;

namespace MemoryTimeline.Views;

/// <summary>
/// Page for managing eras/life phases with Gantt-style visualization.
/// Provides CRUD operations for eras and milestones.
/// </summary>
public sealed partial class ErasPage : Page
{
    public ErasViewModel ViewModel { get; }

    /// <summary>
    /// Color palette for era colors - bound to the GridView.
    /// </summary>
    public List<string> ColorPalette => ErasViewModel.ColorPalette;

    private Era? _eraToDelete;
    private Milestone? _milestoneToDelete;

    public ErasPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<ErasViewModel>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    #region Era CRUD Handlers

    /// <summary>
    /// Opens the Add Era dialog.
    /// </summary>
    private async void AddEra_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PrepareAddEra();
        PrepareEraDialogForAdd();
        await EraDialog.ShowAsync();
    }

    /// <summary>
    /// Opens the Edit Era dialog for the selected era.
    /// </summary>
    private async void EditSelectedEra_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEra != null)
        {
            ViewModel.PrepareEditEra(ViewModel.SelectedEra);
            PrepareEraDialogForEdit(ViewModel.SelectedEra);
            await EraDialog.ShowAsync();
        }
    }

    /// <summary>
    /// Opens the Delete Confirmation dialog for the selected era.
    /// </summary>
    private async void DeleteSelectedEra_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEra != null)
        {
            _eraToDelete = ViewModel.SelectedEra;
            await DeleteConfirmDialog.ShowAsync();
        }
    }

    /// <summary>
    /// Handles era bar click for selection.
    /// </summary>
    private void EraBar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is GanttEraBarDto eraBar)
        {
            var era = ViewModel.Eras.FirstOrDefault(era => era.EraId == eraBar.EraId);
            ViewModel.SelectEra(era);
        }
    }

    /// <summary>
    /// Handles era bar double-click for editing.
    /// </summary>
    private async void EraBar_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is GanttEraBarDto eraBar)
        {
            var era = ViewModel.Eras.FirstOrDefault(era => era.EraId == eraBar.EraId);
            if (era != null)
            {
                ViewModel.PrepareEditEra(era);
                PrepareEraDialogForEdit(era);
                await EraDialog.ShowAsync();
            }
        }
    }

    /// <summary>
    /// Saves the era when the dialog's primary button is clicked.
    /// </summary>
    private async void EraDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(EraNameBox.Text))
        {
            args.Cancel = true;
            return;
        }

        // Update ViewModel properties from dialog
        ViewModel.EditName = EraNameBox.Text;
        ViewModel.EditSubtitle = EraSubtitleBox.Text;
        ViewModel.EditStartDate = EraStartDatePicker.Date?.DateTime ?? DateTime.Now;
        ViewModel.EditEndDate = EraEndDatePicker.Date?.DateTime;
        ViewModel.EditDescription = EraDescriptionBox.Text;
        ViewModel.EditNotes = EraNotesBox.Text;

        // Category
        if (EraCategoryComboBox.SelectedItem is EraCategory selectedCategory)
        {
            ViewModel.EditCategoryId = selectedCategory.CategoryId;
        }

        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.SaveEraAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// Confirms era deletion.
    /// </summary>
    private async void DeleteConfirmDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_eraToDelete != null)
        {
            var deferral = args.GetDeferral();
            try
            {
                await ViewModel.DeleteEraAsync(_eraToDelete);
            }
            finally
            {
                _eraToDelete = null;
                deferral.Complete();
            }
        }
    }

    /// <summary>
    /// Prepares the dialog for adding a new era.
    /// </summary>
    private void PrepareEraDialogForAdd()
    {
        EraDialog.Title = "Add Era";
        EraNameBox.Text = string.Empty;
        EraSubtitleBox.Text = string.Empty;
        EraStartDatePicker.Date = DateTimeOffset.Now;
        EraEndDatePicker.Date = null;
        EraDescriptionBox.Text = string.Empty;
        EraNotesBox.Text = string.Empty;

        // Select first category
        EraCategoryComboBox.SelectedIndex = 0;

        // Select the default color in the palette
        var defaultColorIndex = ViewModel.Eras.Count % ColorPalette.Count;
        ColorPaletteGrid.SelectedIndex = defaultColorIndex;
        CustomColorBox.Text = ColorPalette[defaultColorIndex];
        UpdateColorPreview(ColorPalette[defaultColorIndex]);
    }

    /// <summary>
    /// Prepares the dialog for editing an existing era.
    /// </summary>
    private void PrepareEraDialogForEdit(Era era)
    {
        EraDialog.Title = "Edit Era";
        EraNameBox.Text = era.Name;
        EraSubtitleBox.Text = era.Subtitle ?? string.Empty;
        EraStartDatePicker.Date = new DateTimeOffset(era.StartDate);
        EraEndDatePicker.Date = era.EndDate.HasValue ? new DateTimeOffset(era.EndDate.Value) : null;
        EraDescriptionBox.Text = era.Description ?? string.Empty;
        EraNotesBox.Text = era.Notes ?? string.Empty;

        // Select category
        var category = ViewModel.Categories.FirstOrDefault(c => c.CategoryId == era.CategoryId);
        EraCategoryComboBox.SelectedItem = category;

        // Color
        var colorIndex = ColorPalette.IndexOf(era.ColorCode);
        if (colorIndex >= 0)
        {
            ColorPaletteGrid.SelectedIndex = colorIndex;
        }
        else
        {
            ColorPaletteGrid.SelectedItem = null;
        }

        CustomColorBox.Text = era.ColorCode;
        UpdateColorPreview(era.ColorCode);
    }

    #endregion

    #region Milestone CRUD Handlers

    /// <summary>
    /// Opens the Add Milestone dialog.
    /// </summary>
    private async void AddMilestone_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PrepareAddMilestone();
        PrepareMilestoneDialogForAdd();
        await MilestoneDialog.ShowAsync();
    }

    /// <summary>
    /// Handles milestone marker click for selection.
    /// </summary>
    private void MilestoneMarker_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MilestoneMarkerDto marker)
        {
            var milestone = ViewModel.Milestones.FirstOrDefault(m => m.MilestoneId == marker.MilestoneId);
            ViewModel.SelectedMilestone = milestone;

            // Update selection state
            foreach (var m in ViewModel.MilestoneMarkers)
            {
                m.IsSelected = m.MilestoneId == marker.MilestoneId;
            }
        }
    }

    /// <summary>
    /// Saves the milestone when the dialog's primary button is clicked.
    /// </summary>
    private async void MilestoneDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(MilestoneNameBox.Text))
        {
            args.Cancel = true;
            return;
        }

        ViewModel.EditMilestoneName = MilestoneNameBox.Text;
        ViewModel.EditMilestoneDate = MilestoneDatePicker.Date?.DateTime ?? DateTime.Now;
        ViewModel.EditMilestoneDescription = MilestoneDescriptionBox.Text;

        // Milestone type
        if (MilestoneTypeComboBox.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string typeTag)
        {
            ViewModel.EditMilestoneType = Enum.Parse<MilestoneType>(typeTag);
        }

        // Linked era
        if (MilestoneLinkedEraComboBox.SelectedItem is Era linkedEra)
        {
            ViewModel.EditMilestoneLinkedEraId = linkedEra.EraId;
        }
        else
        {
            ViewModel.EditMilestoneLinkedEraId = null;
        }

        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.SaveMilestoneAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// Confirms milestone deletion.
    /// </summary>
    private async void DeleteMilestoneConfirmDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_milestoneToDelete != null)
        {
            var deferral = args.GetDeferral();
            try
            {
                await ViewModel.DeleteMilestoneAsync(_milestoneToDelete);
            }
            finally
            {
                _milestoneToDelete = null;
                deferral.Complete();
            }
        }
    }

    /// <summary>
    /// Prepares the dialog for adding a new milestone.
    /// </summary>
    private void PrepareMilestoneDialogForAdd()
    {
        MilestoneDialog.Title = "Add Milestone";
        MilestoneNameBox.Text = string.Empty;
        MilestoneDatePicker.Date = DateTimeOffset.Now;
        MilestoneTypeComboBox.SelectedIndex = 0;
        MilestoneLinkedEraComboBox.SelectedItem = null;
        MilestoneDescriptionBox.Text = string.Empty;
    }

    #endregion

    #region Category Filter Handlers

    /// <summary>
    /// Handles category filter toggle.
    /// </summary>
    private void CategoryFilter_Click(object sender, RoutedEventArgs e)
    {
        // Regenerate layout after category visibility change
        ViewModel.GenerateGanttLayout();
    }

    /// <summary>
    /// Shows all categories.
    /// </summary>
    private void ShowAllCategories_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowAllCategories();
    }

    /// <summary>
    /// Hides all categories.
    /// </summary>
    private void HideAllCategories_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.HideAllCategories();
    }

    #endregion

    #region Zoom Handlers

    /// <summary>
    /// Zooms in on the timeline.
    /// </summary>
    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ZoomIn();
    }

    /// <summary>
    /// Zooms out on the timeline.
    /// </summary>
    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ZoomOut();
    }

    #endregion

    #region Color Picker Handlers

    /// <summary>
    /// Handles color palette selection.
    /// </summary>
    private void ColorPalette_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColorPaletteGrid.SelectedItem is string colorCode)
        {
            ViewModel.EditColorCode = colorCode;
            CustomColorBox.Text = colorCode;
            UpdateColorPreview(colorCode);
        }
    }

    /// <summary>
    /// Handles custom color input.
    /// </summary>
    private void CustomColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var colorCode = CustomColorBox.Text?.Trim();
        if (!string.IsNullOrEmpty(colorCode) && IsValidHexColor(colorCode))
        {
            ViewModel.EditColorCode = colorCode;
            UpdateColorPreview(colorCode);

            if (ColorPaletteGrid.SelectedItem is string selectedColor && selectedColor != colorCode)
            {
                ColorPaletteGrid.SelectedItem = null;
            }
        }
    }

    /// <summary>
    /// Updates the color preview rectangle.
    /// </summary>
    private void UpdateColorPreview(string colorCode)
    {
        try
        {
            var color = ParseHexColor(colorCode);
            ColorPreview.Fill = new SolidColorBrush(color);
        }
        catch
        {
            ColorPreview.Fill = new SolidColorBrush(Colors.Gray);
        }
    }

    /// <summary>
    /// Validates a hex color code.
    /// </summary>
    private static bool IsValidHexColor(string colorCode)
    {
        if (string.IsNullOrEmpty(colorCode))
            return false;

        if (!colorCode.StartsWith("#"))
            return false;

        var hex = colorCode.Substring(1);
        if (hex.Length != 6 && hex.Length != 8)
            return false;

        return hex.All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
    }

    /// <summary>
    /// Parses a hex color code to a Color.
    /// </summary>
    private static Color ParseHexColor(string colorCode)
    {
        if (string.IsNullOrEmpty(colorCode) || !colorCode.StartsWith("#"))
            return Colors.Gray;

        var hex = colorCode.Substring(1);

        if (hex.Length == 6)
        {
            return Color.FromArgb(
                255,
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
        else if (hex.Length == 8)
        {
            return Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16));
        }

        return Colors.Gray;
    }

    #endregion
}
