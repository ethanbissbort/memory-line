import XCTest
@testable import MemoryLineMac

/// Covers the macOS sync loop's contract with the server: cursor advance/ack
/// ordering, paging, and the last-write-wins rule when a page replays.
///
/// The stores are real (SQLite in a temp file) and only the API is faked —
/// the persistence is shared code that the iOS suite already exercises, but
/// the coordinator's interaction with it is macOS-specific and new.
final class MacSyncCoordinatorTests: XCTestCase {

    // MARK: - Fixture

    /// Per-test fixture. Built inside each test rather than in setUp: the
    /// coordinator is @MainActor, and overriding XCTest's nonisolated
    /// setUpWithError from a @MainActor class is an isolation mismatch.
    /// The temp directory is removed by the test via `addTeardownBlock`.
    private struct Fixture {
        let directory: URL
        let settings: SQLiteSettingsStore
        let statuses: SQLiteCaptureStatusStore
    }

    private func makeFixture() throws -> Fixture {
        let directory = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("macsync-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: directory) }

        let database = try AppDatabase.open(at: directory.appendingPathComponent("memoryline.db"))
        let settings = SQLiteSettingsStore(database: database)
        let statuses = try SQLiteCaptureStatusStore(database: database)

        // The coordinator refuses to run unless this Mac looks paired.
        settings.set("https://example.test", for: AppSettingsKey.serverURL)
        settings.set("device-1", for: AppSettingsKey.deviceId)
        settings.set(true, for: AppSettingsKey.syncEnabled)

        return Fixture(directory: directory, settings: settings, statuses: statuses)
    }

    // MARK: - Tests

    @MainActor
    func testAppliesCaptureStatusAndAcksAdvancedCursor() async throws {
        let f = try makeFixture()
        let api = FakeSyncAPI(pages: [
            SyncPullResponse(
                changes: [statusChange(changeId: 1, captureId: "cap-1", status: "processing")],
                nextCursor: 7,
                hasMore: false)
        ])

        let coordinator = MacSyncCoordinator(settings: f.settings, api: api, statusStore: f.statuses)
        await coordinator.pullNow()

        XCTAssertEqual(coordinator.state, .idle)
        XCTAssertEqual(coordinator.lastAppliedCount, 1)
        XCTAssertEqual(api.ackedCursors, [7], "the advanced cursor must be acked exactly once")
        XCTAssertEqual(f.settings.string(MacSyncSettingsKey.pullCursor), "7")

        let stored = try f.statuses.status(captureId: "cap-1")
        XCTAssertEqual(stored?.status, "processing")
    }

    @MainActor
    func testEmptyPageDoesNotReAckTheSameCursor() async throws {
        let f = try makeFixture()
        f.settings.set("42", for: MacSyncSettingsKey.pullCursor)
        // An empty page echoes the request cursor back.
        let api = FakeSyncAPI(pages: [
            SyncPullResponse(changes: [], nextCursor: 42, hasMore: false)
        ])

        let coordinator = MacSyncCoordinator(settings: f.settings, api: api, statusStore: f.statuses)
        await coordinator.pullNow()

        XCTAssertEqual(api.ackedCursors, [], "a cursor that did not advance must not be acked")
        XCTAssertEqual(coordinator.state, .idle)
    }

    @MainActor
    func testFollowsHasMoreAcrossPages() async throws {
        let f = try makeFixture()
        let api = FakeSyncAPI(pages: [
            SyncPullResponse(
                changes: [statusChange(changeId: 1, captureId: "cap-1", status: "received")],
                nextCursor: 1, hasMore: true),
            SyncPullResponse(
                changes: [statusChange(changeId: 2, captureId: "cap-2", status: "completed")],
                nextCursor: 2, hasMore: false)
        ])

        let coordinator = MacSyncCoordinator(settings: f.settings, api: api, statusStore: f.statuses)
        await coordinator.pullNow()

        XCTAssertEqual(api.pullCount, 2)
        XCTAssertEqual(coordinator.lastAppliedCount, 2)
        XCTAssertEqual(api.ackedCursors, [1, 2])
    }

    @MainActor
    func testOlderReplayedStatusDoesNotOverwriteNewer() async throws {
        let f = try makeFixture()
        let newer = Date(timeIntervalSince1970: 2_000_000)
        let older = Date(timeIntervalSince1970: 1_000_000)

        let api = FakeSyncAPI(pages: [
            SyncPullResponse(
                changes: [statusChange(changeId: 1, captureId: "cap-1", status: "completed", updatedAt: newer)],
                nextCursor: 1, hasMore: false),
            SyncPullResponse(
                changes: [statusChange(changeId: 2, captureId: "cap-1", status: "processing", updatedAt: older)],
                nextCursor: 2, hasMore: false)
        ])

        let coordinator = MacSyncCoordinator(settings: f.settings, api: api, statusStore: f.statuses)
        await coordinator.pullNow()
        await coordinator.pullNow()

        let stored = try f.statuses.status(captureId: "cap-1")
        XCTAssertEqual(stored?.status, "completed", "an older replayed change must not clobber a newer one")
    }

    @MainActor
    func testUnparseablePayloadIsSkippedWithoutStallingTheCursor() async throws {
        let f = try makeFixture()
        var broken = statusChange(changeId: 1, captureId: "cap-1", status: "processing")
        broken.payloadJson = "{ not json"

        let api = FakeSyncAPI(pages: [
            SyncPullResponse(changes: [broken], nextCursor: 5, hasMore: false)
        ])

        let coordinator = MacSyncCoordinator(settings: f.settings, api: api, statusStore: f.statuses)
        await coordinator.pullNow()

        XCTAssertEqual(coordinator.state, .idle, "a bad payload is skipped, not treated as a failure")
        XCTAssertEqual(coordinator.lastAppliedCount, 0)
        XCTAssertEqual(api.ackedCursors, [5], "the cursor still advances past a change we cannot parse")
    }

    @MainActor
    func testNonStatusEntitiesAreIgnored() async throws {
        let f = try makeFixture()
        var capture = statusChange(changeId: 1, captureId: "cap-1", status: "processing")
        capture.entityType = SyncChangeEntityType.capture

        let api = FakeSyncAPI(pages: [
            SyncPullResponse(changes: [capture], nextCursor: 3, hasMore: false)
        ])

        let coordinator = MacSyncCoordinator(settings: f.settings, api: api, statusStore: f.statuses)
        await coordinator.pullNow()

        XCTAssertEqual(coordinator.lastAppliedCount, 0)
        XCTAssertNil(try f.statuses.status(captureId: "cap-1"))
    }

    @MainActor
    func testUnpairedMacDoesNotCallTheServer() async throws {
        let f = try makeFixture()
        f.settings.set(nil, for: AppSettingsKey.deviceId)
        let api = FakeSyncAPI(pages: [])

        let coordinator = MacSyncCoordinator(settings: f.settings, api: api, statusStore: f.statuses)
        await coordinator.pullNow()

        XCTAssertEqual(api.pullCount, 0)
        XCTAssertFalse(coordinator.canSync)
    }

    @MainActor
    func testFailedPullDoesNotAdvanceTheCursor() async throws {
        let f = try makeFixture()
        let api = FakeSyncAPI(pages: [
            SyncPullResponse(
                changes: [statusChange(changeId: 1, captureId: "cap-1", status: "processing")],
                nextCursor: 9, hasMore: false)
        ])
        api.pullError = SyncAPIError(
            statusCode: 503, apiError: nil, retryable: true, message: "Service Unavailable")

        let coordinator = MacSyncCoordinator(settings: f.settings, api: api, statusStore: f.statuses)
        await coordinator.pullNow()

        guard case .failed = coordinator.state else {
            return XCTFail("a failed pull must surface as .failed, got \(coordinator.state)")
        }
        XCTAssertNil(f.settings.string(MacSyncSettingsKey.pullCursor),
                     "a failed pull must not advance the stored cursor")
    }

    // MARK: - Helpers

    private func statusChange(
        changeId: Int64,
        captureId: String,
        status: String,
        updatedAt: Date = Date(timeIntervalSince1970: 1_700_000_000)
    ) -> SyncChangeDto {
        let payload = CaptureStatusChangePayload(
            captureId: captureId,
            status: status,
            processingStage: nil,
            updatedAtUtc: updatedAt,
            transcriptAvailable: false,
            transcriptPreview: nil,
            transcriptCharCount: nil,
            pendingEventCount: nil,
            approvedEventCount: nil,
            failureReason: nil,
            failureRetryable: nil)
        let json = String(data: try! SyncJSON.encoder.encode(payload), encoding: .utf8)

        return SyncChangeDto(
            changeId: changeId,
            entityType: SyncChangeEntityType.captureStatus,
            entityId: captureId,
            operation: "upsert",
            revision: 1,
            changedAtUtc: updatedAt,
            sourceDeviceId: "windows-1",
            payloadJson: json)
    }
}

/// Minimal `SyncAPI` double. Only pull/ack are exercised; everything else traps
/// so an unexpected call is a loud test failure rather than a silent no-op.
private final class FakeSyncAPI: SyncAPI, @unchecked Sendable {
    private var pages: [SyncPullResponse]
    private(set) var pullCount = 0
    private(set) var ackedCursors: [Int64] = []
    var pullError: SyncAPIError?

    init(pages: [SyncPullResponse]) {
        self.pages = pages
    }

    func pull(cursor: Int64, limit: Int) async throws -> SyncPullResponse {
        if let pullError { throw pullError }
        pullCount += 1
        guard !pages.isEmpty else {
            return SyncPullResponse(changes: [], nextCursor: cursor, hasMore: false)
        }
        return pages.removeFirst()
    }

    func ack(cursor: Int64) async throws {
        ackedCursors.append(cursor)
    }

    func register(serverURL: String, request: DeviceRegisterRequest) async throws -> DeviceRegisterResponse {
        fatalError("unused")
    }
    func revokeDevice() async throws { fatalError("unused") }
    func createCapture(_ request: CaptureCreateRequest) async throws -> CaptureResponse {
        fatalError("unused")
    }
    func initiateArtifact(captureId: String, _ request: ArtifactInitiateRequest) async throws -> ArtifactInitiateResponse {
        fatalError("unused")
    }
    func uploadArtifactPart(artifactId: String, partNumber: Int, fileURL: URL) async throws {
        fatalError("unused")
    }
    func completeArtifact(artifactId: String, _ request: ArtifactCompleteRequest) async throws -> ArtifactCompleteResponse {
        fatalError("unused")
    }
}
