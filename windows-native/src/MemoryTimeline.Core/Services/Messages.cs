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
/// is successfully updated. Carries the (possibly changed) start date so
/// subscribers can decide whether the event is inside their viewport without
/// a lookup.
/// </summary>
public sealed record EventUpdatedMessage(string EventId, DateTime StartDate);

/// <summary>
/// Published via <c>WeakReferenceMessenger.Default</c> after an event is
/// successfully deleted, so views holding the event (timeline, search) can
/// drop it without renavigation.
/// </summary>
public sealed record EventDeletedMessage(string EventId);

/// <summary>
/// Published via <c>WeakReferenceMessenger.Default</c> after a media file is
/// successfully attached to an event, so the timeline can refresh the pin's
/// photo-count badge and the details-panel media strip without renavigation.
/// </summary>
public sealed record MediaAttachedMessage(string EventId, string MediaId);

/// <summary>
/// Published via <c>WeakReferenceMessenger.Default</c> when the timeline's
/// swimlane mode changes (the <c>LaneMode</c> enum name as a string, e.g.
/// "Auto", "Category"), so other views can react to the new grouping.
/// </summary>
public sealed record LaneModeChangedMessage(string LaneMode);

/// <summary>
/// Published via <c>WeakReferenceMessenger.Default</c> after the archive has
/// been replaced from a backup file (<c>IBackupService.RestoreAsync</c>).
/// Consumers should surface a "restart recommended" notice: in-memory caches
/// (settings, singleton view-model state) may still reflect the pre-restore
/// database.
/// </summary>
public sealed record ArchiveRestoredMessage(string BackupPath);
