namespace MemoryTimeline.Core.Services;

/// <summary>
/// Published via <c>CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default</c>
/// after an event is successfully created, so the timeline (and other views) can
/// refresh without renavigation.
/// </summary>
public sealed record EventCreatedMessage(string EventId, DateTime StartDate);

/// <summary>
/// Published via <c>WeakReferenceMessenger.Default</c> after a person is
/// successfully created.
/// </summary>
public sealed record PersonCreatedMessage(string PersonId);

/// <summary>
/// Published via <c>WeakReferenceMessenger.Default</c> after a person is
/// successfully updated (including favorite toggles).
/// </summary>
public sealed record PersonUpdatedMessage(string PersonId);

/// <summary>
/// Published via <c>WeakReferenceMessenger.Default</c> after a person is
/// successfully deleted.
/// </summary>
public sealed record PersonDeletedMessage(string PersonId);

/// <summary>
/// Published via <c>WeakReferenceMessenger.Default</c> after the source person
/// has been merged into the target person (source deleted).
/// </summary>
public sealed record PersonsMergedMessage(string SourcePersonId, string TargetPersonId);

/// <summary>
/// Published via <c>WeakReferenceMessenger.Default</c> after a draft is saved
/// or deleted, so draft lists and counts can refresh.
/// </summary>
public sealed record DraftsChangedMessage;

/// <summary>
/// Published via <c>WeakReferenceMessenger.Default</c> after an existing event
/// is successfully updated.
/// </summary>
public sealed record EventUpdatedMessage(string EventId);
