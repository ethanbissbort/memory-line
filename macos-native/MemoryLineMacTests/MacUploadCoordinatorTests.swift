import XCTest
@testable import MemoryLineMac

/// Covers the macOS upload pipeline's contract with the sync service: the order
/// of the four requests, the stability of the artifact ID across retries, and
/// the retryable-vs-terminal failure policy that decides whether a capture is
/// parked for later or the whole pass stops.
///
/// The stores are real (SQLite in a temp directory) and only the `SyncAPI` is
/// faked. Persistence is shared code the iOS suite already exercises, but the
/// coordinator's *use* of it — which write happens before which request — is
/// what makes a crash mid-upload resume instead of duplicating, so it is tested
/// against the real store rather than a spy.
final class MacUploadCoordinatorTests: XCTestCase {

    // MARK: - Fixture

    /// Per-test fixture. Built inside each test rather than in setUp: the
    /// coordinator is @MainActor, and overriding XCTest's nonisolated
    /// setUpWithError from a @MainActor class is an isolation mismatch.
    /// The temp directory is removed by the test via `addTeardownBlock`.
    private struct Fixture {
        let directory: URL
        let recordings: URL
        let parts: URL
        let settings: SQLiteSettingsStore
        let captures: SQLiteCaptureStore
        /// Points the coordinator at this fixture's temp directories instead of
        /// the app container's real Application Support / Caches.
        let locations: MacUploadFileLocations
    }

    private func makeFixture() throws -> Fixture {
        let directory = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("macupload-\(UUID().uuidString)", isDirectory: true)
        let recordings = directory.appendingPathComponent("Recordings", isDirectory: true)
        let parts = directory.appendingPathComponent("UploadParts", isDirectory: true)
        try FileManager.default.createDirectory(at: recordings, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: parts, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: directory) }

        let database = try AppDatabase.open(at: directory.appendingPathComponent("memoryline.db"))
        let settings = SQLiteSettingsStore(database: database)
        let captures = SQLiteCaptureStore(database: database)

        // The coordinator refuses to touch the network unless this Mac looks
        // paired — the same three conditions MacSyncCoordinator checks.
        settings.set("https://example.test", for: AppSettingsKey.serverURL)
        settings.set("device-1", for: AppSettingsKey.deviceId)
        settings.set(true, for: AppSettingsKey.syncEnabled)

