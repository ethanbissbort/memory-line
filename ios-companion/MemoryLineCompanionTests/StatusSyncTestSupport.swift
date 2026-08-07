import Foundation
@testable import MemoryLineCompanion

/// Scripted `SyncAPI` for status-pull tests. `pullResults` is consumed in call
/// order; once it runs dry, `pull` answers with an empty final page so a test
/// never hangs on an unscripted extra call. Only `pull`/`ack` are exercised by
/// the status path — the upload calls throw so an accidental use is loud.
///
/// `@unchecked Sendable`: every access happens on the test's main actor, which
/// is cheaper to state than to lock for.
final class StubSyncAPI: SyncAPI, @unchecked Sendable {
    enum PullResult {
        case page(SyncPullResponse)
        case failure(SyncAPIError)
    }

    /// Responses handed to successive `pull` calls.
    var pullResults: [PullResult] = []
    /// Error thrown by every `ack` call when set.
    var ackError: SyncAPIError?

    /// Cursors passed to `pull`, in call order.
    private(set) var pullCursors: [Int64] = []
    /// Cursors passed to `ack`, in call order.
    private(set) var ackedCursors: [Int64] = []

    func pull(cursor: Int64, limit: Int) async throws -> SyncPullResponse {
        pullCursors.append(cursor)
        guard !pullResults.isEmpty else {
            return SyncPullResponse(changes: [], nextCursor: cursor, hasMore: false)
        }
        switch pullResults.removeFirst() {
        case .page(let response):
            return response
        case .failure(let error):
            throw error
        }
    }

    func ack(cursor: Int64) async throws {
        ackedCursors.append(cursor)
        if let ackError {
            throw ackError
        }
    }

    // MARK: - Not used by the status path

    private var unexpected: SyncAPIError {
        SyncAPIError(statusCode: nil, apiError: nil, retryable: false, message: "unexpected call")
    }

    func register(serverURL: String, request: DeviceRegisterRequest) async throws -> DeviceRegisterResponse {
        throw unexpected
    }

    func revokeDevice() async throws {
        throw unexpected
    }

    func createCapture(_ request: CaptureCreateRequest) async throws -> CaptureResponse {
        throw unexpected
    }

    func initiateArtifact(captureId: String, _ request: ArtifactInitiateRequest) async throws -> ArtifactInitiateResponse {
        throw unexpected
    }

    func uploadArtifactPart(artifactId: String, partNumber: Int, fileURL: URL) async throws {
        throw unexpected
    }

    func completeArtifact(artifactId: String, _ request: ArtifactCompleteRequest) async throws -> ArtifactCompleteResponse {
        throw unexpected
    }
}

/// Records what the coordinator would have shown the user, without touching
/// UserNotifications or the app lifecycle.
@MainActor
final class SpyStatusNotifier: CaptureStatusNotifying {
    private(set) var authorizationRequests = 0
    private(set) var posted: [CaptureStatusNotification] = []

    func requestAuthorizationIfNeeded() async {
        authorizationRequests += 1
    }

    func post(_ notification: CaptureStatusNotification) async {
        posted.append(notification)
    }
}

/// Builders for the `capture_status` wire shape. The payload is encoded with
/// the production `SyncJSON.encoder`, so these changes carry exactly the JSON
/// the service would deliver.
enum StatusSyncTestData {
    /// Fills the settings a pull pass requires before it will do anything.
    static func markPaired(_ settings: any SettingsStore) {
        settings.set(true, for: AppSettingsKey.syncEnabled)
        settings.set("device-1", for: AppSettingsKey.deviceId)
        settings.set("http://mac-mini.tailnet:8080", for: AppSettingsKey.serverURL)
    }

    static func payload(
        captureId: String,
        status: String,
        processingStage: String? = nil,
        updatedAt: Date = Date(timeIntervalSince1970: 1_754_500_500.5),
        transcriptAvailable: Bool = false,
        transcriptPreview: String? = nil,
        transcriptCharCount: Int? = nil,
        pendingEventCount: Int? = nil,
        approvedEventCount: Int? = nil,
        failureReason: String? = nil,
        failureRetryable: Bool? = nil
    ) -> CaptureStatusChangePayload {
        CaptureStatusChangePayload(
            captureId: captureId,
            status: status,
            processingStage: processingStage,
            updatedAtUtc: updatedAt,
            transcriptAvailable: transcriptAvailable,
            transcriptPreview: transcriptPreview,
            transcriptCharCount: transcriptCharCount,
            pendingEventCount: pendingEventCount,
            approvedEventCount: approvedEventCount,
            failureReason: failureReason,
            failureRetryable: failureRetryable)
    }

    static func change(
        changeId: Int64,
        payload: CaptureStatusChangePayload,
        entityType: String = SyncChangeEntityType.captureStatus,
        operation: String = "upsert"
    ) throws -> SyncChangeDto {
        let data = try SyncJSON.encoder.encode(payload)
        return SyncChangeDto(
            changeId: changeId,
            entityType: entityType,
            entityId: payload.captureId,
            operation: operation,
            revision: 1,
            changedAtUtc: Date(timeIntervalSince1970: 1_754_500_500),
            sourceDeviceId: "windows-1",
            payloadJson: String(decoding: data, as: UTF8.self))
    }

    /// One page, ending the pass unless `hasMore` says otherwise.
    static func page(
        _ changes: [SyncChangeDto],
        nextCursor: Int64,
        hasMore: Bool = false
    ) -> StubSyncAPI.PullResult {
        .page(SyncPullResponse(changes: changes, nextCursor: nextCursor, hasMore: hasMore))
    }
}
