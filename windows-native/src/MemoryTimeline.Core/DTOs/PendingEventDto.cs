using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
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
        "milestone" => "\uE735", // Flag
        "work" => "\uE821", // Briefcase
        "education" => "\uE7BE", // Education
        "health" => "\uE95E", // Health
        "travel" => "\uE804", // Airplane
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
    }

    partial void OnEndDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(EndDateDisplay));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(HasEndDate));
        OnPropertyChanged(nameof(IsLongEvent));
        OnPropertyChanged(nameof(EndDateOffset));
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
            Category = pendingEvent.Category,
            ConfidenceScore = pendingEvent.ConfidenceScore,
            IsApproved = pendingEvent.IsApproved,
            CreatedAt = pendingEvent.CreatedAt,
            ExtractedData = pendingEvent.ExtractedData,
            Transcript = pendingEvent.Transcript,
            AudioFilePath = pendingEvent.AudioFilePath
        };
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
