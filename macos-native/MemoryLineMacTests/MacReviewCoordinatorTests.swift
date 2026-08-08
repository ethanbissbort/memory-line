import XCTest
@testable import MemoryLineMac

/// Covers the Mac's only write path: queueing a review verdict and delivering
/// it. The store and the projection are real (SQLite in a temp file); only the
/// push is faked, because what is being tested is which failures retry and which
/// stop, not the HTTP.
final class MacReviewCoordinatorTests: XCTestCase {

    // MARK: - Fixture

    private struct Fixture {
        let settings: SQLiteSettingsStore
        let decisions: SQLiteReviewDecisionStore
        let projections: SQLiteTimelineProjectionStore
    }

    private func makeFixture(paired: Bool = true) throws -> Fixture {
        let directory = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("macreview-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: directory) }

        let database = try AppDatabase.open(at: directory.appendingPathComponent("memoryline.db"))
        let settings = SQLiteSettingsStore(database: database)
        let decisions = try SQLiteReviewDecisionStore(database: database, settings: settings)
        let projections = try SQLiteTimelineProjectionStore(database: database)

        if paired {
            settings.set("https://example.test", for: AppSettingsKey.serverURL)
            settings.set("device-1", for: AppSettingsKey.deviceId)
        }

        return Fixture(settings: settings, decisions: decisions, projections: projections)
    }

    @MainActor
    private func makeCoordinator(_ f: Fixture, api: FakePush) -> MacReviewCoordinator {
        MacReviewCoordinator(
            settings: f.settings, api: api, store: f.decisions, projections: f.projections)
    }

    // MARK: - Queueing

    @MainActor
    func testDecideQueuesDurablyBeforeSending() async throws {
        let f = try makeFixture()
        let api = FakePush()
        api.error = SyncAPIError(
            statusCode: nil, apiError: nil, retryable: true, message: "offline")

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.decide(pendingEventId: "pending-1", verdict: .approve)

        // The point of the queue: the send failed and the verdict survived.
        let stored = try f.decisions.decision(for: "pending-1")
        XCTAssertEqual(stored?.verdict, .approve)
        XCTAssertEqual(stored?.state, .pending, "a transport failure must not discard the verdict")
    }

    @MainActor
    func testAcceptedDecisionIsMarkedSentAndCarriesTheContractPayload() async throws {
        let f = try makeFixture()
        let api = FakePush()

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.decide(pendingEventId: "pending-1", verdict: .reject)

        XCTAssertEqual(try f.decisions.decision(for: "pending-1")?.state, .sent)

        let entry = try XCTUnwrap(api.sent.first?.entries.first)
        XCTAssertEqual(entry.entityType, "pending_event_decision")
        XCTAssertEqual(entry.entityId, "pending-1")

        let payload = try SyncJSON.decoder.decode(
            PendingEventDecisionPayload.self,
            from: XCTUnwrap(entry.payloadJson).data(using: .utf8)!)
        XCTAssertEqual(payload.decision, "reject")
        XCTAssertEqual(payload.decidedByDeviceId, "device-1")
        XCTAssertNil(payload.corrections, "no corrections UI yet; sending an empty object would say something different")
    }

    /// An unpaired Mac still records the verdict — it just has nowhere to send
    /// it. Refusing to queue would lose a review made before pairing finished.
    @MainActor
    func testUnpairedMacQueuesWithoutCallingTheServer() async throws {
        let f = try makeFixture(paired: false)
        let api = FakePush()

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.decide(pendingEventId: "pending-1", verdict: .approve)

        XCTAssertEqual(api.sent.count, 0)
        XCTAssertEqual(try f.decisions.decision(for: "pending-1")?.state, .pending)
    }

    // MARK: - What retries and what does not

    @MainActor
    func testPerEntryRejectionIsTerminalAndNotRetried() async throws {
        let f = try makeFixture()
        let api = FakePush()
        api.reject = "This pending event has already been reviewed."

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.decide(pendingEventId: "pending-1", verdict: .approve)

        let stored = try XCTUnwrap(try f.decisions.decision(for: "pending-1"))
        XCTAssertEqual(stored.state, .failed, "the same bytes would fail the same validation")
        XCTAssertEqual(stored.lastError, "This pending event has already been reviewed.")

        // A later drain must not pick it back up.
        await coordinator.drain()
        XCTAssertEqual(api.sent.count, 1, "a rejected entry must not be resent")
    }

    @MainActor
    func testTransportFailureRetriesOnTheNextDrain() async throws {
        let f = try makeFixture()
        let api = FakePush()
        api.error = SyncAPIError(
            statusCode: 503, apiError: nil, retryable: true, message: "Service Unavailable")

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.decide(pendingEventId: "pending-1", verdict: .approve)
        XCTAssertEqual(api.sent.count, 1)

        api.error = nil
        await coordinator.drain()

        XCTAssertEqual(api.sent.count, 2, "an unreachable server is a state that ends")
        XCTAssertEqual(try f.decisions.decision(for: "pending-1")?.state, .sent)
    }

    /// A duplicate means the server already has what we were sending. Treating it
    /// as a failure would retry forever against a receipt that keeps saying the
    /// same thing.
    @MainActor
    func testDuplicateResultCountsAsDelivered() async throws {
        let f = try makeFixture()
        let api = FakePush()
        api.duplicate = true

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.decide(pendingEventId: "pending-1", verdict: .approve)

        XCTAssertEqual(try f.decisions.decision(for: "pending-1")?.state, .sent)
    }

    @MainActor
    func testDeliveredDecisionIsNotReplacedBySecondThoughts() async throws {
        let f = try makeFixture()
        let api = FakePush()

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.decide(pendingEventId: "pending-1", verdict: .approve)
        await coordinator.decide(pendingEventId: "pending-1", verdict: .reject)

        // Windows has already acted on the approval; the service drops a verdict
        // for a review it has resolved, so a second one would only produce a
        // confusing local state.
        XCTAssertEqual(try f.decisions.decision(for: "pending-1")?.verdict, .approve)
        XCTAssertEqual(api.sent.count, 1)
    }

    // MARK: - Sequence allocation

    /// The sequence must never be reused: the server keys its receipt on
    /// (device, clientSequence), so a repeat would be recognised as a duplicate
    /// of a delivered decision and silently dropped. Deriving it from the table
    /// would do exactly that once a row is pruned.
    @MainActor
    func testClientSequenceIsNotReusedAfterTheRowIsPruned() async throws {
        let f = try makeFixture()
        let api = FakePush()
        let coordinator = makeCoordinator(f, api: api)

        await coordinator.decide(pendingEventId: "pending-1", verdict: .approve)
        let first = try XCTUnwrap(f.decisions.decision(for: "pending-1")?.clientSequence)

        try f.decisions.delete(pendingEventId: "pending-1")
        await coordinator.decide(pendingEventId: "pending-2", verdict: .approve)
        let second = try XCTUnwrap(f.decisions.decision(for: "pending-2")?.clientSequence)

        XCTAssertGreaterThan(second, first)
    }

    // MARK: - Pruning

    @MainActor
    func testDeliveredRowIsPrunedOnlyOnceWindowsConfirms() async throws {
        let f = try makeFixture()
        try f.projections.upsertPendingEvent(PendingEventProjectionPayload(
            pendingEventId: "pending-1",
            title: "Ferry to the island",
            startDate: Date(timeIntervalSince1970: 1_000_000_000),
            updatedAtUtc: Date(timeIntervalSince1970: 1_700_000_000)))

        let coordinator = makeCoordinator(f, api: FakePush())
        await coordinator.decide(pendingEventId: "pending-1", verdict: .approve)

        // Windows has not published its tombstone yet, so the row stays and the
        // queue keeps showing "waiting for Windows" rather than flipping the item
        // back to undecided.
        coordinator.pruneConfirmed()
        XCTAssertNotNil(try f.decisions.decision(for: "pending-1"))

        try f.projections.deletePendingEvent(pendingEventId: "pending-1")
        coordinator.pruneConfirmed()
        XCTAssertNil(try f.decisions.decision(for: "pending-1"))
    }

    @MainActor
    func testUndeliveredRowIsNeverPruned() async throws {
        let f = try makeFixture()
        let api = FakePush()
        api.error = SyncAPIError(statusCode: nil, apiError: nil, retryable: true, message: "offline")

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.decide(pendingEventId: "pending-1", verdict: .approve)

        // Nothing about this pending event is in the projection, so a prune that
        // keyed only on absence would delete an undelivered verdict.
        coordinator.pruneConfirmed()
        XCTAssertEqual(try f.decisions.decision(for: "pending-1")?.state, .pending)
    }
}

/// Minimal `MacDecisionPushing` double. Records what was sent and replays a
/// scripted outcome; every entry in a request gets the same outcome, which is
/// enough because the coordinator's per-entry handling is keyed on
/// `clientSequence` rather than on position.
private final class FakePush: MacDecisionPushing, @unchecked Sendable {
    private(set) var sent: [SyncPushRequest] = []
    /// Thrown instead of returning — a transport failure.
    var error: SyncAPIError?
    /// When set, entries come back rejected with this message.
    var reject: String?
    var duplicate = false

    func push(_ request: SyncPushRequest) async throws -> SyncPushResponse {
        sent.append(request)
        if let error { throw error }
        return SyncPushResponse(results: request.entries.map { entry in
            SyncPushEntryResult(
                clientSequence: entry.clientSequence,
                accepted: reject == nil,
                duplicate: duplicate,
                serverChangeId: reject == nil ? 1 : nil,
                error: reject)
        })
    }
}
