import XCTest
@testable import MemoryLineCompanion

final class CaptureStatusStoreTests: XCTestCase {
    private var databaseURL: URL!
    private var database: SQLiteDatabase!
    private var store: SQLiteCaptureStatusStore!

    override func setUpWithError() throws {
        databaseURL = TestSupport.temporaryDatabaseURL()
        database = try AppDatabase.open(at: databaseURL)
        store = try SQLiteCaptureStatusStore(database: database)
    }

    override func tearDownWithError() throws {
        store = nil
        database = nil
        TestSupport.removeDatabaseFiles(at: databaseURL)
    }

    // MARK: - Round trip

    func testUpsertFetchRoundTripPreservesEveryField() throws {
        let record = CaptureStatusRecord(
            captureId: "cap-1",
            status: SyncCaptureStatus.reviewReady,
            processingStage: "review_ready",
            transcriptAvailable: true,
            transcriptPreview: "We stopped at the diner outside Amarillo",
            transcriptCharCount: 4_213,
            pendingEventCount: 3,
            approvedEventCount: 1,
            failureReason: nil,
            failureRetryable: nil,
            updatedAt: Date(timeIntervalSince1970: 1_754_500_500.75))

        try store.upsert(record)
        let fetched = try XCTUnwrap(store.status(captureId: "cap-1"))

        XCTAssertEqual(fetched.captureId, record.captureId)
        XCTAssertEqual(fetched.status, record.status)
        XCTAssertEqual(fetched.processingStage, record.processingStage)
        XCTAssertEqual(fetched.transcriptAvailable, record.transcriptAvailable)
        XCTAssertEqual(fetched.transcriptPreview, record.transcriptPreview)
        XCTAssertEqual(fetched.transcriptCharCount, record.transcriptCharCount)
        XCTAssertEqual(fetched.pendingEventCount, record.pendingEventCount)
        XCTAssertEqual(fetched.approvedEventCount, record.approvedEventCount)
        XCTAssertNil(fetched.failureReason)
        XCTAssertNil(fetched.failureRetryable)
        XCTAssertEqual(
            fetched.updatedAt.timeIntervalSince1970,
            record.updatedAt.timeIntervalSince1970,
            accuracy: 0.0005)
    }

    /// Every optional is genuinely nullable — an early `received` status has
    /// no transcript and no review counts.
    func testRoundTripWithEveryOptionalNil() throws {
        let record = CaptureStatusRecord(
            captureId: "cap-empty",
            status: SyncCaptureStatus.received,
            processingStage: nil,
            transcriptAvailable: false,
            transcriptPreview: nil,
            transcriptCharCount: nil,
            pendingEventCount: nil,
            approvedEventCount: nil,
            failureReason: nil,
            failureRetryable: nil,
            updatedAt: Date(timeIntervalSince1970: 1_754_500_000))

        try store.upsert(record)
        let fetched = try XCTUnwrap(store.status(captureId: "cap-empty"))

        XCTAssertEqual(fetched, record)
    }

    func testFailureRetryableFalseIsDistinctFromNil() throws {
        var record = CaptureStatusRecord(
            captureId: "cap-failed",
            status: SyncCaptureStatus.failed,
            processingStage: "failed_permanent",
            transcriptAvailable: false,
            transcriptPreview: nil,
            transcriptCharCount: nil,
            pendingEventCount: nil,
            approvedEventCount: nil,
            failureReason: "Transcription model is not configured.",
            failureRetryable: false,
            updatedAt: Date(timeIntervalSince1970: 1_754_500_600))
        try store.upsert(record)
        XCTAssertEqual(try XCTUnwrap(store.status(captureId: "cap-failed")).failureRetryable, false)

        record.failureRetryable = nil
        try store.upsert(record)
        XCTAssertNil(try XCTUnwrap(store.status(captureId: "cap-failed")).failureRetryable)
    }

    // MARK: - Upsert semantics

    func testUpsertReplacesTheExistingRow() throws {
        let first = CaptureStatusRecord(
            captureId: "cap-1",
            status: SyncCaptureStatus.processing,
            processingStage: "transcribing",
            transcriptAvailable: false,
            transcriptPreview: nil,
            transcriptCharCount: nil,
            pendingEventCount: nil,
            approvedEventCount: nil,
            failureReason: nil,
            failureRetryable: nil,
            updatedAt: Date(timeIntervalSince1970: 1_754_500_100))
        var second = first
        second.status = SyncCaptureStatus.reviewReady
        second.processingStage = "review_ready"
        second.transcriptAvailable = true
        second.transcriptPreview = "preview text"
        second.transcriptCharCount = 900
        second.pendingEventCount = 2
        second.updatedAt = Date(timeIntervalSince1970: 1_754_500_200)

        try store.upsert(first)
        try store.upsert(second)

        XCTAssertEqual(try store.allStatuses().count, 1)
        XCTAssertEqual(try store.status(captureId: "cap-1"), second)
    }

