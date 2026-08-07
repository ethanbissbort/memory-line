namespace MemoryTimeline.Core.Services;

/// <summary>
/// The archive entities that are projected onto the sync feed. Used by
/// <see cref="ITimelineProjectionPublisher.PublishDeletedAsync"/>, where the row
/// is already gone and only its identity survives to be tombstoned.
/// </summary>
public enum TimelineProjectionEntity
{
    /// <summary>A timeline event.</summary>
    Event,

    /// <summary>A life period.</summary>
    Era,

    /// <summary>A contact-book person.</summary>
    Person,

    /// <summary>An extracted event awaiting review.</summary>
    PendingEvent,
}

/// <summary>
/// Projects the Windows archive — events, eras, people and the review queue —
/// onto the sync feed so a companion device can render a timeline it does not
/// own (design §19 Phase 3).
///
/// The port is declared here, next to <see cref="ICaptureStatusPublisher"/> and
/// for the same reason: the services that mutate the archive must be able to say
/// "this changed" without Core taking a dependency on the sync contracts. The
/// implementation lives in MemoryTimeline.Sync, writes rows into the local sync
/// outbox, and <c>LocalOutboxPublisher</c> pushes them.
///
/// <para><b>Direction.</b> Windows publishes; companions consume. These are
/// read-only projections, never instructions — Windows remains the only writer
/// and the only editing surface. The single write that travels the other way is
/// a pending-event decision, which the sync layer applies through the same
/// approval path the Windows UI uses.</para>
///
/// <para><b>Idempotence.</b> Every method is safe to call after every mutation:
/// a projection that is byte-identical to the last one published for the same
/// entity is dropped rather than re-published, exactly as
/// <see cref="ICaptureStatusPublisher"/> no-ops on an unchanged status. Callers
/// therefore never need to work out whether a change was "interesting enough".
/// </para>
///
/// <para><b>Privacy (§14.5).</b> Implementations publish what a user reads on a
/// timeline — titles, dates, categories, names — and never transcript bodies,
/// audio, file paths, or contact details. Text that can be arbitrarily long is
/// bounded by the publisher.</para>
/// </summary>
public interface ITimelineProjectionPublisher
{
    /// <summary>
    /// Publishes an event as an <c>event</c> upsert, with its tags, people and
    /// locations denormalised inline. No-ops when the event does not exist (a
    /// deleted event is tombstoned through <see cref="PublishDeletedAsync"/>,
    /// which still has its id) and when the projection is unchanged.
    /// </summary>
    /// <param name="eventId">The event to project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishEventAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an era as an <c>era</c> upsert. No-ops for a missing era or an
    /// unchanged projection.
    /// </summary>
    /// <param name="eraId">The era to project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishEraAsync(string eraId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a person as a <c>person</c> upsert. No-ops for a missing person
    /// or an unchanged projection.
    ///
    /// A person who was merged away is NOT a deletion: the row survives carrying
    /// its <c>merged_into_id</c>, and this method publishes it so consumers can
    /// follow the pointer to the surviving person instead of showing a duplicate
    /// or a dangling reference from older events.
    /// </summary>
    /// <param name="personId">The person to project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishPersonAsync(string personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an extracted event awaiting review as a <c>pending_event</c>
    /// upsert, so a companion can show the queue and act on it. No-ops for a
    /// missing pending event or an unchanged projection.
    /// </summary>
    /// <param name="pendingEventId">The pending event to project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishPendingEventAsync(string pendingEventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tombstones a removed entity with a <c>delete</c> operation, so a
    /// companion drops its copy instead of showing a memory the user deleted.
    /// Takes the id rather than the row because the row is already gone by the
    /// time a caller knows to publish this.
    ///
    /// Repeated calls collapse: a delete published immediately after another
    /// delete for the same entity is dropped. Note that a merged person is not a
    /// deletion — see <see cref="PublishPersonAsync"/>.
    /// </summary>
    /// <param name="entity">Which entity type the id belongs to.</param>
    /// <param name="entityId">The id that no longer exists.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishDeletedAsync(
        TimelineProjectionEntity entity, string entityId, CancellationToken cancellationToken = default);
}
