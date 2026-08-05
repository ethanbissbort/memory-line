using System.Text.Json;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Services;
using MemoryTimeline.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace MemoryTimeline.Views;

/// <summary>
/// People page: a contact book with a master list (search, sort, favorites),
/// a detail pane with linked events, an add/edit dialog with draft support,
/// merge, and delete.
/// </summary>
public sealed partial class ContactsPage : Page
{
    /// <summary>The page's ViewModel (x:Bind root).</summary>
    public ContactsViewModel ViewModel { get; }

    /// <summary>Set once the initial load completed, so XAML-default SelectionChanged events are ignored.</summary>
    private bool _initialized;

    /// <summary>True once Loaded fired; navigation parameters arriving later run immediately.</summary>
    private bool _loaded;

    /// <summary>Navigation parameter ("draft:&lt;id&gt;" or a personId) waiting for the page to load.</summary>
    private string? _pendingParameter;

    /// <summary>PersonId being edited, or null when the editor is creating a new person.</summary>
    private string? _editingPersonId;

    /// <summary>Photo path of the person being edited (not editable here, but preserved on save).</summary>
    private string? _editingPhotoPath;

    /// <summary>Draft id the editor was resumed from; deleted after a successful real save.</summary>
    private string? _currentDraftId;

    /// <summary>Chosen avatar color hex, or null for Auto (name-derived).</summary>
    private string? _editorAvatarHex;

    /// <summary>The merge source while the merge dialog is open.</summary>
    private PersonDto? _mergeSource;

    /// <summary>Guards programmatic editor-control updates from re-firing their handlers.</summary>
    private bool _syncingEditor;

