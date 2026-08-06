using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryTimeline.Controls;

/// <summary>
/// One revision in the history list (display projection).
/// </summary>
public sealed class RevisionListItem
{
    public EventRevision Revision { get; init; } = null!;

    /// <summary>Parsed snapshot, or null when the stored JSON is unreadable.</summary>
    public EventSnapshot? Snapshot { get; init; }

    public string KindGlyph { get; init; } = "";
    public string KindLabel { get; init; } = string.Empty;
    public string RevisedAtDisplay { get; init; } = string.Empty;
    public string ChangedFieldsDisplay { get; init; } = string.Empty;
    public bool HasChangedFields => !string.IsNullOrEmpty(ChangedFieldsDisplay);
}

/// <summary>
/// One row of the field-level diff panel: the selected version's value vs the
/// event's current value. Changed rows render SemiBold.
/// </summary>
public sealed class RevisionDiffRow
{
    public string FieldName { get; init; } = string.Empty;
    public string OldValue { get; init; } = string.Empty;
    public string CurrentValue { get; init; } = string.Empty;
    public bool Changed { get; init; }

    // Fully qualified to sidestep the Windows.UI.Text / Microsoft.UI.Text
    // FontWeights ambiguity: TextBlock.FontWeight is Windows.UI.Text.FontWeight
    // in WinUI 3, produced by Microsoft.UI.Text.FontWeights.
    public Windows.UI.Text.FontWeight ValueWeight => Changed
        ? Microsoft.UI.Text.FontWeights.SemiBold
        : Microsoft.UI.Text.FontWeights.Normal;
}

/// <summary>
/// Event history dialog (F12): lists an event's revisions (newest first) with
/// a field-level diff of the selected version against the current event, and
/// restores a selected version via <see cref="IRevisionService"/> after a
/// two-step in-dialog confirmation. The timeline refreshes itself through the
/// <see cref="EventUpdatedMessage"/> the restore publishes.
/// Construct with the event id/title, set <c>XamlRoot</c>, then <c>ShowAsync</c>.
/// </summary>
public sealed partial class EventHistoryDialog : ContentDialog
{
    private readonly string _eventId;
    private readonly IRevisionService _revisionService;
    private readonly IEventService _eventService;
    private readonly ILogger<EventHistoryDialog> _logger;

    private EventSnapshot? _currentSnapshot;
    private bool _confirmPending;

    public EventHistoryDialog(string eventId, string? eventTitle)
    {
        InitializeComponent();

        _eventId = eventId;
        _revisionService = App.Current.Services.GetRequiredService<IRevisionService>();
        _eventService = App.Current.Services.GetRequiredService<IEventService>();
        _logger = App.Current.Services.GetRequiredService<ILogger<EventHistoryDialog>>();

        Title = string.IsNullOrWhiteSpace(eventTitle) ? "Event history" : $"History — {eventTitle}";
        IsPrimaryButtonEnabled = false;

        Opened += EventHistoryDialog_Opened;
        PrimaryButtonClick += EventHistoryDialog_PrimaryButtonClick;
    }

    private async void EventHistoryDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _currentSnapshot = await BuildCurrentSnapshotAsync();

            var revisions = await _revisionService.GetHistoryAsync(_eventId);
            var items = revisions.Select(ToListItem).ToList();

