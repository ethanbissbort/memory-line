import XCTest
@testable import MemoryLineCompanion

@MainActor
final class StatusSyncCoordinatorTests: XCTestCase {
    private var databaseURL: URL!
    private var database: SQLiteDatabase!
    private var captures: SQLiteCaptureStore!
    private var settings: SQLiteSettingsStore!
    private var statuses: SQLiteCaptureStatusStore!
    private var api: StubSyncAPI!
    private var notifier: SpyStatusNotifier!
    private var coordinator: StatusSyncCoordinator!

    override func setUpWithError() throws {
        databaseURL = TestSupport.temporaryDatabaseURL()
        database = try AppDatabase.open(at: databaseURL)
        captures = SQLiteCaptureStore(database: database)
        settings = SQLiteSettingsStore(database: database)
        statuses = try SQLiteCaptureStatusStore(database: database)
        api = StubSyncAPI()
        notifier = SpyStatusNotifier()
        coordinator = StatusSyncCoordinator(
            store: captures,
            settings: settings,
            api: api,
            statusStore: statuses,
            notifier: notifier)
        StatusSyncTestData.markPaired(settings)
    }

    override func tearDownWithError() throws {
        coordinator = nil
        statuses = nil
        settings = nil
        captures = nil
        database = nil
        TestSupport.removeDatabaseFiles(at: databaseURL)
    }

    // MARK: - Status mapping

    func testStatusMapsOntoTheLifecycle() {
        let table: [(status: String, current: CaptureLifecycleState, expected: CaptureLifecycleState?)] = [
            (SyncCaptureStatus.received, .uploaded, .processingRemote),
            (SyncCaptureStatus.processing, .uploaded, .processingRemote),
            (SyncCaptureStatus.reviewReady, .processingRemote, .reviewReady),
            (SyncCaptureStatus.completed, .reviewReady, .completed),
            // Windows may skip straight past a stage the phone never saw.
            (SyncCaptureStatus.completed, .uploaded, .completed),
            (SyncCaptureStatus.reviewReady, .uploaded, .reviewReady),
            (SyncCaptureStatus.failed, .uploaded, .failedRecoverable),
            (SyncCaptureStatus.failed, .processingRemote, .failedRecoverable),
            (SyncCaptureStatus.failed, .completed, .failedRecoverable),
            // A failure the phone already recorded is not a transition.
            (SyncCaptureStatus.failed, .failedRecoverable, nil),
            // Windows holding the capture proves the upload landed, so a
            // capture parked by a failed upload attempt may move forward.
            (SyncCaptureStatus.reviewReady, .failedRecoverable, .reviewReady),
            (SyncCaptureStatus.processing, .failedRecoverable, .processingRemote),
        ]

        for row in table {
            XCTAssertEqual(
                StatusSyncCoordinator.resolvedState(for: row.status, current: row.current),
                row.expected,
                "\(row.status) from \(row.current.rawValue)")
        }
    }

    /// A late status must never clobber a capture the phone still owns.
    func testNeverDowngradesACaptureThatIsStillLocal() {
        let localStates: [CaptureLifecycleState] = [.recording, .savedLocal, .queuedUpload, .uploading, .cancelled]
        let wireStatuses = [
            SyncCaptureStatus.received,
            SyncCaptureStatus.processing,
            SyncCaptureStatus.reviewReady,
            SyncCaptureStatus.completed,
            SyncCaptureStatus.failed,
        ]

        for state in localStates {
            for status in wireStatuses {
                XCTAssertNil(
                    StatusSyncCoordinator.resolvedState(for: status, current: state),
                    "\(status) must not touch \(state.rawValue)")
            }
        }
    }

    func testNeverMovesACaptureBackwardsThroughTheLifecycle() {
        let backwards: [(status: String, current: CaptureLifecycleState)] = [
            (SyncCaptureStatus.received, .processingRemote),
            (SyncCaptureStatus.processing, .processingRemote),
            (SyncCaptureStatus.processing, .reviewReady),
            (SyncCaptureStatus.processing, .completed),
            (SyncCaptureStatus.reviewReady, .reviewReady),
            (SyncCaptureStatus.reviewReady, .completed),
            (SyncCaptureStatus.completed, .completed),
        ]

        for row in backwards {
            XCTAssertNil(
                StatusSyncCoordinator.resolvedState(for: row.status, current: row.current),
                "\(row.status) from \(row.current.rawValue)")
        }
    }

