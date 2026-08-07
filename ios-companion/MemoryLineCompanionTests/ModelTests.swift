import XCTest
@testable import MemoryLineCompanion

final class ModelTests: XCTestCase {

    // MARK: - State machine flags (truth tables over ALL cases)

    func testIsUploadPendingTruthTable() {
        let expected: Set<CaptureLifecycleState> = [.savedLocal, .queuedUpload, .failedRecoverable]
        for state in CaptureLifecycleState.allCases {
            XCTAssertEqual(
                state.isUploadPending, expected.contains(state),
                "isUploadPending wrong for \(state.rawValue)")
        }
    }

    func testIsDoneUploadingTruthTable() {
        let expected: Set<CaptureLifecycleState> = [.uploaded, .processingRemote, .reviewReady, .completed]
        for state in CaptureLifecycleState.allCases {
            XCTAssertEqual(
                state.isDoneUploading, expected.contains(state),
                "isDoneUploading wrong for \(state.rawValue)")
        }
    }

    func testNoStateIsBothPendingAndDone() {
        for state in CaptureLifecycleState.allCases {
            XCTAssertFalse(
                state.isUploadPending && state.isDoneUploading,
                "\(state.rawValue) is both pending and done")
        }
    }

    // MARK: - Persisted raw values (frozen: they live in SQLite and on the wire)

    func testLifecycleStateRawValuesAreStable() {
        XCTAssertEqual(CaptureLifecycleState.savedLocal.rawValue, "saved_local")
        XCTAssertEqual(CaptureLifecycleState.queuedUpload.rawValue, "queued_upload")
        XCTAssertEqual(CaptureLifecycleState.failedRecoverable.rawValue, "failed_recoverable")
        XCTAssertEqual(CaptureLifecycleState.processingRemote.rawValue, "processing_remote")
        XCTAssertEqual(CaptureLifecycleState.reviewReady.rawValue, "review_ready")
        XCTAssertEqual(CaptureLifecycleState.recording.rawValue, "recording")
        XCTAssertEqual(CaptureLifecycleState.uploading.rawValue, "uploading")
        XCTAssertEqual(CaptureLifecycleState.uploaded.rawValue, "uploaded")
        XCTAssertEqual(CaptureLifecycleState.completed.rawValue, "completed")
        XCTAssertEqual(CaptureLifecycleState.cancelled.rawValue, "cancelled")
    }

    func testCaptureTypeRawValuesAreStable() {
        XCTAssertEqual(CaptureType.tripLog.rawValue, "trip_log")
        XCTAssertEqual(CaptureType.memory.rawValue, "memory")
        XCTAssertEqual(CaptureType.idea.rawValue, "idea")
        XCTAssertEqual(CaptureType.reminder.rawValue, "reminder")
        XCTAssertEqual(CaptureType.question.rawValue, "question")
        XCTAssertEqual(CaptureType.unclassified.rawValue, "unclassified")
    }

    // MARK: - newLocal factory

    func testNewLocalDefaults() throws {
        let timeZone = try XCTUnwrap(TimeZone(identifier: "America/Denver"))
        let capturedAt = Date(timeIntervalSince1970: 1_754_500_000)
        let capture = CaptureRecord.newLocal(
            type: .idea, fileName: "a.m4a", capturedAt: capturedAt, timeZone: timeZone)

        XCTAssertEqual(capture.state, .recording)
        XCTAssertEqual(capture.type, .idea)
        XCTAssertEqual(capture.fileName, "a.m4a")
        XCTAssertEqual(capture.capturedAt, capturedAt)
        XCTAssertEqual(capture.timezoneId, "America/Denver")
        XCTAssertEqual(capture.localOffsetMinutes, timeZone.secondsFromGMT(for: capturedAt) / 60)
        XCTAssertEqual(capture.byteLength, 0)
        XCTAssertEqual(capture.sha256, "")
        XCTAssertEqual(capture.durationSeconds, 0)
        XCTAssertEqual(capture.uploadAttempts, 0)
        XCTAssertNil(capture.artifactId)
        XCTAssertNil(capture.uploadedAt)

        // Device-generated capture ID must be a lowercased UUID string.
        XCTAssertEqual(capture.id, capture.id.lowercased())
        XCTAssertNotNil(UUID(uuidString: capture.id))
    }

    func testNewLocalGeneratesUniqueIds() {
        let first = CaptureRecord.newLocal(type: .memory, fileName: "1.m4a")
        let second = CaptureRecord.newLocal(type: .memory, fileName: "2.m4a")
        XCTAssertNotEqual(first.id, second.id)
    }
}