        return Fixture(
            directory: directory,
            recordings: recordings,
            parts: parts,
            settings: settings,
            captures: captures,
            locations: MacUploadFileLocations(
                recordingURL: { recordings.appendingPathComponent($0, isDirectory: false) },
                partsDirectory: { parts }))
    }

    /// Inserts a capture whose audio file really exists, with a real SHA-256 —
    /// the coordinator refuses to upload anything whose hash or byte length is
    /// missing, and it checks the on-disk size against the record.
    ///
    /// `order` fixes `createdAt` so `pendingUploads()` (created_at ASC) returns
    /// multi-capture fixtures in a known sequence; two captures inserted in the
    /// same instant would otherwise tie and fall back to UUID order.
    @discardableResult
    private func makeCapture(
        in fixture: Fixture,
        order: Int = 0,
        byteLength: Int = 4096,
        state: CaptureLifecycleState = .savedLocal
    ) throws -> CaptureRecord {
        var record = CaptureRecord.newLocal(type: .memory, fileName: "\(UUID().uuidString).wav")
        let url = fixture.recordings.appendingPathComponent(record.fileName, isDirectory: false)
        try Self.audioBytes(count: byteLength).write(to: url)

        record.state = state
        record.byteLength = Int64(byteLength)
        record.sha256 = try FileHasher.sha256Hex(of: url)
        record.durationSeconds = 1
        record.createdAt = Date(timeIntervalSince1970: 1_700_000_000 + Double(order))
        try fixture.captures.insert(record)
        return record
    }

    /// Deterministic pseudo-audio. The pattern's period (251) does not divide
    /// the part sizes used here, so every part differs from every other one and
    /// a mis-ordered or short slice cannot reassemble by luck.
    private static func audioBytes(count: Int) -> Data {
        Data((0..<count).map { UInt8(($0 &* 31 &+ 7) % 251) })
    }

    /// @MainActor because the coordinator's initializer is: constructing it from
    /// a nonisolated helper would be an isolation violation.
    @MainActor
    private func makeCoordinator(_ fixture: Fixture, api: FakeSyncAPI) -> MacUploadCoordinator {
        MacUploadCoordinator(
            store: fixture.captures,
            settings: fixture.settings,
            api: api,
            locations: fixture.locations)
    }

    // MARK: - Happy path

    @MainActor
    func testHappyPathDrivesTheServerSequenceInOrder() async throws {
        let f = try makeFixture()
        let record = try makeCapture(in: f)
        let api = FakeSyncAPI()
        let coordinator = makeCoordinator(f, api: api)

        await coordinator.drainPendingUploads()

        XCTAssertEqual(
            api.log,
            ["createCapture", "initiateArtifact", "uploadArtifactPart(1)", "completeArtifact"],
            "create → initiate → parts → complete is a server contract, not a local choice")

        let stored = try XCTUnwrap(f.captures.capture(id: record.id))
        XCTAssertEqual(stored.state, .uploaded)
        XCTAssertNotNil(stored.uploadedAt)
        XCTAssertNil(stored.lastError)
        XCTAssertEqual(stored.uploadAttempts, 1)
        XCTAssertNotNil(stored.artifactId, "the artifact ID must be persisted so a later pass can resume")

        XCTAssertEqual(coordinator.state, .idle)
        XCTAssertEqual(coordinator.pendingCount, 0)
        XCTAssertNotNil(coordinator.lastUploadedAt)
        XCTAssertFalse(coordinator.isDraining)
    }

    /// The Mac records WAV, not the phone's m4a. Declaring the iOS media type
    /// would make Windows give the downloaded artifact an `.m4a` extension
    /// (RemoteChangeApplier.ExtensionFor) and mislabel the download's
    /// Content-Type.
    @MainActor
    func testArtifactIsDeclaredWithTheRecordingsRealMediaType() async throws {
        let f = try makeFixture()
        let record = try makeCapture(in: f)
        let api = FakeSyncAPI()

        await makeCoordinator(f, api: api).drainPendingUploads()

        let declaration = try XCTUnwrap(api.initiateRequests.first)
        XCTAssertEqual(declaration.mediaType, "audio/wav")
        XCTAssertEqual(declaration.artifactType, "audio_original")
        XCTAssertEqual(declaration.originalFileName, record.fileName)
        XCTAssertEqual(declaration.expectedByteLength, record.byteLength)
        XCTAssertEqual(declaration.expectedSha256, record.sha256)
    }

    @MainActor
    func testMultiPartUploadSendsEveryPartInOrderAndReassemblesTheFile() async throws {
        let f = try makeFixture()
        let record = try makeCapture(in: f, byteLength: 4096)
        let api = FakeSyncAPI()
        api.partSizeBytes = 1024 // forces a 4-part plan

        await makeCoordinator(f, api: api).drainPendingUploads()

        XCTAssertEqual(api.log, [
            "createCapture",
            "initiateArtifact",
            "uploadArtifactPart(1)",
            "uploadArtifactPart(2)",
            "uploadArtifactPart(3)",
            "uploadArtifactPart(4)",
            "completeArtifact",
        ])
        XCTAssertEqual(
            api.receivedBytes, Self.audioBytes(count: 4096),
            "the parts must concatenate back into the original file, in order")
        XCTAssertEqual(api.completeRequests.first?.partCount, 4)
        XCTAssertEqual(api.completeRequests.first?.sha256, record.sha256)
        XCTAssertEqual(
            try FileManager.default.contentsOfDirectory(atPath: f.parts.path), [],
            "part slices are scratch space and must not survive the upload")
        XCTAssertEqual(try XCTUnwrap(f.captures.capture(id: record.id)).state, .uploaded)
    }

    // MARK: - Failure policy

    @MainActor
    func testRetryableFailureParksTheCaptureAndReusesTheArtifactIdOnRetry() async throws {
        let f = try makeFixture()
        let record = try makeCapture(in: f)
        let api = FakeSyncAPI()
        api.fail("initiateArtifact", with: SyncAPIError(
            statusCode: 503, apiError: nil, retryable: true, message: "Service unavailable."))
        let coordinator = makeCoordinator(f, api: api)

        await coordinator.drainPendingUploads()

        let afterFailure = try XCTUnwrap(f.captures.capture(id: record.id))
        XCTAssertEqual(afterFailure.state, .failedRecoverable, "a retryable failure must stay retryable")
        XCTAssertTrue(afterFailure.state.isUploadPending)
        XCTAssertEqual(afterFailure.uploadAttempts, 1)
        XCTAssertNotNil(afterFailure.lastError)
        let artifactId = try XCTUnwrap(afterFailure.artifactId, "the artifact ID is persisted before initiate")
        XCTAssertEqual(coordinator.state, .failed("Service unavailable."))
        XCTAssertEqual(coordinator.pendingCount, 1)

        // Second drain: same capture, same artifact.
        await coordinator.drainPendingUploads()

        let afterRetry = try XCTUnwrap(f.captures.capture(id: record.id))
        XCTAssertEqual(afterRetry.state, .uploaded)
        XCTAssertEqual(afterRetry.uploadAttempts, 2, "the attempt count accumulates across drains")
        XCTAssertNil(afterRetry.lastError)
        XCTAssertEqual(afterRetry.artifactId, artifactId)
        XCTAssertEqual(api.initiateRequests.count, 2)
        XCTAssertEqual(
            api.initiateRequests.compactMap(\.artifactId), [artifactId, artifactId],
            "a retry must re-declare the SAME artifact, or every attempt orphans a new one server-side")
        XCTAssertEqual(coordinator.state, .idle)
        XCTAssertEqual(coordinator.pendingCount, 0)
    }

    /// A retryable failure is a condition every later capture would hit too, so
    /// the pass stops rather than burning the rest of the queue's attempts.
    @MainActor
    func testRetryableFailureAbortsTheRestOfThePass() async throws {
        let f = try makeFixture()
        let first = try makeCapture(in: f, order: 0)
        let second = try makeCapture(in: f, order: 1)
        let api = FakeSyncAPI()
        api.fail(
            "createCapture",
            with: SyncAPIError(
                statusCode: nil, apiError: nil, retryable: true, message: "The network is unavailable."),
            times: 10)

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.drainPendingUploads()

        XCTAssertEqual(api.log, ["createCapture"], "the pass must stop at the first retryable failure")
        XCTAssertEqual(try XCTUnwrap(f.captures.capture(id: first.id)).state, .failedRecoverable)
        XCTAssertEqual(
            try XCTUnwrap(f.captures.capture(id: second.id)).state, .savedLocal,
            "the second capture must be left untouched, not attempted")
        XCTAssertEqual(coordinator.pendingCount, 2)
    }

    /// A non-retryable 4xx will never succeed for that capture, so it is parked
    /// once and the pass moves on — and, crucially, it is attempted exactly
    /// once, never re-queued inside the same drain.
    @MainActor
    func testNonRetryableFailureIsAttemptedOnceAndDoesNotStopThePass() async throws {
        let f = try makeFixture()
        let bad = try makeCapture(in: f, order: 0)
        let good = try makeCapture(in: f, order: 1)
        let api = FakeSyncAPI()
        // Exactly one scripted failure, so it lands on `bad` and `good` then
        // uploads cleanly. A retry inside the same drain would still be caught:
        // it would put a third "createCapture" in the log, which the count
        // assertion below rejects. (Scripting the failure ten deep instead
        // would fail `good` too, which is the pass stopping — the very thing
        // this test exists to prove does not happen.)
        api.fail(
            "createCapture",
            with: SyncAPIError(
                statusCode: 422, apiError: nil, retryable: false, message: "The capture was rejected."),
            times: 1)

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.drainPendingUploads()

        XCTAssertEqual(api.log, [
            "createCapture",              // bad: rejected, and the pass continues
            "createCapture",              // good
            "initiateArtifact",
            "uploadArtifactPart(1)",
            "completeArtifact",
        ])
        XCTAssertEqual(
            api.log.filter { $0 == "createCapture" }.count, 2,
            "exactly one attempt per capture per drain — never a retry loop")

        let parked = try XCTUnwrap(f.captures.capture(id: bad.id))
        XCTAssertEqual(parked.state, .failedRecoverable)
        XCTAssertEqual(parked.lastError, "The capture was rejected.")
        XCTAssertEqual(try XCTUnwrap(f.captures.capture(id: good.id)).state, .uploaded)
        XCTAssertEqual(
            coordinator.state, .failed("The capture was rejected."),
            "the failure still has to reach the UI even though the pass finished")
        XCTAssertEqual(coordinator.pendingCount, 1)
    }

    /// 401 means the device was revoked. Every remaining capture would fail the
    /// same way, and the message has to tell the user what to actually do.
    @MainActor
    func testRevokedDeviceStopsThePassWithARePairMessage() async throws {
        let f = try makeFixture()
        let first = try makeCapture(in: f, order: 0)
        try makeCapture(in: f, order: 1)
        let api = FakeSyncAPI()
        api.fail(
            "createCapture",
            with: SyncAPIError(statusCode: 401, apiError: nil, retryable: false, message: "Unauthorized"),
            times: 10)

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.drainPendingUploads()

        XCTAssertEqual(api.log, ["createCapture"])
        XCTAssertEqual(try XCTUnwrap(f.captures.capture(id: first.id)).state, .failedRecoverable)
        guard case .failed(let message) = coordinator.state else {
            return XCTFail("a revoked device must surface as .failed, got \(coordinator.state)")
        }
        XCTAssertTrue(message.contains("Re-pair"), "the message must say how to recover, got: \(message)")
    }

    /// The preconditions are checked before anything is sent, and the reason
    /// shown to the user must not leak the container path (design §14.5).
    @MainActor
    func testMissingAudioFileFailsWithoutCallingTheServer() async throws {
        let f = try makeFixture()
        let record = try makeCapture(in: f)
        try FileManager.default.removeItem(
            at: f.recordings.appendingPathComponent(record.fileName, isDirectory: false))
        let api = FakeSyncAPI()

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.drainPendingUploads()

        XCTAssertEqual(api.log, [], "a capture with no bytes must never reach the network")
        let stored = try XCTUnwrap(f.captures.capture(id: record.id))
        XCTAssertEqual(stored.state, .failedRecoverable)
        let reason = try XCTUnwrap(stored.lastError)
        XCTAssertFalse(reason.contains(f.recordings.path), "display text must not carry file paths")
        XCTAssertFalse(reason.contains(record.fileName), "display text must not carry file names")
    }

    // MARK: - Idempotency and durability

    @MainActor
    func testAlreadyUploadedCaptureIsNotReUploaded() async throws {
        let f = try makeFixture()
        var record = try makeCapture(in: f)
        record.state = .uploaded
        record.uploadedAt = Date(timeIntervalSince1970: 1_700_000_500)
        record.artifactId = "already-there"
        try f.captures.update(record)
        let api = FakeSyncAPI()

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.drainPendingUploads()

        XCTAssertEqual(api.log, [], "an uploaded capture is not upload-pending and must be left alone")
        let stored = try XCTUnwrap(f.captures.capture(id: record.id))
        XCTAssertEqual(stored.state, .uploaded)
        XCTAssertEqual(stored.uploadAttempts, 0, "no attempt was made, so nothing was counted")
        XCTAssertEqual(coordinator.pendingCount, 0)
        XCTAssertEqual(coordinator.state, .idle)
    }

    /// A capture stranded in `.uploading` by a crash is invisible to
    /// `pendingUploads()`, so it must be reparked before the queue is read — and
    /// it must resume onto the artifact the interrupted attempt already
    /// declared, not a fresh one.
    @MainActor
    func testInterruptedUploadResumesOntoTheSameArtifact() async throws {
        let f = try makeFixture()
        var record = try makeCapture(in: f)
        record.state = .uploading
        record.uploadAttempts = 1
        record.artifactId = "artifact-from-the-interrupted-pass"
        try f.captures.update(record)
        let api = FakeSyncAPI()

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.drainPendingUploads()

        let stored = try XCTUnwrap(f.captures.capture(id: record.id))
        XCTAssertEqual(stored.state, .uploaded)
        XCTAssertEqual(stored.uploadAttempts, 2)
        XCTAssertEqual(stored.artifactId, "artifact-from-the-interrupted-pass")
        XCTAssertEqual(api.initiateRequests.count, 1)
        XCTAssertEqual(
            api.initiateRequests.compactMap(\.artifactId), ["artifact-from-the-interrupted-pass"],
            "resuming must re-declare the interrupted artifact, never mint a second one")
        XCTAssertEqual(api.log, [
            "createCapture", "initiateArtifact", "uploadArtifactPart(1)", "completeArtifact",
        ])
    }

    /// The protocol promises `drainPendingUploads()` is safe to call while a
    /// pass is running. The second call is made from *inside* the first (the
    /// fake calls back mid-`createCapture`), which pins the guard without racing
    /// a sleep.
    @MainActor
    func testASecondDrainWhileOneIsInFlightDoesNotDoubleUpload() async throws {
        let f = try makeFixture()
        let record = try makeCapture(in: f)
        let api = FakeSyncAPI()
        let coordinator = makeCoordinator(f, api: api)
        // Fires once, on the first createCapture, so it cannot recurse even if
        // the guard were broken. Cleared afterwards to break the api → closure →
        // coordinator → api retain cycle.
        api.onFirstCreateCapture = { await coordinator.drainPendingUploads() }
        defer { api.onFirstCreateCapture = nil }

        await coordinator.drainPendingUploads()

        XCTAssertEqual(
            api.log,
            ["createCapture", "initiateArtifact", "uploadArtifactPart(1)", "completeArtifact"],
            "the reentrant drain must return immediately, leaving exactly one upload sequence")
        let stored = try XCTUnwrap(f.captures.capture(id: record.id))
        XCTAssertEqual(stored.state, .uploaded)
        XCTAssertEqual(stored.uploadAttempts, 1, "a second pass over the same row would bump this to 2")
        XCTAssertFalse(coordinator.isDraining)
    }

    // MARK: - Pairing guard

    @MainActor
    func testUnpairedOrDisabledMacNeverTouchesTheNetwork() async throws {
        let f = try makeFixture()
        try makeCapture(in: f)
        f.settings.set(nil, for: AppSettingsKey.deviceId)
        let api = FakeSyncAPI()

        let coordinator = makeCoordinator(f, api: api)
        await coordinator.drainPendingUploads()

        XCTAssertFalse(coordinator.canUpload)
        XCTAssertEqual(api.log, [])
        XCTAssertEqual(coordinator.pendingCount, 1, "the capture stays queued for after pairing")
        guard case .failed(let message) = coordinator.state else {
            return XCTFail("a queued capture with nowhere to go must be visible, got \(coordinator.state)")
        }
        XCTAssertFalse(message.isEmpty)

        // The same guard from the other direction: paired, but sync switched off.
        f.settings.set("device-1", for: AppSettingsKey.deviceId)
        f.settings.set(false, for: AppSettingsKey.syncEnabled)
        await coordinator.drainPendingUploads()

        XCTAssertFalse(coordinator.canUpload)
        XCTAssertEqual(api.log, [])
    }

    // MARK: - Kicks

    @MainActor
    func testCaptureFinalizedKicksADrain() async throws {
        let f = try makeFixture()
        let api = FakeSyncAPI()
        let coordinator = makeCoordinator(f, api: api)
        let record = try makeCapture(in: f)

        coordinator.captureFinalized(record)

        try await waitFor("the finalized capture to upload") {
            try f.captures.capture(id: record.id)?.state == .uploaded
        }
        XCTAssertEqual(
            api.log,
            ["createCapture", "initiateArtifact", "uploadArtifactPart(1)", "completeArtifact"])
    }

    /// macOS has no BGTaskScheduler, so retries ride a foreground loop. The
    /// interval is short here because the test waits on the outcome, not on the
    /// clock.
    @MainActor
    func testPeriodicDrainUploadsWithoutAnExplicitCall() async throws {
        let f = try makeFixture()
        let record = try makeCapture(in: f)
        let api = FakeSyncAPI()
        let coordinator = makeCoordinator(f, api: api)
        defer { coordinator.stopPeriodicDrain() }

        coordinator.startPeriodicDrain(interval: .milliseconds(20))

        try await waitFor("the ticker to drain the queue") {
            try f.captures.capture(id: record.id)?.state == .uploaded
        }
    }

    // MARK: - Helpers

    /// Polls `condition` on the main actor until it holds, bounded so a genuine
    /// regression fails the test instead of hanging the suite. Used only for the
    /// two fire-and-forget paths (`captureFinalized`, the ticker), which have no
    /// value to await.
    @MainActor
    private func waitFor(
        _ description: String,
        timeout: TimeInterval = 5,
        until condition: () throws -> Bool
    ) async rethrows {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if try condition() { return }
            try? await Task.sleep(for: .milliseconds(2))
        }
        XCTFail("timed out waiting for \(description)")
    }
}

