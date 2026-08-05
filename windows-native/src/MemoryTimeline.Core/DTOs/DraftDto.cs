using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Core.DTOs;

/// <summary>
/// DTO for displaying and resuming saved editor drafts (events, eras, people).
/// The editor state travels as JSON in <see cref="PayloadJson"/>; use
/// <see cref="GetPayload{T}"/> with the matching payload class.
/// </summary>
public partial class DraftDto : ObservableObject
{
    [ObservableProperty]
    private string _draftId = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _draftType = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _payloadJson = "{}";

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private DateTime _updatedAt;

    /// <summary>
    /// Human-readable draft type: "Event", "Era", or "Person" (falls back to
    /// the raw <see cref="DraftType"/> for unknown values).
    /// </summary>
    public string TypeDisplay => DraftType switch
    {
        DraftTypes.Event => "Event",
        DraftTypes.Era => "Era",
        DraftTypes.Person => "Person",
        _ => DraftType
    };

    /// <summary>
    /// Segoe Fluent icon glyph for the draft type: calendar for events,
    /// history for eras, contact for people.
    /// </summary>
    public string TypeGlyph => DraftType switch
    {
        DraftTypes.Event => "\uE8EB",  // calendar
        DraftTypes.Era => "\uE81C",    // history
        DraftTypes.Person => "\uE77B", // contact
        _ => "\uE70B"                  // drafts (fallback)
    };

    /// <summary>
    /// Last-updated timestamp formatted as "MMM d, yyyy h:mm tt".
    /// </summary>
    public string UpdatedDisplay => UpdatedAt.ToString("MMM d, yyyy h:mm tt");

    partial void OnDraftTypeChanged(string value)
    {
        OnPropertyChanged(nameof(TypeDisplay));
        OnPropertyChanged(nameof(TypeGlyph));
    }

    partial void OnUpdatedAtChanged(DateTime value)
    {
        OnPropertyChanged(nameof(UpdatedDisplay));
    }

    /// <summary>
    /// Creates a DTO from a <see cref="Draft"/> entity.
    /// </summary>
    public static DraftDto FromDraft(Draft draft)
    {
        return new DraftDto
        {
            DraftId = draft.DraftId,
            DraftType = draft.DraftType,
            Title = draft.Title,
            PayloadJson = draft.PayloadJson,
            CreatedAt = draft.CreatedAt,
            UpdatedAt = draft.UpdatedAt
        };
    }

    /// <summary>
    /// Converts the DTO back to a <see cref="Draft"/> entity.
    /// </summary>
    public Draft ToDraft()
    {
        return new Draft
        {
            DraftId = DraftId,
            DraftType = DraftType,
            Title = Title,
            PayloadJson = PayloadJson,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }

    /// <summary>
    /// Deserializes <see cref="PayloadJson"/> as <typeparamref name="T"/>.
    /// Returns null (never throws) when the payload is empty or malformed.
    /// </summary>
    public T? GetPayload<T>() where T : class
    {
        if (string.IsNullOrWhiteSpace(PayloadJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(PayloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Serialized editor state for an event draft (<see cref="DraftTypes.Event"/>).
/// </summary>
public sealed class EventDraftPayload
{
    /// <summary>Event title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Event description.</summary>
    public string? Description { get; set; }

    /// <summary>Event start date.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Event end date.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Date precision of the start date. Older drafts without this property
    /// keep the initializer default (Day).
    /// </summary>
    public DatePrecision DatePrecision { get; set; } = DatePrecision.Day;

    /// <summary>Event category.</summary>
    public string? Category { get; set; }

    /// <summary>Linked era id.</summary>
    public string? EraId { get; set; }

    /// <summary>Free-form location text.</summary>
    public string? Location { get; set; }

    /// <summary>Tag names attached to the event.</summary>
    public List<string> TagNames { get; set; } = new();

    /// <summary>Ids of existing persons linked to the event.</summary>
    public List<string> PersonIds { get; set; } = new();

    /// <summary>Names of to-be-created persons linked to the event.</summary>
    public List<string> PersonNames { get; set; } = new();

    /// <summary>
    /// Absolute source paths of photos staged in the dialog ("N photos will
    /// be attached on save") when the draft was saved, so resuming the draft
    /// re-stages them instead of silently dropping them. Nullable so drafts
    /// saved before this property existed still deserialize (as null).
    /// </summary>
    public List<string>? PendingPhotoPaths { get; set; }
}

/// <summary>
/// Serialized editor state for an era draft (<see cref="DraftTypes.Era"/>).
/// </summary>
public sealed class EraDraftPayload
{
    /// <summary>Era name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Era subtitle.</summary>
    public string? Subtitle { get; set; }

    /// <summary>Era start date.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Era end date.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Era category id.</summary>
    public string? CategoryId { get; set; }

    /// <summary>Era color as a hex string.</summary>
    public string? ColorCode { get; set; }

    /// <summary>Optional per-era color override as a hex string.</summary>
    public string? ColorOverride { get; set; }

    /// <summary>Era description.</summary>
    public string? Description { get; set; }

    /// <summary>Free-form notes.</summary>
    public string? Notes { get; set; }

    /// <summary>Era tags.</summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Serialized editor state for a person draft (<see cref="DraftTypes.Person"/>).
/// </summary>
public sealed class PersonDraftPayload
{
    /// <summary>Person name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Nickname.</summary>
    public string? Nickname { get; set; }

    /// <summary>Relationship to the user.</summary>
    public string? Relationship { get; set; }

    /// <summary>Email address.</summary>
    public string? Email { get; set; }

    /// <summary>Phone number.</summary>
    public string? Phone { get; set; }

    /// <summary>Birthday.</summary>
    public DateTime? Birthday { get; set; }

    /// <summary>Company or organization.</summary>
    public string? Company { get; set; }

    /// <summary>Free-form notes.</summary>
    public string? Notes { get; set; }

    /// <summary>Avatar color as a hex string.</summary>
    public string? AvatarColor { get; set; }

    /// <summary>Whether the person is marked as a favorite.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Date the user first met the person.</summary>
    public DateTime? FirstMetDate { get; set; }
}