    /// `local_only` and `uploading` echo what the phone already knows, and a
    /// status from a newer Windows build must be inert rather than harmful.
    func testEchoedAndUnknownStatusesChangeNothing() {
        for status in [SyncCaptureStatus.localOnly, SyncCaptureStatus.uploading, "quantum_indexing", ""] {
            for state in CaptureLifecycleState.allCases {
                XCTAssertNil(
                    StatusSyncCoordinator.resolvedState(for: status, current: state),
                    "\(status) from \(state.rawValue)")
            }
        }
    }

    // MARK: - Applying a pull

    func testAppliesStatusAndStoresTheDetail() async throws {
        try captures.insert(TestSupport.makeCapture(id: "cap-1", state: .uploaded))
        api.pullResults = [StatusSyncTestData.page([
            try StatusSyncTestData.change(changeId: 12, payload: StatusSyncTestData.payload(
                captureId: "cap-1",
                status: SyncCaptureStatus.reviewReady,
                processingStage: "review_ready",
                transcriptAvailable: true,
                transcriptPreview: "We stopped at the diner",
                transcriptCharCount: 4213,
                pendingEventCount: 3,
                approvedEventCount: 0))
        ], nextCursor: 12)]

        await coordinator.pullNow()

        XCTAssertEqual(try XCTUnwrap(captures.capture(id: "cap-1")).state, .reviewReady)
        let status = try XCTUnwrap(statuses.status(captureId: "cap-1"))
        XCTAssertEqual(status.status, SyncCaptureStatus.reviewReady)
        XCTAssertEqual(status.processingStage, "review_ready")
        XCTAssertEqual(status.transcriptPreview, "We stopped at the diner")
        XCTAssertEqual(status.transcriptCharCount, 4213)
        XCTAssertEqual(status.pendingEventCount, 3)
        XCTAssertEqual(status.approvedEventCount, 0)
        XCTAssertNil(coordinator.lastPullError)
        XCTAssertNotNil(coordinator.lastPulledAt)
    }

    func testFailureStatusRecordsTheReasonOnTheCapture() async throws {
        try captures.insert(TestSupport.makeCapture(id: "cap-1", state: .processingRemote))
        api.pullResults = [StatusSyncTestData.page([
            try StatusSyncTestData.change(changeId: 4, payload: StatusSyncTestData.payload(
                captureId: "cap-1",
                status: SyncCaptureStatus.failed,
                processingStage: "failed_retryable",
                failureReason: "The transcription service was unreachable.",
                failureRetryable: true))
        ], nextCursor: 4)]

        await coordinator.pullNow()

        let capture = try XCTUnwrap(captures.capture(id: "cap-1"))
        XCTAssertEqual(capture.state, .failedRecoverable)
        XCTAssertEqual(capture.lastError, "The transcription service was unreachable.")
        XCTAssertEqual(try XCTUnwrap(statuses.status(captureId: "cap-1")).failureRetryable, true)
    }

    /// Moving past a failure clears the stale error the UI would otherwise
    /// keep showing.
    func testAdvancingPastAFailureClearsTheLastError() async throws {
        var capture = TestSupport.makeCapture(id: "cap-1", state: .failedRecoverable)
        capture.lastError = "Upload was interrupted."
        try captures.insert(capture)
        api.pullResults = [StatusSyncTestData.page([
            try StatusSyncTestData.change(changeId: 7, payload: StatusSyncTestData.payload(
                captureId: "cap-1", status: SyncCaptureStatus.completed))
        ], nextCursor: 7)]

        await coordinator.pullNow()

        let updated = try XCTUnwrap(captures.capture(id: "cap-1"))
        XCTAssertEqual(updated.state, .completed)
        XCTAssertNil(updated.lastError)
    }