/// Minimal `SyncAPI` double for the upload path. Every call is appended to `log`
/// *before* any scripted failure is thrown, so the log records what the
/// coordinator attempted — which is the ordering contract under test. The device
/// and change-feed calls trap, so an unexpected call is a loud failure rather
/// than a silent no-op.
///
/// The coordinator's per-capture pipeline is `nonisolated` and therefore runs
/// off the main actor, while the assertions read this object from the main
/// actor — hence the lock rather than the bare `var`s a single-threaded double
/// would use.
private final class FakeSyncAPI: SyncAPI, @unchecked Sendable {
    private let lock = NSLock()
    private var _log: [String] = []
    private var _initiateRequests: [ArtifactInitiateRequest] = []
    private var _completeRequests: [ArtifactCompleteRequest] = []
    private var _receivedBytes = Data()
    private var _partSizeBytes: Int64 = 8 * 1024 * 1024
    private var _failures: [String: [SyncAPIError]] = [:]
    private var _onFirstCreateCapture: (@Sendable () async -> Void)?
    private var _createCaptureCount = 0

    /// Ordered call log. Part uploads carry their part number:
    /// `uploadArtifactPart(1)`.
    var log: [String] { lock.withLock { _log } }
    var initiateRequests: [ArtifactInitiateRequest] { lock.withLock { _initiateRequests } }
    var completeRequests: [ArtifactCompleteRequest] { lock.withLock { _completeRequests } }
    /// Every part's bytes, concatenated in the order they were uploaded.
    var receivedBytes: Data { lock.withLock { _receivedBytes } }

