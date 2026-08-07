import XCTest
@testable import MemoryLineCompanion

final class CaptureStoreTests: XCTestCase {
    private var databaseURL: URL!
    private var database: SQLiteDatabase!
    private var store: SQLiteCaptureStore!

    override func setUpWithError() throws {
        databaseURL = TestSupport.temporaryDatabaseURL()
        database = try AppDatabase.open(at: databaseURL)
        store = SQLiteCaptureStore(database: database)
    }

    override func tearDownWithError() throws {
        store = nil
        database = nil
        TestSupport.removeDatabaseFiles(at: databaseURL)
    }

    // MARK: - Round trip

    func testInsertFetchRoundTripPreservesEveryField() throws {
        var capture = TestSupport.makeCapture(
            id: "cap-roundtrip",
            type: .tripLog,
            state: .uploaded,
            capturedAt: Date(timeIntervalSince1970: 1_754_500_000.123),
            createdAt: Date(timeIntervalSince1970: 1_754_500_001.456))
        capture.titleHint = "Diner off route 66"
        capture.userNote = "Ask about the pie"
        capture.byteLength = 123_456_789
        capture.sha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        capture.durationSeconds = 61.25
        capture.uploadAttempts = 3
        capture.lastError = "HTTP 503"
        capture.artifactId = "artifact-9"
        capture.uploadedAt = Date(timeIntervalSince1970: 1_754_500_100.789)

        try store.insert(capture)
        let fetched = try XCTUnwrap(store.capture(id: "cap-roundtrip"))

        XCTAssertEqual(fetched.id, capture.id)
        XCTAssertEqual(fetched.type, capture.type)
        XCTAssertEqual(fetched.state, capture.state)
        assertSameInstant(fetched.capturedAt, capture.capturedAt)
        XCTAssertEqual(fetched.timezoneId, capture.timezoneId)
        XCTAssertEqual(fetched.localOffsetMinutes, capture.localOffsetMinutes)
        XCTAssertEqual(fetched.titleHint, capture.titleHint)
        XCTAssertEqual(fetched.userNote, capture.userNote)
        XCTAssertEqual(fetched.fileName, capture.fileName)
        XCTAssertEqual(fetched.byteLength, capture.byteLength)
        XCTAssertEqual(fetched.sha256, capture.sha256)
        XCTAssertEqual(fetched.durationSeconds, capture.durationSeconds, accuracy: 0.0001)
        assertSameInstant(fetched.createdAt, capture.createdAt)
        XCTAssertEqual(fetched.uploadAttempts, capture.uploadAttempts)
        XCTAssertEqual(fetched.lastError, capture.lastError)
        XCTAssertEqual(fetched.artifactId, capture.artifactId)
        assertSameInstant(try XCTUnwrap(fetched.uploadedAt), try XCTUnwrap(capture.uploadedAt))
    }

    func testNilOptionalFieldsRoundTripAsNil() throws {
        let capture = TestSupport.makeCapture(id: "cap-nils")
        try store.insert(capture)
        let fetched = try XCTUnwrap(store.capture(id: "cap-nils"))
        XCTAssertNil(fetched.titleHint)
        XCTAssertNil(fetched.userNote)
        XCTAssertNil(fetched.lastError)
        XCTAssertNil(fetched.artifactId)
        XCTAssertNil(fetched.uploadedAt)
    }

    func testUpdatePersistsChanges() throws {
        var capture = TestSupport.makeCapture(id: "cap-update", state: .savedLocal)
        try store.insert(capture)

        capture.state = .failedRecoverable
        capture.uploadAttempts = 2
        capture.lastError = "network unreachable"
        capture.byteLength = 42_000
        capture.sha256 = "deadbeef"
        capture.durationSeconds = 12.5
        capture.artifactId = "artifact-1"
        try store.update(capture)

        let fetched = try XCTUnwrap(store.capture(id: "cap-update"))
        XCTAssertEqual(fetched.state, .failedRecoverable)
        XCTAssertEqual(fetched.uploadAttempts, 2)
        XCTAssertEqual(fetched.lastError, "network unreachable")
        XCTAssertEqual(fetched.byteLength, 42_000)
        XCTAssertEqual(fetched.sha256, "deadbeef")
        XCTAssertEqual(fetched.durationSeconds, 12.5, accuracy: 0.0001)
        XCTAssertEqual(fetched.artifactId, "artifact-1")
    }