            RevisionList.ItemsSource = items;
            EmptyHistoryText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoSelectionText.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            DiffList.ItemsSource = null;
            IsPrimaryButtonEnabled = false;
            _confirmPending = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading revision history for event {EventId}", _eventId);
            ShowStatus(InfoBarSeverity.Error, "Could not load history", ex.Message);
        }
    }

    private async Task<EventSnapshot?> BuildCurrentSnapshotAsync()
    {
        var current = await _eventService.GetEventWithDetailsAsync(_eventId);
        if (current == null)
        {
            return null;
        }

        return EventRevisionWriter.BuildSnapshot(
            current,
            current.EventTags.Select(et => et.Tag?.TagName ?? string.Empty),
            current.EventPeople.Select(ep => ep.Person?.Name ?? string.Empty),
            current.EventLocations.Select(el => el.Location?.Name ?? string.Empty));
    }

    private void RevisionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _confirmPending = false;

        if (RevisionList.SelectedItem is not RevisionListItem item)
        {
            DiffList.ItemsSource = null;
            NoSelectionText.Visibility = Visibility.Visible;
            IsPrimaryButtonEnabled = false;
            return;
        }

        NoSelectionText.Visibility = Visibility.Collapsed;

        if (item.Snapshot == null)
        {
            DiffList.ItemsSource = null;
            IsPrimaryButtonEnabled = false;
            ShowStatus(InfoBarSeverity.Warning, "Unreadable version",
                "This version's snapshot could not be parsed, so it cannot be compared or restored.");
            return;
        }

        HistoryStatusBar.IsOpen = false;
        DiffList.ItemsSource = BuildDiffRows(item.Snapshot, _currentSnapshot);
        IsPrimaryButtonEnabled = _currentSnapshot != null;
    }

    /// <summary>
    /// Two-step confirm without a nested dialog (WinUI allows only one
    /// ContentDialog at a time): the first click arms the confirmation, the
    /// second click performs the restore. Both keep the dialog open.
    /// </summary>
    private async void EventHistoryDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;

        if (RevisionList.SelectedItem is not RevisionListItem item || item.Snapshot == null)
        {
            return;
        }

        if (!_confirmPending)
        {
            _confirmPending = true;
            ShowStatus(InfoBarSeverity.Warning, "Restore this version?",
                "Restoring overwrites the event's current values (they are preserved as a new history entry). " +
                "Click \"Restore this version\" again to confirm.");
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            IsPrimaryButtonEnabled = false;
            await _revisionService.RestoreRevisionAsync(_eventId, item.Revision.RevisionId);

            // Reload: the restore appended a new Restored revision and changed
            // the current state; the timeline refreshed via EventUpdatedMessage.
            await LoadAsync();
            ShowStatus(InfoBarSeverity.Success, "Version restored",
                "The event was restored and the timeline refreshed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring revision for event {EventId}", _eventId);
            ShowStatus(InfoBarSeverity.Error, "Restore failed", ex.Message);
            IsPrimaryButtonEnabled = true;
        }
        finally
        {
            _confirmPending = false;
            deferral.Complete();
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        HistoryStatusBar.Severity = severity;
        HistoryStatusBar.Title = title;
        HistoryStatusBar.Message = message;
        HistoryStatusBar.IsOpen = true;
    }

    private RevisionListItem ToListItem(EventRevision revision)
    {
        var (glyph, label) = revision.RevisionKind switch
        {
            RevisionKind.Created => ("\uE710", "Created"),    // Add
            RevisionKind.Edited => ("\uE70F", "Edited"),      // Edit
            RevisionKind.Approved => ("\uE8FB", "Approved"),  // Accept
            RevisionKind.Imported => ("\uE8B5", "Imported"),  // Import
            RevisionKind.Merged => ("\uE8C8", "Merged"),      // Merge
            RevisionKind.Restored => ("\uE777", "Restored"),  // UpdateRestore
            _ => ("\uE946", revision.RevisionKind.ToString()) // Info
        };

        var changed = string.IsNullOrWhiteSpace(revision.ChangedFieldsCsv)
            ? string.Empty
            : "Changed: " + revision.ChangedFieldsCsv.Replace("_", " ").Replace(",", ", ");

        return new RevisionListItem
        {
            Revision = revision,
            Snapshot = _revisionService.DeserializeSnapshot(revision),
            KindGlyph = glyph,
            KindLabel = label,
            RevisedAtDisplay = revision.RevisedAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt"),
            ChangedFieldsDisplay = changed
        };
    }

    private static List<RevisionDiffRow> BuildDiffRows(EventSnapshot old, EventSnapshot? current)
    {
        RevisionDiffRow Row(string name, string oldValue, string? currentValue)
        {
            var currentDisplay = currentValue ?? "(event deleted)";
            return new RevisionDiffRow
            {
                FieldName = name,
                OldValue = oldValue,
                CurrentValue = currentDisplay,
                Changed = !string.Equals(oldValue, currentDisplay, StringComparison.Ordinal)
            };
        }

        static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
        static string Date(DateTime? value) => value?.ToString("MMM d, yyyy") ?? "—";
        static string Names(List<string> values) => values.Count == 0 ? "—" : string.Join(", ", values);

        return new List<RevisionDiffRow>
        {
            Row("Title", Text(old.Title), current == null ? null : Text(current.Title)),
            Row("Start date", Date(old.StartDate), current == null ? null : Date(current.StartDate)),
            Row("End date", Date(old.EndDate), current == null ? null : Date(current.EndDate)),
            Row("Category", Text(old.Category), current == null ? null : Text(current.Category)),
            Row("Description", Text(old.Description), current == null ? null : Text(current.Description)),
            Row("Location", Text(old.Location), current == null ? null : Text(current.Location)),
            Row("Precision", old.DatePrecision.ToString(), current?.DatePrecision.ToString()),
            Row("Earliest", Date(old.EarliestPossible), current == null ? null : Date(current.EarliestPossible)),
            Row("Latest", Date(old.LatestPossible), current == null ? null : Date(current.LatestPossible)),
            Row("Tags", Names(old.Tags), current == null ? null : Names(current.Tags)),
            Row("People", Names(old.People), current == null ? null : Names(current.People)),
            Row("Locations", Names(old.Locations), current == null ? null : Names(current.Locations)),
        };
    }
}