    /// Part size this fake's plan advertises; the part count is derived from the
    /// declared byte length exactly as the service derives it.
    var partSizeBytes: Int64 {
        get { lock.withLock { _partSizeBytes } }
        set { lock.withLock { _partSizeBytes = newValue } }
    }

    /// Invoked from inside the *first* `createCapture`, before it returns. Lets
    /// a test act while a drain is provably mid-flight.
    var onFirstCreateCapture: (@Sendable () async -> Void)? {
        get { lock.withLock { _onFirstCreateCapture } }
        set { lock.withLock { _onFirstCreateCapture = newValue } }
    }

    /// Scripts the next `times` calls to `call` to throw `error`. Keys are the
    /// bare method names: `createCapture`, `initiateArtifact`,
    /// `uploadArtifactPart`, `completeArtifact`.
    func fail(_ call: String, with error: SyncAPIError, times: Int = 1) {
        lock.withLock {
            _failures[call, default: []].append(contentsOf: Array(repeating: error, count: times))
        }
    }

    private func record(_ call: String) {
        lock.withLock { _log.append(call) }
    }

    private func scriptedFailure(for call: String) -> SyncAPIError? {
        return lock.withLock { () -> SyncAPIError? in
            guard var queue = _failures[call], !queue.isEmpty else { return nil }
            let error = queue.removeFirst()
            _failures[call] = queue
            return error
        }
    }

