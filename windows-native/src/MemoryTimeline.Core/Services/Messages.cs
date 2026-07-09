namespace MemoryTimeline.Core.Services;

/// <summary>
/// Published via <c>CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default</c>
/// after an event is successfully created, so the timeline (and other views) can
/// refresh without renavigation.
/// </summary>
public sealed record EventCreatedMessage(string EventId, DateTime StartDate);
