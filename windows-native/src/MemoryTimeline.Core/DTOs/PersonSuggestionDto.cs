using CommunityToolkit.Mvvm.ComponentModel;

namespace MemoryTimeline.Core.DTOs;

/// <summary>
/// Kind of person suggestion computed from an extracted pending event.
/// </summary>
public enum PersonSuggestionKind
{
    /// <summary>The name matches no existing contact; applying creates a new person.</summary>
    NewPerson,

    /// <summary>The name matches an existing contact and there is nothing new to add.</summary>
    KnownPerson,

    /// <summary>The name matches an existing contact and the extraction carries details that contact lacks.</summary>
    UpdateDetails
}

/// <summary>
/// A create/update suggestion for one person mentioned in a pending event,
/// shown in the Review queue with one-click apply.
/// </summary>
public partial class PersonSuggestionDto : ObservableObject
{
    [ObservableProperty]
    private string _pendingEventId = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _matchedPersonId;

    [ObservableProperty]
    private string? _matchedPersonName;

    [ObservableProperty]
    private PersonSuggestionKind _kind;

    [ObservableProperty]
    private string? _suggestedRelationship;

    [ObservableProperty]
    private string? _suggestedDetails;

    [ObservableProperty]
    private bool _isApplied;

    /// <summary>
    /// Short label for the suggestion kind.
    /// </summary>
    public string KindDisplay => Kind switch
    {
        PersonSuggestionKind.NewPerson => "New person",
        PersonSuggestionKind.KnownPerson => "Known",
        PersonSuggestionKind.UpdateDetails => "Update details",
        _ => Kind.ToString()
    };

    /// <summary>
    /// Segoe Fluent glyph for the suggestion kind (add-friend / check / edit).
    /// </summary>
    public string KindGlyph => Kind switch
    {
        PersonSuggestionKind.NewPerson => "\uE8FA",
        PersonSuggestionKind.KnownPerson => "\uE73E",
        PersonSuggestionKind.UpdateDetails => "\uE70F",
        _ => "\uE77B"
    };

    /// <summary>
    /// Natural-language summary of what applying this suggestion will do.
    /// </summary>
    public string Summary
    {
        get
        {
            switch (Kind)
            {
                case PersonSuggestionKind.NewPerson:
                    return string.IsNullOrWhiteSpace(SuggestedRelationship)
                        ? "Will be added as a new contact"
                        : $"Will be added as a new contact ({SuggestedRelationship})";

                case PersonSuggestionKind.KnownPerson:
                    return $"Matches existing contact '{MatchedPersonName ?? Name}'";

                case PersonSuggestionKind.UpdateDetails:
                    var target = MatchedPersonName ?? Name;
                    var hasRelationship = !string.IsNullOrWhiteSpace(SuggestedRelationship);
                    var hasDetails = !string.IsNullOrWhiteSpace(SuggestedDetails);

                    if (hasRelationship && hasDetails)
                        return $"Adds relationship '{SuggestedRelationship}' and details to '{target}'";
                    if (hasRelationship)
                        return $"Adds relationship '{SuggestedRelationship}' to '{target}'";
                    if (hasDetails)
                        return $"Adds details to '{target}'";
                    return $"Updates details for '{target}'";

                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>
    /// True when the suggestion can still be applied (not yet applied, and not a
    /// pure known-person match which has nothing to apply).
    /// </summary>
    public bool CanApply => !IsApplied && Kind != PersonSuggestionKind.KnownPerson;

    partial void OnKindChanged(PersonSuggestionKind value)
    {
        OnPropertyChanged(nameof(KindDisplay));
        OnPropertyChanged(nameof(KindGlyph));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanApply));
    }

    partial void OnIsAppliedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApply));
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnMatchedPersonNameChanged(string? value)
    {
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnSuggestedRelationshipChanged(string? value)
    {
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnSuggestedDetailsChanged(string? value)
    {
        OnPropertyChanged(nameof(Summary));
    }
}