    // MARK: SyncAPI

    func createCapture(_ request: CaptureCreateRequest) async throws -> CaptureResponse {
        record("createCapture")
        let isFirstCall: Bool = lock.withLock {
            _createCaptureCount += 1
            return _createCaptureCount == 1
        }
        if isFirstCall, let hook = onFirstCreateCapture {
            await hook()
        }
        if let error = scriptedFailure(for: "createCapture") { throw error }
        return CaptureResponse(
            captureId: request.captureId,
            sourceDeviceId: request.sourceDeviceId,
            sourcePlatform: "other",
            captureType: request.captureType,
            capturedAtUtc: request.capturedAtUtc,
            status: SyncCaptureStatus.localOnly,
            serverRevision: 1,
            createdAtUtc: Date(timeIntervalSince1970: 1_700_000_000),
            artifacts: nil)
    }

    func initiateArtifact(
        captureId: String,
        _ request: ArtifactInitiateRequest
    ) async throws -> ArtifactInitiateResponse {
        record("initiateArtifact")
        lock.withLock { _initiateRequests.append(request) }
        if let error = scriptedFailure(for: "initiateArtifact") { throw error }
        let partSize = partSizeBytes
        let partCount = Int(max(1, (request.expectedByteLength + partSize - 1) / partSize))
        // Echoes the requested ID, as the service does for an idempotent
        // re-declaration — which is what makes the stable-ID assertions mean
        // something.
        return ArtifactInitiateResponse(
            artifactId: request.artifactId ?? UUID().uuidString.lowercased(),
            partSizeBytes: partSize,
            expectedPartCount: partCount)
    }