    /// A capture deleted on the phone is not an error: no crash, no orphan
    /// status row, and the cursor still advances past the change.
    func testUnknownCaptureIdIsASilentNoOp() async throws {
        api.pullResults = [StatusSyncTestData.page([
            try StatusSyncTestData.change(changeId: 9, payload: StatusSyncTestData.payload(
                captureId: "deleted-on-phone", status: SyncCaptureStatus.completed))
        ], nextCursor: 9)]

        await coordinator.pullNow()

        XCTAssertNil(try statuses.status(captureId: "deleted-on-phone"))
        XCTAssertEqual(try statuses.allStatuses().count, 0)
        XCTAssertEqual(settings.string(SyncCursorKey.pullCursor), "9")
        XCTAssertNil(coordinator.lastPullError)
        XCTAssertTrue(notifier.posted.isEmpty)
    }

    /// The change log carries every entity type; the ones this build does not
    /// consume must still carry the cursor forward.
    func testOtherEntityTypesOnlyAdvanceTheCursor() async throws {
        try captures.insert(TestSupport.makeCapture(id: "cap-1", state: .uploaded))
        api.pullResults = [StatusSyncTestData.page([
            try StatusSyncTestData.change(
                changeId: 20,
                payload: StatusSyncTestData.payload(captureId: "cap-1", status: SyncCaptureStatus.completed),
                entityType: SyncChangeEntityType.capture)
        ], nextCursor: 20)]

        await coordinator.pullNow()

        XCTAssertEqual(try XCTUnwrap(captures.capture(id: "cap-1")).state, .uploaded)
        XCTAssertNil(try statuses.status(captureId: "cap-1"))
        XCTAssertEqual(settings.string(SyncCursorKey.pullCursor), "20")
    }

    /// A payload this build cannot read must not wedge the cursor forever.
    func testUndecodablePayloadIsSkippedAndTheCursorAdvances() async throws {
        try captures.insert(TestSupport.makeCapture(id: "cap-1", state: .uploaded))
        let broken = SyncChangeDto(
            changeId: 30,
            entityType: SyncChangeEntityType.captureStatus,
            entityId: "cap-1",
            operation: "upsert",
            revision: 1,
            changedAtUtc: Date(timeIntervalSince1970: 1_754_500_000),
            sourceDeviceId: "windows-1",
            payloadJson: "{\"captureId\":\"cap-1\"")
        api.pullResults = [StatusSyncTestData.page([broken], nextCursor: 30)]

        await coordinator.pullNow()

        XCTAssertEqual(try XCTUnwrap(captures.capture(id: "cap-1")).state, .uploaded)
        XCTAssertEqual(settings.string(SyncCursorKey.pullCursor), "30")
        XCTAssertNil(coordinator.lastPullError)
    }

    // MARK: - Cursor and ack

    func testCursorAdvancesPersistsAndIsAckedAfterApply() async throws {
        try captures.insert(TestSupport.makeCapture(id: "cap-1", state: .uploaded))
        api.pullResults = [StatusSyncTestData.page([
            try StatusSyncTestData.change(changeId: 41, payload: StatusSyncTestData.payload(
                captureId: "cap-1", status: SyncCaptureStatus.processing))
        ], nextCursor: 41)]

        await coordinator.pullNow()

        XCTAssertEqual(api.pullCursors, [0])
        XCTAssertEqual(api.ackedCursors, [41])
        XCTAssertEqual(settings.string(SyncCursorKey.pullCursor), "41")
        // The ack only happens once the page is durably applied.
        XCTAssertEqual(try XCTUnwrap(captures.capture(id: "cap-1")).state, .processingRemote)
    }

    func testNextPassResumesFromThePersistedCursor() async throws {
        settings.set("128", for: SyncCursorKey.pullCursor)

        await coordinator.pullNow()

        XCTAssertEqual(api.pullCursors, [128])
    }

    func testEmptyPageIsNotAcked() async {
        await coordinator.pullNow()

        XCTAssertEqual(api.pullCursors, [0])
        XCTAssertTrue(api.ackedCursors.isEmpty)
        XCTAssertNil(settings.string(SyncCursorKey.pullCursor))
        XCTAssertNil(coordinator.lastPullError)
    }

