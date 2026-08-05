using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Core.DTOs;

/// <summary>
/// DTO for displaying and managing pending events awaiting review.
/// </summary>
public partial class PendingEventDto : ObservableObject
{
    [ObservableProperty]
    private string _pendingEventId = string.Empty;

    [ObservableProperty]
    private string _queueId = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private DateTime _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private string _category = "other";

    /// <summary>
    /// How much of <see cref="StartDate"/> to believe, as assigned by
    /// extraction and correctable in the Review edit panel.
    /// </summary>
    [ObservableProperty]
    private DatePrecision _datePrecision = DatePrecision.Day;

    /// <summary>
    /// Pass-through of the stored explicit lower bound so review edits do not
    /// wipe it (not editable in the UI).
    /// </summary>
    public DateTime? EarliestPossible { get; set; }

    /// <summary>
    /// Pass-through of the stored explicit upper bound so review edits do not
    /// wipe it (not editable in the UI).
    /// </summary>
    public DateTime? LatestPossible { get; set; }

    [ObservableProperty]
    private double _confidenceScore;

    [ObservableProperty]
    private bool _isApproved;

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private string? _extractedData;

    [ObservableProperty]
    private string? _transcript;

    [ObservableProperty]
    private string? _audioFilePath;

    [ObservableProperty]
    private bool _isSelected;

    // Commands for UI actions
    public IRelayCommand? ApproveCommand { get; set; }
    public IRelayCommand? RejectCommand { get; set; }
    public IRelayCommand? EditCommand { get; set; }

    // Display properties
    public string StartDateDisplay => StartDate.ToString("MMM d, yyyy");

    public string EndDateDisplay => EndDate.HasValue ? EndDate.Value.ToString("MMM d, yyyy") : "N/A";

    public string DurationDisplay
    {
        get
        {
            if (!EndDate.HasValue || EndDate.Value == StartDate)
                return "Single day event";

            var duration = EndDate.Value - StartDate;
            if (duration.TotalDays < 1)
                return $"{duration.TotalHours:F1} hours";
            else if (duration.TotalDays == 1)
                return "1 day";
            else
                return $"{duration.TotalDays:F0} days";
        }
    }

    /// <summary>
    /// Short label for the assigned precision ("Day", "Season", ...), shown as
    /// a chip on the review card.
    /// </summary>
    public string DatePrecisionLabel => DateDisplay.GetPrecisionLabel(DatePrecision);

    /// <summary>
    /// Precision-honest human form of the date ("Summer 1998"); live preview
    /// in the review edit panel.
    /// </summary>
    public string DatePrecisionDisplay => DateDisplay.FormatPrecise(StartDate, DatePrecision, EndDate);

    /// <summary>
    /// String bridge over <see cref="DatePrecision"/> ("exact".."unknown") for
    /// the review edit panel's ComboBox (SelectedValuePath="Tag" pattern, same
    /// as the category combo). Unrecognized writes fall back to Day.
    /// </summary>
    public string DatePrecisionValue
    {
        get => DatePrecisionParser.ToStringValue(DatePrecision);
        set => DatePrecision = DatePrecisionParser.Parse(value);
    }

    public string ConfidenceDisplay => $"{ConfidenceScore:P0}";

    public SolidColorBrush ConfidenceColor
    {
        get
        {
            if (ConfidenceScore >= 0.8)
                return new SolidColorBrush(Colors.Green);
            else if (ConfidenceScore >= 0.6)
                return new SolidColorBrush(Colors.Orange);
            else
                return new SolidColorBrush(Colors.Red);
        }
    }

    public string CategoryIcon => Category?.ToLowerInvariant() switch
    {
        // Canonical categories (EventCategory.AllCategories)
        "milestone" => "\uE735", // Flag
        "work" => "\uE821", // Briefcase
        "education" => "\uE7BE", // Education
        "relationship" => "\uE77B", // People
        "travel" => "\uE804", // Airplane
        "achievement" => "\uE734", // Trophy
        "challenge" => "\uE7BA", // Alert
        "era" => "\uE787", // Clock
        // Legacy values from older extraction prompts
        "health" => "\uE95E", // Health
        "social" => "\uE716", // People
        "personal" => "\uE77B", // Contact
        "family" => "\uE728", // Home
        _ => "\uE8EB" // Calendar
    };

    /// <summary>True when the event has an end date. Bind through BoolToVisibilityConverter in XAML.</summary>
    public bool HasEndDate => EndDate.HasValue;

    /// <summary>True when the event spans more than one day. Bind through BoolToVisibilityConverter in XAML.</summary>
    public bool IsLongEvent => EndDate.HasValue && (EndDate.Value - StartDate).TotalDays > 1;

    /// <summary>True when a source transcript is available for display.</summary>
    public bool HasTranscript => !string.IsNullOrWhiteSpace(Transcript);

    /// <summary>
    /// Names of people mentioned in the extracted event, parsed from the
    /// extraction payload (PeopleDetails names, falling back to the flat
    /// People list). Empty when the payload is missing or malformed.
    /// </summary>
    public List<string> PeopleNames { get; private set; } = new();

    /// <summary>True when at least one person was extracted for this event.</summary>
    public bool HasPeople => PeopleNames.Count > 0;