    func testFetchMissingCaptureReturnsNil() throws {
        XCTAssertNil(try store.capture(id: "no-such-id"))
    }

    // MARK: - Queries

    func testAllCapturesNewestFirst() throws {
        let base = Date(timeIntervalSince1970: 1_754_500_000)
        try store.insert(TestSupport.makeCapture(id: "old", createdAt: base))
        try store.insert(TestSupport.makeCapture(id: "newest", createdAt: base.addingTimeInterval(20)))
        try store.insert(TestSupport.makeCapture(id: "middle", createdAt: base.addingTimeInterval(10)))

        XCTAssertEqual(try store.allCaptures().map(\.id), ["newest", "middle", "old"])
    }

    func testPendingUploadsFiltersStatesAndOrdersOldestFirst() throws {
        let base = Date(timeIntervalSince1970: 1_754_500_000)
        try store.insert(TestSupport.makeCapture(id: "done", state: .uploaded, createdAt: base))
        try store.insert(TestSupport.makeCapture(id: "live", state: .recording, createdAt: base.addingTimeInterval(5)))
        try store.insert(TestSupport.makeCapture(id: "queued", state: .queuedUpload, createdAt: base.addingTimeInterval(10)))
        try store.insert(TestSupport.makeCapture(id: "gone", state: .cancelled, createdAt: base.addingTimeInterval(15)))
        try store.insert(TestSupport.makeCapture(id: "failed", state: .failedRecoverable, createdAt: base.addingTimeInterval(20)))
        try store.insert(TestSupport.makeCapture(id: "inflight", state: .uploading, createdAt: base.addingTimeInterval(25)))
        try store.insert(TestSupport.makeCapture(id: "saved", state: .savedLocal, createdAt: base.addingTimeInterval(30)))
        try store.insert(TestSupport.makeCapture(id: "remote", state: .processingRemote, createdAt: base.addingTimeInterval(35)))

        // Only saved_local / queued_upload / failed_recoverable, oldest first.
        XCTAssertEqual(try store.pendingUploads().map(\.id), ["queued", "failed", "saved"])
        XCTAssertEqual(try store.pendingUploadCount(), 3)
    }

    func testAbandonedRecordingsReturnsOnlyRecordingState() throws {
        let base = Date(timeIntervalSince1970: 1_754_500_000)
        try store.insert(TestSupport.makeCapture(id: "finished", state: .savedLocal, createdAt: base))
        try store.insert(TestSupport.makeCapture(id: "crashed-late", state: .recording, createdAt: base.addingTimeInterval(10)))
        try store.insert(TestSupport.makeCapture(id: "crashed-early", state: .recording, createdAt: base.addingTimeInterval(5)))

        XCTAssertEqual(try store.abandonedRecordings().map(\.id), ["crashed-early", "crashed-late"])
    }

    func testPendingUploadCountEmptyDatabaseIsZero() throws {
        XCTAssertEqual(try store.pendingUploadCount(), 0)
    }

    func testDeleteRemovesCapture() throws {
        try store.insert(TestSupport.makeCapture(id: "cap-delete"))
        try store.delete(id: "cap-delete")
        XCTAssertNil(try store.capture(id: "cap-delete"))
        XCTAssertTrue(try store.allCaptures().isEmpty)
    }

    func testInsertDuplicateIdThrows() throws {
        try store.insert(TestSupport.makeCapture(id: "cap-dup"))
        XCTAssertThrowsError(try store.insert(TestSupport.makeCapture(id: "cap-dup"))) { error in
            XCTAssertTrue(error is SQLiteError)
        }
    }

    // MARK: - Helpers

    private func assertSameInstant(
        _ actual: Date, _ expected: Date,
        file: StaticString = #filePath, line: UInt = #line
    ) {
        XCTAssertEqual(
            actual.timeIntervalSince1970, expected.timeIntervalSince1970,
            accuracy: 0.01, file: file, line: line)
    }
}