    func testPagesUntilHasMoreClears() async throws {
        try captures.insert(TestSupport.makeCapture(id: "cap-1", state: .uploaded))
        try captures.insert(TestSupport.makeCapture(id: "cap-2", state: .uploaded))
        api.pullResults = [
            StatusSyncTestData.page([
                try StatusSyncTestData.change(changeId: 1, payload: StatusSyncTestData.payload(
                    captureId: "cap-1", status: SyncCaptureStatus.processing))
            ], nextCursor: 1, hasMore: true),
            StatusSyncTestData.page([
                try StatusSyncTestData.change(changeId: 2, payload: StatusSyncTestData.payload(
                    captureId: "cap-2", status: SyncCaptureStatus.completed))
            ], nextCursor: 2, hasMore: false),
        ]

        await coordinator.pullNow()

        XCTAssertEqual(api.pullCursors, [0, 1])
        XCTAssertEqual(api.ackedCursors, [1, 2])
        XCTAssertEqual(try XCTUnwrap(captures.capture(id: "cap-1")).state, .processingRemote)
        XCTAssertEqual(try XCTUnwrap(captures.capture(id: "cap-2")).state, .completed)
        XCTAssertEqual(settings.string(SyncCursorKey.pullCursor), "2")
    }

    func testRetryableFailureStopsAndKeepsTheCursor() async throws {
        try captures.insert(TestSupport.makeCapture(id: "cap-1", state: .uploaded))
        api.pullResults = [
            StatusSyncTestData.page([
                try StatusSyncTestData.change(changeId: 5, payload: StatusSyncTestData.payload(
                    captureId: "cap-1", status: SyncCaptureStatus.processing))
            ], nextCursor: 5, hasMore: true),
            .failure(SyncAPIError(
                statusCode: 503, apiError: nil, retryable: true, message: "The server is unavailable.")),
        ]

        await coordinator.pullNow()

        XCTAssertEqual(api.pullCursors, [0, 5])
        XCTAssertEqual(api.ackedCursors, [5])
        // The first page stuck; only the failing page is replayed next time.
        XCTAssertEqual(settings.string(SyncCursorKey.pullCursor), "5")
        XCTAssertEqual(coordinator.lastPullError, "The server is unavailable.")
        XCTAssertNil(coordinator.lastPulledAt)
    }

    func testFirstPageFailureLeavesTheCursorUntouched() async {
        api.pullResults = [.failure(SyncAPIError(
            statusCode: nil, apiError: nil, retryable: true, message: "The network connection was lost."))]

        await coordinator.pullNow()

        XCTAssertNil(settings.string(SyncCursorKey.pullCursor))
        XCTAssertTrue(api.ackedCursors.isEmpty)
        XCTAssertEqual(coordinator.lastPullError, "The network connection was lost.")
    }

    func testRevokedDeviceReportsARepairableError() async {
        api.pullResults = [.failure(SyncAPIError(
            statusCode: 401, apiError: nil, retryable: false, message: "Unauthorized."))]

        await coordinator.pullNow()

        XCTAssertEqual(
            coordinator.lastPullError,
            "This device is no longer paired. Re-pair in Settings to resume sync.")
    }

    /// A failed ack costs only the server's retention bookkeeping — the cursor
    /// is already durable locally, so the page must not be replayed.
    func testFailedAckDoesNotRewindTheCursor() async throws {
        try captures.insert(TestSupport.makeCapture(id: "cap-1", state: .uploaded))
        api.ackError = SyncAPIError(
            statusCode: 503, apiError: nil, retryable: true, message: "The server is unavailable.")
        api.pullResults = [StatusSyncTestData.page([
            try StatusSyncTestData.change(changeId: 6, payload: StatusSyncTestData.payload(
                captureId: "cap-1", status: SyncCaptureStatus.completed))
        ], nextCursor: 6)]

        await coordinator.pullNow()

        XCTAssertEqual(settings.string(SyncCursorKey.pullCursor), "6")
        XCTAssertEqual(try XCTUnwrap(captures.capture(id: "cap-1")).state, .completed)
        XCTAssertNil(coordinator.lastPullError)
    }