    func testAllStatusesIsNewestUpdateFirst() throws {
        for (index, id) in ["a", "b", "c"].enumerated() {
            try store.upsert(CaptureStatusRecord(
                captureId: id,
                status: SyncCaptureStatus.processing,
                processingStage: nil,
                transcriptAvailable: false,
                transcriptPreview: nil,
                transcriptCharCount: nil,
                pendingEventCount: nil,
                approvedEventCount: nil,
                failureReason: nil,
                failureRetryable: nil,
                updatedAt: Date(timeIntervalSince1970: 1_754_500_000 + Double(index))))
        }

        XCTAssertEqual(try store.allStatuses().map(\.captureId), ["c", "b", "a"])
    }

    func testDeleteRemovesTheRow() throws {
        try store.upsert(CaptureStatusRecord(
            captureId: "cap-1",
            status: SyncCaptureStatus.completed,
            processingStage: nil,
            transcriptAvailable: false,
            transcriptPreview: nil,
            transcriptCharCount: nil,
            pendingEventCount: nil,
            approvedEventCount: nil,
            failureReason: nil,
            failureRetryable: nil,
            updatedAt: Date(timeIntervalSince1970: 1_754_500_000)))

        try store.delete(captureId: "cap-1")

        XCTAssertNil(try store.status(captureId: "cap-1"))
    }

    func testUnknownCaptureIdReadsAsNil() throws {
        XCTAssertNil(try store.status(captureId: "never-seen"))
    }

    /// A status may outlive the capture it describes (deleted on the phone),
    /// so there is deliberately no foreign key to `captures`.
    func testStatusCanBeStoredWithoutAMatchingCaptureRow() throws {
        try store.upsert(CaptureStatusRecord(
            captureId: "orphan",
            status: SyncCaptureStatus.completed,
            processingStage: nil,
            transcriptAvailable: false,
            transcriptPreview: nil,
            transcriptCharCount: nil,
            pendingEventCount: nil,
            approvedEventCount: nil,
            failureReason: nil,
            failureRetryable: nil,
            updatedAt: Date(timeIntervalSince1970: 1_754_500_000)))

        XCTAssertNotNil(try store.status(captureId: "orphan"))
    }

    // MARK: - Schema

    func testMigrationIsIdempotentAndPreservesData() throws {
        try store.upsert(CaptureStatusRecord(
            captureId: "survivor",
            status: SyncCaptureStatus.reviewReady,
            processingStage: "review_ready",
            transcriptAvailable: true,
            transcriptPreview: "preview",
            transcriptCharCount: 12,
            pendingEventCount: 1,
            approvedEventCount: 0,
            failureReason: nil,
            failureRetryable: nil,
            updatedAt: Date(timeIntervalSince1970: 1_754_500_000)))

        try CaptureStatusSchema.migrate(database)
        try CaptureStatusSchema.migrate(database)
        // A second store over the same connection re-runs the DDL too.
        _ = try SQLiteCaptureStatusStore(database: database)

        XCTAssertNotNil(try store.status(captureId: "survivor"))
    }

    /// The status schema must not touch `PRAGMA user_version`, or the next
    /// `AppDatabase.open` would reject the file as newer than the app supports.
    func testMigrationLeavesTheAppSchemaVersionAlone() throws {
        XCTAssertEqual(
            try database.scalarInt64("PRAGMA user_version;"),
            Int64(AppDatabase.migrations.count))

        try CaptureStatusSchema.migrate(database)

        XCTAssertEqual(
            try database.scalarInt64("PRAGMA user_version;"),
            Int64(AppDatabase.migrations.count))
        XCTAssertNoThrow(try database.migrate(AppDatabase.migrations))
    }

    func testSchemaSurvivesReopen() throws {
        try store.upsert(CaptureStatusRecord(
            captureId: "persisted",
            status: SyncCaptureStatus.completed,
            processingStage: "completed",
            transcriptAvailable: true,
            transcriptPreview: "preview",
            transcriptCharCount: 7,
            pendingEventCount: 0,
            approvedEventCount: 4,
            failureReason: nil,
            failureRetryable: nil,
            updatedAt: Date(timeIntervalSince1970: 1_754_500_000)))
        store = nil
        database = nil

        let reopened = try AppDatabase.open(at: databaseURL)
        let reopenedStore = try SQLiteCaptureStatusStore(database: reopened)

        XCTAssertEqual(try XCTUnwrap(reopenedStore.status(captureId: "persisted")).approvedEventCount, 4)
    }
}
