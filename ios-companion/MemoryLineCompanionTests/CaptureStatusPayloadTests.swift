import XCTest
@testable import MemoryLineCompanion

/// Decoding of the real `capture_status` wire shape — the JSON string carried
/// in `SyncChangeDto.payloadJson`, produced by System.Text.Json on the service.
final class CaptureStatusPayloadTests: XCTestCase {

    private func decode(_ json: String) throws -> CaptureStatusChangePayload {
        try SyncJSON.decoder.decode(CaptureStatusChangePayload.self, from: Data(json.utf8))
    }

    // MARK: - Full and minimal payloads

    func testDecodesFullPayload() throws {
        let payload = try decode("""
        {"captureId":"3f0b7f2a-aaaa-bbbb-cccc-1234567890ab","status":"review_ready",\
        "processingStage":"review_ready","updatedAtUtc":"2026-08-07T12:00:00.125Z",\
        "transcriptAvailable":true,"transcriptPreview":"We stopped at the diner",\
        "transcriptCharCount":4213,"pendingEventCount":3,"approvedEventCount":1,\
        "failureReason":null,"failureRetryable":null}
        """)

        XCTAssertEqual(payload.captureId, "3f0b7f2a-aaaa-bbbb-cccc-1234567890ab")
        XCTAssertEqual(payload.status, SyncCaptureStatus.reviewReady)
        XCTAssertEqual(payload.processingStage, "review_ready")
        XCTAssertTrue(payload.transcriptAvailable)
        XCTAssertEqual(payload.transcriptPreview, "We stopped at the diner")
        XCTAssertEqual(payload.transcriptCharCount, 4213)
        XCTAssertEqual(payload.pendingEventCount, 3)
        XCTAssertEqual(payload.approvedEventCount, 1)
        XCTAssertNil(payload.failureReason)
        XCTAssertNil(payload.failureRetryable)

        let reference = try XCTUnwrap(ISO8601DateFormatter().date(from: "2026-08-07T12:00:00Z"))
        XCTAssertEqual(payload.updatedAtUtc.timeIntervalSince(reference), 0.125, accuracy: 0.0005)
    }

    /// System.Text.Json omits null members under the service's default
    /// options, so absent keys must decode exactly like explicit nulls.
    func testDecodesPayloadWithOptionalMembersOmitted() throws {
        let payload = try decode("""
        {"captureId":"cap-1","status":"received","updatedAtUtc":"2026-08-07T12:00:00Z",\
        "transcriptAvailable":false}
        """)

        XCTAssertEqual(payload.status, SyncCaptureStatus.received)
        XCTAssertNil(payload.processingStage)
        XCTAssertFalse(payload.transcriptAvailable)
        XCTAssertNil(payload.transcriptPreview)
        XCTAssertNil(payload.transcriptCharCount)
        XCTAssertNil(payload.pendingEventCount)
        XCTAssertNil(payload.approvedEventCount)
        XCTAssertNil(payload.failureReason)
        XCTAssertNil(payload.failureRetryable)
    }

    func testDecodesExplicitNullsIdenticallyToOmittedMembers() throws {
        let omitted = try decode("""
        {"captureId":"cap-1","status":"received","updatedAtUtc":"2026-08-07T12:00:00Z",\
        "transcriptAvailable":false}
        """)
        let explicit = try decode("""
        {"captureId":"cap-1","status":"received","processingStage":null,\
        "updatedAtUtc":"2026-08-07T12:00:00Z","transcriptAvailable":false,\
        "transcriptPreview":null,"transcriptCharCount":null,"pendingEventCount":null,\
        "approvedEventCount":null,"failureReason":null,"failureRetryable":null}
        """)

        XCTAssertEqual(omitted.processingStage, explicit.processingStage)
        XCTAssertEqual(omitted.transcriptCharCount, explicit.transcriptCharCount)
        XCTAssertEqual(omitted.pendingEventCount, explicit.pendingEventCount)
        XCTAssertEqual(omitted.approvedEventCount, explicit.approvedEventCount)
        XCTAssertEqual(omitted.failureRetryable, explicit.failureRetryable)
        XCTAssertEqual(omitted.updatedAtUtc, explicit.updatedAtUtc)
    }

    func testDecodesFailurePayload() throws {
        let payload = try decode("""
        {"captureId":"cap-1","status":"failed","processingStage":"failed_retryable",\
        "updatedAtUtc":"2026-08-07T12:00:00Z","transcriptAvailable":false,\
        "failureReason":"The transcription service was unreachable.","failureRetryable":true}
        """)

        XCTAssertEqual(payload.status, SyncCaptureStatus.failed)
        XCTAssertEqual(payload.processingStage, "failed_retryable")
        XCTAssertEqual(payload.failureReason, "The transcription service was unreachable.")
        XCTAssertEqual(payload.failureRetryable, true)
    }

    // MARK: - Date tolerance

    /// The service emits a variable number of fractional-second digits, and
    /// none at all on a whole second.
    func testDecodesDatesWithAndWithoutFractionalSeconds() throws {
        func payload(updatedAt: String) -> String {
            """
            {"captureId":"cap-1","status":"processing","updatedAtUtc":"\(updatedAt)",\
            "transcriptAvailable":false}
            """
        }

        let whole = try decode(payload(updatedAt: "2026-08-07T12:00:00Z"))
        let fractional = try decode(payload(updatedAt: "2026-08-07T12:00:00.5Z"))

        let reference = try XCTUnwrap(ISO8601DateFormatter().date(from: "2026-08-07T12:00:00Z"))
        XCTAssertEqual(whole.updatedAtUtc, reference)
        XCTAssertEqual(fractional.updatedAtUtc.timeIntervalSince(reference), 0.5, accuracy: 0.0005)
    }

    // MARK: - Forward compatibility

    /// A newer Windows build may add members; unknown keys must not fail the
    /// decode, or a single change would wedge the cursor.
    func testUnknownMembersAreIgnored() throws {
        let payload = try decode("""
        {"captureId":"cap-1","status":"completed","updatedAtUtc":"2026-08-07T12:00:00Z",\
        "transcriptAvailable":true,"embeddingCount":17,"reviewedByUserId":"someone"}
        """)

        XCTAssertEqual(payload.status, SyncCaptureStatus.completed)
        XCTAssertTrue(payload.transcriptAvailable)
    }

    func testEncodesCamelCaseWireNames() throws {
        let payload = StatusSyncTestData.payload(
            captureId: "cap-1",
            status: SyncCaptureStatus.reviewReady,
            processingStage: "review_ready",
            transcriptAvailable: true,
            transcriptPreview: "preview",
            transcriptCharCount: 12)

        let json = String(decoding: try SyncJSON.encoder.encode(payload), as: UTF8.self)

        XCTAssertTrue(json.contains("\"captureId\""), "raw JSON: \(json)")
        XCTAssertTrue(json.contains("\"processingStage\""), "raw JSON: \(json)")
        XCTAssertTrue(json.contains("\"updatedAtUtc\""), "raw JSON: \(json)")
        XCTAssertTrue(json.contains("\"transcriptCharCount\""), "raw JSON: \(json)")
        XCTAssertFalse(json.contains("capture_id"), "raw JSON: \(json)")
    }
}
