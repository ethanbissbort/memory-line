import Foundation

/// Protocol seams the feature layers code against. Concrete types (prescribed
/// names, all in this app target): SQLiteCaptureStore, SQLiteSettingsStore,
/// KeychainTokenStore, SyncAPIClient, UploadCoordinator, AudioRecorderService,
/// wired together in App/AppEnvironment.swift.

/// Durable capture storage. Implementations are thread-safe (SQLite in WAL mode
/// behind an internal lock); calls are synchronous and cheap.
protocol CaptureStore: AnyObject, Sendable {
    func insert(_ capture: CaptureRecord) throws
    func update(_ capture: CaptureRecord) throws
    func delete(id: String) throws
    func capture(id: String) throws -> CaptureRecord?
    /// Newest first.
    func allCaptures() throws -> [CaptureRecord]
    /// Captures whose state.isUploadPending, oldest first (upload in capture order).
    func pendingUploads() throws -> [CaptureRecord]
    /// Captures stuck in `.recording` (crash recovery, design §8.4).
    func abandonedRecordings() throws -> [CaptureRecord]
    func pendingUploadCount() throws -> Int
}

/// Durable key-value settings (SQLite-backed, NOT UserDefaults — settings ride
/// in the same database file as captures so backup/restore stays atomic).
protocol SettingsStore: AnyObject, Sendable {
    func string(_ key: String) -> String?
    func set(_ value: String?, for key: String)
    func bool(_ key: String, default defaultValue: Bool) -> Bool
    func set(_ value: Bool, for key: String)
}

/// Token storage in the iOS Keychain (design §14.1/§14.3). Server URL and device
/// ID are not secrets and live in SettingsStore instead.
protocol TokenStore: AnyObject, Sendable {
    func accessToken() -> String?
    func refreshToken() -> String?
    func save(accessToken: String, refreshToken: String) throws
    func clear()
}

/// Memory Line Sync API v1 client surface (mirrors shared-contracts/openapi).
/// Implementations attach the bearer token and transparently refresh once on a
/// 401 (rotation is serialized internally). All errors surface as SyncAPIError.
protocol SyncAPI: AnyObject, Sendable {
    /// The only call that takes an explicit server URL and needs no stored
    /// credentials. On success the caller persists identity + tokens.
    func register(serverURL: String, request: DeviceRegisterRequest) async throws -> DeviceRegisterResponse
    func revokeDevice() async throws
    func createCapture(_ request: CaptureCreateRequest) async throws -> CaptureResponse
    func initiateArtifact(captureId: String, _ request: ArtifactInitiateRequest) async throws -> ArtifactInitiateResponse
    /// Uploads one part's bytes from a file slice.
    func uploadArtifactPart(artifactId: String, partNumber: Int, fileURL: URL) async throws
    func completeArtifact(artifactId: String, _ request: ArtifactCompleteRequest) async throws -> ArtifactCompleteResponse
    func pull(cursor: Int64, limit: Int) async throws -> SyncPullResponse
    func ack(cursor: Int64) async throws
}

/// Error surface for the sync client. `retryable` follows design §16.2:
/// network/408/429/5xx retry; other 4xx do not.
struct SyncAPIError: Error, Sendable {
    var statusCode: Int?
    var apiError: ApiError?
    var retryable: Bool
    var message: String

    /// True when the device's credentials are gone/revoked (needs re-pairing).
    var needsReauthentication: Bool { statusCode == 401 }
}