    public ContactsPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<ContactsViewModel>();
    }

    /// <summary>
    /// Accepts the navigation parameter: "draft:&lt;draftId&gt;" resumes a person
    /// draft in the editor; any other non-empty string is a personId to select.
    /// Actual handling is deferred until the page has loaded (dialogs need a
    /// XamlRoot).
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string parameter && !string.IsNullOrWhiteSpace(parameter))
        {
            _pendingParameter = parameter;
            if (_loaded)
            {
                _ = HandlePendingParameterAsync();
            }
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        _initialized = true;
        _loaded = true;
        await HandlePendingParameterAsync();
    }

    /// <summary>Processes a stored navigation parameter, if any.</summary>
    private async Task HandlePendingParameterAsync()
    {
        var parameter = _pendingParameter;
        _pendingParameter = null;
        if (parameter == null)
        {
            return;
        }

        if (parameter.StartsWith("draft:", StringComparison.Ordinal))
        {
            await ResumeDraftAsync(parameter.Substring("draft:".Length));
        }
        else
        {
            await ViewModel.SelectPersonByIdAsync(parameter);
        }
    }

    /// <summary>
    /// Loads a person draft and reopens the editor prefilled from it. The
    /// dialog open is deferred via the dispatcher so it never races the page
    /// becoming visible.
    /// </summary>
    private async Task ResumeDraftAsync(string draftId)
    {
        var draft = await ViewModel.GetDraftAsync(draftId);
        var payload = draft?.GetPayload<PersonDraftPayload>();
        if (payload == null)
        {
            ViewModel.ErrorMessage = "This draft could not be opened. It may have been deleted or is damaged.";
            return;
        }

        DispatcherQueue.TryEnqueue(async () =>
        {
            PrepareEditorFromDraft(payload, draftId);
            await ShowPersonDialogAsync();
        });
    }

    /// <summary>Hides the event-count badge for people with no events.</summary>
    public static Visibility CountToVisibility(int count) =>
        count > 0 ? Visibility.Visible : Visibility.Collapsed;

    #region List handlers

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        if (SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.SortOption = tag switch
            {
                "recent" => PersonSortOption.RecentlyAdded,
                "events" => PersonSortOption.MostEvents,
                "updated" => PersonSortOption.RecentlyUpdated,
                _ => PersonSortOption.Name
            };
        }
    }

    private async void ContactsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedPerson != null)
        {
            PrepareEditorForEdit(ViewModel.SelectedPerson);
            await ShowPersonDialogAsync();
        }
    }

    private void LinkedEvents_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PersonEventSummary summary)
        {
            ViewModel.OpenEvent(summary.EventId);
        }
    }

    #endregion

    #region InfoBar handlers

    private void ErrorInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ViewModel.ErrorMessage = string.Empty;
    }

    private void InfoInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ViewModel.InfoMessage = string.Empty;
    }

    private void DuplicatesInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ViewModel.DismissDuplicates();
    }

    /// <summary>
    /// Opens the merge dialog for the surfaced duplicate pair: the second
    /// person is the merge source, the first is preselected as the target.
    /// </summary>
    private async void ReviewDuplicates_Click(object sender, RoutedEventArgs e)
    {
        var pair = ViewModel.FirstDuplicatePair;
        if (pair == null)
        {
            return;
        }

        await ViewModel.SelectPersonByIdAsync(pair.Second.PersonId);
        await OpenMergeDialogAsync(pair.Second, pair.First.PersonId);
    }

    #endregion

    #region Person editor dialog

    private async void AddPerson_Click(object sender, RoutedEventArgs e)
    {
        PrepareEditorForAdd();
        await ShowPersonDialogAsync();
    }

    private async void EditPerson_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPerson != null)
        {
            PrepareEditorForEdit(ViewModel.SelectedPerson);
            await ShowPersonDialogAsync();
        }
    }

    private async Task ShowPersonDialogAsync()
    {
        PersonDialog.XamlRoot = XamlRoot;
        await PersonDialog.ShowAsync();
    }

    /// <summary>Resets the editor for creating a new person.</summary>
    private void PrepareEditorForAdd()
    {
        _editingPersonId = null;
        _editingPhotoPath = null;
        _currentDraftId = null;
        PersonDialog.Title = "Add Person";
        SetEditorFields(
            name: string.Empty, nickname: null, relationship: null, email: null,
            phone: null, birthday: null, company: null, firstMetDate: null,
            notes: null, avatarColor: null, isFavorite: false);
    }

    /// <summary>Prefills the editor from an existing person.</summary>
    private void PrepareEditorForEdit(PersonDto person)
    {
        _editingPersonId = person.PersonId;
        _editingPhotoPath = person.PhotoPath;
        _currentDraftId = null;
        PersonDialog.Title = "Edit Person";
        SetEditorFields(
            person.Name, person.Nickname, person.Relationship, person.Email,
            person.Phone, person.Birthday, person.Company, person.FirstMetDate,
            person.Notes, person.AvatarColor, person.IsFavorite);
    }

    /// <summary>Prefills the editor from a resumed draft payload.</summary>
    private void PrepareEditorFromDraft(PersonDraftPayload payload, string draftId)
    {
        _editingPersonId = null;
        _editingPhotoPath = null;
        _currentDraftId = draftId;
        PersonDialog.Title = "Resume Person Draft";
        SetEditorFields(
            payload.Name, payload.Nickname, payload.Relationship, payload.Email,
            payload.Phone, payload.Birthday, payload.Company, payload.FirstMetDate,
            payload.Notes, payload.AvatarColor, payload.IsFavorite);
    }

    private void SetEditorFields(
        string name,
        string? nickname,
        string? relationship,
        string? email,
        string? phone,
        DateTime? birthday,
        string? company,
        DateTime? firstMetDate,
        string? notes,
        string? avatarColor,
        bool isFavorite)
    {
        EditorNameBox.Text = name;
        EditorNicknameBox.Text = nickname ?? string.Empty;
        EditorRelationshipCombo.SelectedItem = null;
        EditorRelationshipCombo.Text = relationship ?? string.Empty;
        EditorEmailBox.Text = email ?? string.Empty;
        EditorPhoneBox.Text = phone ?? string.Empty;
        EditorBirthdayPicker.Date = ToOffset(birthday);
        EditorCompanyBox.Text = company ?? string.Empty;
        EditorFirstMetPicker.Date = ToOffset(firstMetDate);
        EditorNotesBox.Text = notes ?? string.Empty;
        EditorFavoriteCheckBox.IsChecked = isFavorite;
        SetEditorAvatarColor(avatarColor);

        EditorValidationText.Text = string.Empty;
        EditorValidationText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Reflects an avatar color in the swatch grid. Auto (null) selects the
    /// Auto swatch; a palette color selects its swatch; an off-palette custom
    /// color clears the selection but is preserved unless the user picks a
    /// swatch.
    /// </summary>
    private void SetEditorAvatarColor(string? avatarColor)
    {
        _editorAvatarHex = string.IsNullOrWhiteSpace(avatarColor) ? null : avatarColor;

        _syncingEditor = true;
        try
        {
            if (_editorAvatarHex == null)
            {
                EditorColorGrid.SelectedItem = ViewModel.AvatarColorOptions[0]; // Auto
                return;
            }

            var match = ViewModel.AvatarColorOptions.FirstOrDefault(o =>
                o.Hex != null &&
                string.Equals(o.Hex, _editorAvatarHex, StringComparison.OrdinalIgnoreCase));
            EditorColorGrid.SelectedItem = match; // null clears selection for custom colors
        }
        finally
        {
            _syncingEditor = false;
        }
    }

    private void EditorColorGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingEditor)
        {
            return;
        }

        if (EditorColorGrid.SelectedItem is AvatarColorOption option)
        {
            _editorAvatarHex = option.Hex; // null when the Auto swatch was picked
        }
    }

    private void ClearBirthday_Click(object sender, RoutedEventArgs e)
    {
        EditorBirthdayPicker.Date = null;
    }

    private void ClearFirstMet_Click(object sender, RoutedEventArgs e)
    {
        EditorFirstMetPicker.Date = null;
    }

    /// <summary>Builds the DTO the editor currently describes.</summary>
    private PersonDto BuildPersonFromEditor()
    {
        return new PersonDto
        {
            PersonId = _editingPersonId ?? Guid.NewGuid().ToString(),
            Name = EditorNameBox.Text?.Trim() ?? string.Empty,
            Nickname = NullIfWhiteSpace(EditorNicknameBox.Text),
            Relationship = NullIfWhiteSpace(EditorRelationshipCombo.Text),
            Email = NullIfWhiteSpace(EditorEmailBox.Text),
            Phone = NullIfWhiteSpace(EditorPhoneBox.Text),
            Birthday = EditorBirthdayPicker.Date?.Date,
            Company = NullIfWhiteSpace(EditorCompanyBox.Text),
            Notes = NullIfWhiteSpace(EditorNotesBox.Text),
            PhotoPath = _editingPhotoPath,
            AvatarColor = _editorAvatarHex,
            IsFavorite = EditorFavoriteCheckBox.IsChecked == true,
            FirstMetDate = EditorFirstMetPicker.Date?.Date
        };
    }

    /// <summary>Builds the draft payload the editor currently describes.</summary>
    private PersonDraftPayload BuildDraftPayloadFromEditor()
    {
        return new PersonDraftPayload
        {
            Name = EditorNameBox.Text?.Trim() ?? string.Empty,
            Nickname = NullIfWhiteSpace(EditorNicknameBox.Text),
            Relationship = NullIfWhiteSpace(EditorRelationshipCombo.Text),
            Email = NullIfWhiteSpace(EditorEmailBox.Text),
            Phone = NullIfWhiteSpace(EditorPhoneBox.Text),
            Birthday = EditorBirthdayPicker.Date?.Date,
            Company = NullIfWhiteSpace(EditorCompanyBox.Text),
            Notes = NullIfWhiteSpace(EditorNotesBox.Text),
            AvatarColor = _editorAvatarHex,
            IsFavorite = EditorFavoriteCheckBox.IsChecked == true,
            FirstMetDate = EditorFirstMetPicker.Date?.Date
        };
    }

    /// <summary>
    /// Save: validates the name inline, then creates/updates the person. On
    /// success a resumed draft is deleted; on failure the dialog stays open
    /// with the error shown inline.
    /// </summary>
    private async void PersonDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = EditorNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowEditorValidation("A name is required.");
            args.Cancel = true;
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            var person = BuildPersonFromEditor();
            var error = await ViewModel.SavePersonAsync(person, isNew: _editingPersonId == null);
            if (error != null)
            {
                ShowEditorValidation(error);
                args.Cancel = true;
                return;
            }

            if (_currentDraftId != null)
            {
                await ViewModel.DeletePersonDraftAsync(_currentDraftId);
                _currentDraftId = null;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// Save as Draft: serializes the editor state as a person draft and closes
    /// the dialog. On failure the dialog stays open so nothing is lost.
    /// </summary>
    private async void PersonDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var payload = BuildDraftPayloadFromEditor();
            var json = JsonSerializer.Serialize(payload);
            var saved = await ViewModel.SavePersonDraftAsync(payload.Name, json, _currentDraftId);
            if (!saved)
            {
                args.Cancel = true;
                return;
            }

            _currentDraftId = null;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ShowEditorValidation(string message)
    {
        EditorValidationText.Text = message;
        EditorValidationText.Visibility = Visibility.Visible;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset? ToOffset(DateTime? date) =>
        date.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(date.Value, DateTimeKind.Unspecified)) : null;

    #endregion

    #region Merge dialog

    private async void MergePerson_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPerson != null)
        {
            await OpenMergeDialogAsync(ViewModel.SelectedPerson, preselectTargetId: null);
        }
    }

    /// <summary>
    /// Opens the merge dialog for a source person, optionally preselecting the
    /// merge target (used by the duplicates Review action).
    /// </summary>
    private async Task OpenMergeDialogAsync(PersonDto source, string? preselectTargetId)
    {
        _mergeSource = source;
        ViewModel.PrepareMergeCandidates(source);

        if (ViewModel.MergeCandidates.Count == 0)
        {
            _mergeSource = null;
            ViewModel.InfoMessage = "There is no one else to merge with yet.";
            return;
        }

        MergeSourceText.Text = $"Merge '{source.DisplayName}' into another contact.";
        MergeWarningBar.Message =
            $"All event links from '{source.DisplayName}' move to the person you choose, " +
            $"and '{source.DisplayName}' is deleted.";
        MergeTargetCombo.SelectedItem = preselectTargetId == null
            ? null
            : ViewModel.MergeCandidates.FirstOrDefault(p => p.PersonId == preselectTargetId);

        MergeDialog.XamlRoot = XamlRoot;
        await MergeDialog.ShowAsync();
    }

    private async void MergeDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_mergeSource == null || MergeTargetCombo.SelectedItem is not PersonDto target)
        {
            args.Cancel = true; // No target chosen yet; keep the dialog open.
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.MergePersonsAsync(_mergeSource.PersonId, target.PersonId);
        }
        finally
        {
            _mergeSource = null;
            deferral.Complete();
        }
    }

    #endregion

    #region Delete dialog

    private async void DeletePerson_Click(object sender, RoutedEventArgs e)
    {
        var person = ViewModel.SelectedPerson;
        if (person == null)
        {
            return;
        }

        var eventNote = person.EventCount switch
        {
            0 => "No events are linked to this person.",
            1 => "1 event is linked to this person; the link will be removed (the event itself is kept).",
            _ => $"{person.EventCount} events are linked to this person; the links will be removed (the events themselves are kept)."
        };
        DeletePersonText.Text =
            $"Are you sure you want to delete '{person.DisplayName}'? {eventNote} This cannot be undone.";

        DeletePersonDialog.XamlRoot = XamlRoot;
        await DeletePersonDialog.ShowAsync();
    }

    private async void DeletePersonDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.DeleteSelectedPersonAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    #endregion
}
