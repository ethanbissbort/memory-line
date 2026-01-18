using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using MemoryTimeline.Data.Models;
using MemoryTimeline.ViewModels;
using Windows.UI;

namespace MemoryTimeline.Views;

/// <summary>
/// Page for managing eras/life phases.
/// Provides CRUD operations for creating, editing, and deleting eras.
/// </summary>
public sealed partial class ErasPage : Page
{
    public ErasViewModel ViewModel { get; }

    /// <summary>
    /// Color palette for era colors - bound to the GridView.
    /// </summary>
    public List<string> ColorPalette => ErasViewModel.ColorPalette;

    private Era? _eraToDelete;

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
        await ViewModel.LoadErasAsync();
    }

    /// <summary>
    /// Opens the Add Era dialog.
    /// </summary>
    private async void AddEra_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PrepareAddEra();
        PrepareDialogForAdd();
        await EraDialog.ShowAsync();
    }

    /// <summary>
    /// Opens the Edit Era dialog from context menu.
    /// </summary>
    private async void EditEra_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is Era era)
        {
            ViewModel.PrepareEditEra(era);
            PrepareDialogForEdit(era);
            await EraDialog.ShowAsync();
        }
    }

    /// <summary>
    /// Opens the Edit Era dialog for the selected era.
    /// </summary>
    private async void EditSelectedEra_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEra != null)
        {
            ViewModel.PrepareEditEra(ViewModel.SelectedEra);
            PrepareDialogForEdit(ViewModel.SelectedEra);
            await EraDialog.ShowAsync();
        }
    }

    /// <summary>
    /// Opens the Delete Confirmation dialog from context menu.
    /// </summary>
    private async void DeleteEra_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is Era era)
        {
            _eraToDelete = era;
            await DeleteConfirmDialog.ShowAsync();
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
    /// Handles era item click for selection.
    /// </summary>
    private void Era_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Era era)
        {
            ViewModel.SelectEra(era);
        }
    }

    /// <summary>
    /// Saves the era when the dialog's primary button is clicked.
    /// </summary>
    private async void EraDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(EraNameBox.Text))
        {
            args.Cancel = true;
            return;
        }

        // Update ViewModel properties from dialog
        ViewModel.EditName = EraNameBox.Text;
        ViewModel.EditStartDate = EraStartDatePicker.Date?.DateTime ?? DateTime.Now;
        ViewModel.EditEndDate = EraEndDatePicker.Date?.DateTime;
        ViewModel.EditDescription = EraDescriptionBox.Text;

        // Color is updated via SelectionChanged or TextChanged

        // Defer to allow dialog to close
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

            // Deselect palette if custom color differs
            if (ColorPaletteGrid.SelectedItem is string selectedColor && selectedColor != colorCode)
            {
                ColorPaletteGrid.SelectedItem = null;
            }
        }
    }

    /// <summary>
    /// Prepares the dialog for adding a new era.
    /// </summary>
    private void PrepareDialogForAdd()
    {
        EraDialog.Title = "Add Era";
        EraNameBox.Text = string.Empty;
        EraStartDatePicker.Date = DateTimeOffset.Now;
        EraEndDatePicker.Date = null;
        EraDescriptionBox.Text = string.Empty;

        // Select the default color in the palette
        var defaultColorIndex = ViewModel.Eras.Count % ColorPalette.Count;
        ColorPaletteGrid.SelectedIndex = defaultColorIndex;
        CustomColorBox.Text = ColorPalette[defaultColorIndex];
        UpdateColorPreview(ColorPalette[defaultColorIndex]);
    }

    /// <summary>
    /// Prepares the dialog for editing an existing era.
    /// </summary>
    private void PrepareDialogForEdit(Era era)
    {
        EraDialog.Title = "Edit Era";
        EraNameBox.Text = era.Name;
        EraStartDatePicker.Date = new DateTimeOffset(era.StartDate);
        EraEndDatePicker.Date = era.EndDate.HasValue ? new DateTimeOffset(era.EndDate.Value) : null;
        EraDescriptionBox.Text = era.Description ?? string.Empty;

        // Try to find the color in the palette
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
}