    /// <summary>Comma-separated people names for display.</summary>
    public string PeopleDisplay => string.Join(", ", PeopleNames);

    /// <summary>
    /// DateTimeOffset bridge over <see cref="StartDate"/> for CalendarDatePicker.Date bindings.
    /// Date-only semantics: the time component is discarded on write.
    /// </summary>
    public DateTimeOffset? StartDateOffset
    {
        get => new DateTimeOffset(DateTime.SpecifyKind(StartDate.Date, DateTimeKind.Local));
        set
        {
            if (value.HasValue)
            {
                StartDate = value.Value.Date;
            }
        }
    }

    /// <summary>
    /// DateTimeOffset bridge over <see cref="EndDate"/> for CalendarDatePicker.Date bindings.
    /// Null clears the end date; date-only semantics.
    /// </summary>
    public DateTimeOffset? EndDateOffset
    {
        get => EndDate.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(EndDate.Value.Date, DateTimeKind.Local))
            : null;
        set => EndDate = value?.Date;
    }

    // Property change notifications
    partial void OnStartDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(StartDateDisplay));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(IsLongEvent));
        OnPropertyChanged(nameof(StartDateOffset));
        OnPropertyChanged(nameof(DatePrecisionDisplay));
    }

    partial void OnEndDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(EndDateDisplay));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(HasEndDate));
        OnPropertyChanged(nameof(IsLongEvent));
        OnPropertyChanged(nameof(EndDateOffset));
        OnPropertyChanged(nameof(DatePrecisionDisplay));
    }

    partial void OnDatePrecisionChanged(DatePrecision value)
    {
        OnPropertyChanged(nameof(DatePrecisionLabel));
        OnPropertyChanged(nameof(DatePrecisionDisplay));
        OnPropertyChanged(nameof(DatePrecisionValue));
    }

    partial void OnTranscriptChanged(string? value)
    {
        OnPropertyChanged(nameof(HasTranscript));
    }

    partial void OnConfidenceScoreChanged(double value)
    {
        OnPropertyChanged(nameof(ConfidenceDisplay));
        OnPropertyChanged(nameof(ConfidenceColor));
    }

    partial void OnCategoryChanged(string value)
    {
        OnPropertyChanged(nameof(CategoryIcon));
    }

    /// <summary>
    /// Creates a DTO from a PendingEvent entity.
    /// </summary>
    public static PendingEventDto FromPendingEvent(PendingEvent pendingEvent)
    {
        return new PendingEventDto
        {
            PendingEventId = pendingEvent.PendingEventId,
            QueueId = pendingEvent.QueueId ?? string.Empty,
            Title = pendingEvent.Title,
            Description = pendingEvent.Description,
            StartDate = pendingEvent.StartDate,
            EndDate = pendingEvent.EndDate,
            DatePrecision = pendingEvent.DatePrecision,
            EarliestPossible = pendingEvent.EarliestPossible,
            LatestPossible = pendingEvent.LatestPossible,
            Category = pendingEvent.Category,
            ConfidenceScore = pendingEvent.ConfidenceScore,
            IsApproved = pendingEvent.IsApproved,
            CreatedAt = pendingEvent.CreatedAt,
            ExtractedData = pendingEvent.ExtractedData,
            Transcript = pendingEvent.Transcript,
            AudioFilePath = pendingEvent.AudioFilePath,
            PeopleNames = ParsePeopleNames(pendingEvent.ExtractedData)
        };
    }

    // Reused across parses; building JsonSerializerOptions per call is expensive.
    private static readonly JsonSerializerOptions ExtractedDataJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parses distinct, trimmed people names out of the extraction payload.
    /// Returns an empty list when the payload is null, empty, or malformed.
    /// </summary>
    private static List<string> ParsePeopleNames(string? extractedData)
    {
        if (string.IsNullOrWhiteSpace(extractedData))
        {
            return new List<string>();
        }

        try
        {
            var extracted = JsonSerializer.Deserialize<ExtractedEvent>(extractedData, ExtractedDataJsonOptions);
            if (extracted == null)
            {
                return new List<string>();
            }

            var detailNames = DistinctPeopleNames(extracted.PeopleDetails?.Select(d => d.Name));
            return detailNames.Count > 0
                ? detailNames
                : DistinctPeopleNames(extracted.People);
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static List<string> DistinctPeopleNames(IEnumerable<string>? names)
    {
        return (names ?? Enumerable.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Converts DTO back to PendingEvent entity.
    /// </summary>
    public PendingEvent ToPendingEvent()
    {
        return new PendingEvent
        {
            PendingEventId = PendingEventId,
            QueueId = QueueId,
            Title = Title,
            Description = Description,
            StartDate = StartDate,
            EndDate = EndDate,
            DatePrecision = DatePrecision,
            EarliestPossible = EarliestPossible,
            LatestPossible = LatestPossible,
            Category = string.IsNullOrWhiteSpace(Category) ? "other" : Category,
            ConfidenceScore = ConfidenceScore,
            IsApproved = IsApproved,
            CreatedAt = CreatedAt,
            ExtractedData = ExtractedData ?? string.Empty,
            Transcript = Transcript,
            AudioFilePath = AudioFilePath
        };
    }
}