    /// Local persistence failure must hold the cursor so the page replays.
    func testLocalPersistenceFailureHoldsTheCursor() async throws {
        let failingCoordinator = StatusSyncCoordinator(
            store: captures,
            settings: settings,
            api: api,
            statusStore: FailingCaptureStatusStore(),
            notifier: notifier)
        try captures.insert(TestSupport.makeCapture(id: "cap-1", state: .uploaded))
        api.pullResults = [StatusSyncTestData.page([
            try StatusSyncTestData.change(changeId: 8, payload: StatusSyncTestData.payload(
                captureId: "cap-1", status: SyncCaptureStatus.completed))
        ], nextCursor: 8)]

        await failingCoordinator.pullNow()

        XCTAssertNil(settings.string(SyncCursorKey.pullCursor))
        XCTAssertTrue(api.ackedCursors.isEmpty)
        XCTAssertEqual(try XCTUnwrap(captures.capture(id: "cap-1")).state, .uploaded)
        XCTAssertEqual(failingCoordinator.lastPullError, "Could not save the latest capture status.")
    }

    // MARK: - Notifications

    func testNotifiesOnlyOnMeaningfulTransitions() async throws {
        try captures.insert(TestSupport.makeCapture(id: "cap-processing", state: .uploaded))
        try captures.insert(TestSupport.makeCapture(id: "cap-review", state: .uploaded))
        try captures.insert(TestSupport.makeCapture(id: "cap-done", state: .uploaded))
        try captures.insert(TestSupport.makeCapture(id: "cap-failed", state: .uploaded))
        api.pullResults = [StatusSyncTestData.page([
            try StatusSyncTestData.change(changeId: 1, payload: StatusSyncTestData.payload(
                captureId: "cap-processing", status: SyncCaptureStatus.processing)),
            try StatusSyncTestData.change(changeId: 2, payload: StatusSyncTestData.payload(
                captureId: "cap-review", status: SyncCaptureStatus.reviewReady)),
            try StatusSyncTestData.change(changeId: 3, payload: StatusSyncTestData.payload(
                captureId: "cap-done", status: SyncCaptureStatus.completed)),
            try StatusSyncTestData.change(changeId: 4, payload: StatusSyncTestData.payload(
                captureId: "cap-failed", status: SyncCaptureStatus.failed,
                failureReason: "Audio could not be decoded.")),
        ], nextCursor: 4)]

        await coordinator.pullNow()

        XCTAssertEqual(notifier.posted, [
            .reviewReady(captureId: "cap-review"),
            .completed(captureId: "cap-done"),
            .failed(captureId: "cap-failed", reason: "Audio could not be decoded."),
        ])
    }

    /// A replayed change is not news: the state does not move, so nothing is
    /// posted a second time.
    func testReplayedChangeDoesNotNotifyAgain() async throws {
        try captures.insert(TestSupport.makeCapture(id: "cap-1", state: .uploaded))
        let change = try StatusSyncTestData.change(
            changeId: 1,
            payload: StatusSyncTestData.payload(captureId: "cap-1", status: SyncCaptureStatus.reviewReady))
        api.pullResults = [
            StatusSyncTestData.page([change], nextCursor: 1),
            StatusSyncTestData.page([change], nextCursor: 1),
        ]

        await coordinator.pullNow()
        await coordinator.pullNow()

        XCTAssertEqual(notifier.posted, [.reviewReady(captureId: "cap-1")])
    }

    func testAuthorizationIsRequestedFromThePullPath() async {
        await coordinator.pullNow()

        XCTAssertEqual(notifier.authorizationRequests, 1)
    }

    // MARK: - Guards

    func testUnpairedDeviceNeverPulls() async {
        settings.set(nil, for: AppSettingsKey.deviceId)

        await coordinator.pullNow()

        XCTAssertTrue(api.pullCursors.isEmpty)
        // Nothing was attempted, so the notification prompt stays away too.
        XCTAssertEqual(notifier.authorizationRequests, 0)
    }

    func testSyncDisabledNeverPulls() async {
        settings.set(false, for: AppSettingsKey.syncEnabled)

        await coordinator.pullNow()

        XCTAssertTrue(api.pullCursors.isEmpty)
    }
}

/// `CaptureStatusStore` whose writes always fail, standing in for a full disk
/// or a corrupt database.
private final class FailingCaptureStatusStore: CaptureStatusStore {
    private var error: SQLiteError { SQLiteError(code: 13, message: "database or disk is full") }

    func upsert(_ record: CaptureStatusRecord) throws { throw error }
    func status(captureId: String) throws -> CaptureStatusRecord? { throw error }
    func allStatuses() throws -> [CaptureStatusRecord] { throw error }
    func delete(captureId: String) throws { throw error }
}
