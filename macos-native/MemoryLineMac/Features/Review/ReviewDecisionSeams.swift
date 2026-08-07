import Foundation

/// The one write a non-Windows device may make: an approve/reject verdict on an
/// extracted event awaiting review.
///
/// Swift mirror of `PendingEventDecisionPayload` in
/// `shared-contracts/dotnet/MemoryTimeline.SyncContracts/TimelineProjectionContracts.cs`.
/// Read that file's argument for why accepting this does not make the system
/// multi-writer: a verdict carries no field values to merge, applying it twice is
/// the same as applying it once, and two devices sending the same answer agree by
/// construction. Windows still performs the approval — writing the event, its
/// tags, people and locations in one transaction — so the extraction rules stay
/// in one place.
///
/// `decision` has no default here, mirroring the C# side's refusal to default it
/// to `approve`: the failure mode of a missing verdict must never be a write to
/// someone's archive. The service rejects an empty value outright.
struct PendingEventDecisionPayload: Codable, Equatable, Sendable {
    var pendingEventId: String
    var decision: String
    /// The device that decided, for the audit trail Windows keeps.
    var decidedByDeviceId: String
    var decidedAtUtc: Date
    /// Reviewer corrections applied before approval — nil means "approve as
    /// extracted". Deliberately not general editing; it rides the decision
    /// because it is part of the same single act.
    var corrections: PendingEventCorrections?
}

/// The narrow set of fields a reviewer may correct while approving.
struct PendingEventCorrections: Codable, Equatable, Sendable {
    var title: String?
    var startDate: Date?
    var endDate: Date?
    var datePrecision: String?
    var category: String?
}

/// Verdicts a companion may return. Mirrors `PendingEventDecision` in the C#
/// contract; the strings are the wire values.
enum ReviewVerdict: String, Codable, Sendable, CaseIterable {
    case approve
    case reject

    var displayName: String {
        switch self {
        case .approve: return "Approve"
        case .reject: return "Reject"
        }
    }

    /// For status text about a verdict already given. Spelled out rather than
    /// built by appending "d", which turns "Reject" into "Rejectd".
    var pastTense: String {
        switch self {
        case .approve: return "Approved"
        case .reject: return "Rejected"
        }
    }
}

/// What the Mac needs from the sync client to deliver a decision.
///
/// A narrow protocol rather than a use of `SyncAPI`, for two reasons. `SyncAPI`
/// has no `push` and should not grow one — the phone reaches the server through
/// the capture endpoints and would be obliged to implement a call it never makes,
/// as would every test double in both suites. And naming exactly one capability
/// keeps the Mac's write authority legible: this protocol *is* the statement that
/// a decision is the only thing this app sends over the change feed.
protocol MacDecisionPushing: AnyObject, Sendable {
    func push(_ request: SyncPushRequest) async throws -> SyncPushResponse
}

/// `SyncAPIClient` already has a matching `push`, so this conformance is
/// declaration only. It lives in the macOS target rather than beside the client
/// because the client is shared source and the phone has no review surface.
extension SyncAPIClient: MacDecisionPushing {}

/// How far a queued decision has got. There is no `sending` state: a push either
/// returns or it does not, and a state that only exists inside one `await` cannot
/// be observed by anything that matters — least of all across a relaunch, which
/// is the only reason this table is durable.
enum ReviewDecisionState: String, Codable, Sendable {
    /// Waiting to be pushed, or waiting to be retried.
    case pending
    /// The server accepted it. The row is kept rather than deleted so the review
    /// queue can show "approved, waiting for Windows" instead of briefly showing
    /// the item as undecided again; it is pruned once Windows confirms by
    /// publishing the pending event's tombstone.
    case sent
    /// The server rejected it permanently. Retrying would send the same bytes to
    /// the same validation, so the row stops here and the UI says so.
    case failed
}

/// One queued verdict.
struct ReviewDecisionRecord: Equatable, Sendable {
    var pendingEventId: String
    var verdict: ReviewVerdict
    var decidedAt: Date
    /// Allocated once, when the decision is queued, and reused unchanged across
    /// every retry. The server keys its receipt on (device, clientSequence), so a
    /// retry after a response that never arrived is recognised as the same entry
    /// rather than applied twice — but only if the number does not change.
    var clientSequence: Int64
    var state: ReviewDecisionState = .pending
    var attempts: Int = 0
    /// Server-supplied reason for a `failed` row, safe to display (§14.5).
    var lastError: String?
}

/// Durable queue of verdicts this Mac has made and not yet delivered.
///
/// Durable because the alternative is losing a user's review to a quit. It is a
/// separate table from the projection deliberately: everything in
/// `timeline_pending_event` is Windows' to write and this Mac's to discard,
/// whereas these rows are the Mac's own and must survive a projection reset.
protocol ReviewDecisionStore: AnyObject, Sendable {
    /// Inserts or replaces the verdict for a pending event. Replacing is the
    /// right behaviour for a reviewer who changes their mind before the queue
    /// drains; a delivered decision is not replaced, because Windows has already
    /// acted on it (`decision(for:)` lets the caller check).
    func upsert(_ decision: ReviewDecisionRecord) throws
    func decision(for pendingEventId: String) throws -> ReviewDecisionRecord?
    func all() throws -> [ReviewDecisionRecord]
    /// Undelivered verdicts, oldest first, so the queue drains in the order the
    /// reviewer worked.
    func pending() throws -> [ReviewDecisionRecord]
    func delete(pendingEventId: String) throws
    func deleteAll() throws

    /// Allocates the next client sequence. Monotonic and persisted: deriving it
    /// from `MAX(client_sequence)` over the table would reuse a number after its
    /// row was pruned, and the server would then recognise a brand-new decision
    /// as a duplicate of a delivered one and silently drop it.
    func nextClientSequence() throws -> Int64
}