    func uploadArtifactPart(artifactId: String, partNumber: Int, fileURL: URL) async throws {
        record("uploadArtifactPart(\(partNumber))")
        // Read now, not later: the coordinator deletes each slice as soon as its
        // part succeeds.
        let bytes = (try? Data(contentsOf: fileURL)) ?? Data()
        if let error = scriptedFailure(for: "uploadArtifactPart") { throw error }
        lock.withLock { _receivedBytes.append(bytes) }
    }

    func completeArtifact(
        artifactId: String,
        _ request: ArtifactCompleteRequest
    ) async throws -> ArtifactCompleteResponse {
        record("completeArtifact")
        lock.withLock { _completeRequests.append(request) }
        if let error = scriptedFailure(for: "completeArtifact") { throw error }
        return ArtifactCompleteResponse(
            artifactId: artifactId,
            state: "complete",
            byteLength: request.totalByteLength,
            sha256: request.sha256)
    }

    func register(serverURL: String, request: DeviceRegisterRequest) async throws -> DeviceRegisterResponse {
        fatalError("unused")
    }
    func revokeDevice() async throws { fatalError("unused") }
    func pull(cursor: Int64, limit: Int) async throws -> SyncPullResponse { fatalError("unused") }
    func ack(cursor: Int64) async throws { fatalError("unused") }
}
