import Foundation
import Observation
import os

/// Composition root for the macOS app, mirroring the iOS companion's
/// `AppEnvironment`. The database, stores, and sync client are built once at
/// launch and injected into the view tree with `.environment(_:)`.
///
/// Everything this type touches comes from the shared sources under
/// `ios-companion/MemoryLineCompanion/Shared/`, which the Xcode project
/// compiles into this target (see macos-native/README.md). Two differences from
/// iOS, both deliberate:
///
///  - **No recorder and no upload/status coordinators.** Those live in the iOS
///    app's `Features/` tree and are built on iOS-only APIs (AVFAudio session
///    handling, `BGTaskScheduler`). macOS gets its own; see
///    `docs/design/MACOS-PORT-PLAN.md` §4.
///  - **Device display name** comes from the host name rather than
///    `UIDevice.current.name`.
@MainActor
@Observable
final class MacAppEnvironment {
    let database: SQLiteDatabase
    let captures: SQLiteCaptureStore
    let settings: SQLiteSettingsStore
    let tokens: KeychainTokenStore
    let api: SyncAPIClient
    /// Windows-authored processing status per capture (design §19 Phase 3).
    let statuses: SQLiteCaptureStatusStore
    let sync: MacSyncCoordinator

    private let logger = AppLog.logger(category: "environment")

    init(database: SQLiteDatabase, statuses: SQLiteCaptureStatusStore) {
        let captures = SQLiteCaptureStore(database: database)
        let settings = SQLiteSettingsStore(database: database)
        let tokens = KeychainTokenStore()
        let api = SyncAPIClient(settings: settings, tokens: tokens)

        self.database = database
        self.captures = captures
        self.settings = settings
        self.tokens = tokens
        self.api = api
        self.statuses = statuses
        self.sync = MacSyncCoordinator(settings: settings, api: api, statusStore: statuses)
    }

    /// True when this Mac has completed pairing with a sync server.
    /// Backed by the settings store, so it is NOT observation-tracked; views
    /// that change pairing re-read it via their own state.
    var isPaired: Bool { settings.string(AppSettingsKey.deviceId) != nil }

    var serverURL: String? { settings.string(AppSettingsKey.serverURL) }

    /// Registers this Mac with the sync service using a pairing code, then
    /// persists the identity and tokens. Throws `SyncAPIError` (or
    /// `KeychainError`) for the UI to display; the pairing code is never logged
    /// (design §14.5).
    func pair(serverURL: String, pairingCode: String) async throws {
        // `platform` MUST be set explicitly: DeviceRegisterRequest defaults it to
        // "ios" for the companion, and the memberwise initializer would happily
        // register this Mac as an iPhone.
        //
        // "other", not "macos": the wire contract's enum is [ios, windows, other]
        // (shared-contracts/openapi/memory-line-sync-v1.yaml) and the server
        // rejects anything else outright (DeviceService.AllowedPlatforms). Adding
        // a first-class "macos" value is a cross-cutting change tracked in
        // docs/design/MACOS-PORT-PLAN.md §7.
        let request = DeviceRegisterRequest(
            pairingCode: pairingCode,
            platform: "other",
            displayName: Self.deviceDisplayName,
            appVersion: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String)
        let response = try await api.register(serverURL: serverURL, request: request)

        settings.set(SyncAPIClient.normalizedBaseURL(serverURL) ?? serverURL, for: AppSettingsKey.serverURL)
        settings.set(response.deviceId, for: AppSettingsKey.deviceId)
        settings.set(request.displayName, for: AppSettingsKey.deviceDisplayName)
        do {
            try tokens.save(accessToken: response.accessToken, refreshToken: response.refreshToken)
        } catch {
            // Without tokens the pairing is unusable — roll back the identity so
            // `isPaired` stays false and the user can retry cleanly.
            settings.set(nil, for: AppSettingsKey.deviceId)
            logger.error("pairing aborted: token save failed")
            throw error
        }
        settings.set(true, for: AppSettingsKey.syncEnabled)
        logger.info("paired as device \(response.deviceId, privacy: .public)")
        // Pull immediately so the Library fills in without waiting for the
        // first tick, then keep the periodic loop running.
        sync.startPeriodicSync()
    }

    /// Unpairs from the sync server. Revocation is best-effort (the server may
    /// be unreachable); local credentials are always cleared.
    func unpair() async {
        // Stop the loop first: a pull racing the credential clear would fail
        // with a 401 and surface a misleading "no longer paired" error.
        sync.stopPeriodicSync()
        do {
            try await api.revokeDevice()
            logger.info("device revoked on server")
        } catch {
            logger.notice("server revoke failed during unpair; clearing local credentials anyway")
        }
        tokens.clear()
        settings.set(nil, for: AppSettingsKey.deviceId)
        settings.set(false, for: AppSettingsKey.syncEnabled)
    }

    /// Name this Mac presents to the sync service when pairing. The local host
    /// name is the closest macOS analogue to `UIDevice.current.name` and is what
    /// the user sees in Windows Settings → Sync.
    ///
    /// `nonisolated` because it reads only `ProcessInfo` — nothing main-actor
    /// about it. Without this it inherits the type's `@MainActor` isolation and
    /// cannot be read from a nonisolated context such as a plain XCTest method.
    nonisolated static var deviceDisplayName: String {
        let host = ProcessInfo.processInfo.hostName
        // hostName is often the Bonjour form ("studio.local"); trim the suffix.
        return host.hasSuffix(".local") ? String(host.dropLast(6)) : host
    }

    // MARK: - Bootstrap

    /// Opens the app database, tolerating a corrupt WAL sidecar, exactly as the
    /// iOS companion does: if the first open (or its migrations) fails, the
    /// `-wal`/`-shm` sidecars are deleted and the open is retried once.
    ///
    /// If the retry also fails this traps deliberately — every durability
    /// guarantee flows through this database, and limping on without it would
    /// accept work whose metadata silently vanishes.
    static func bootstrap() -> MacAppEnvironment {
        let url: URL
        do {
            url = try AppDatabase.defaultURL()
        } catch {
            fatalError("Memory Line cannot create its Application Support directory: \(error)")
        }

        let database: SQLiteDatabase
        do {
            database = try AppDatabase.open(at: url)
        } catch {
            AppLog.persistence.error(
                "database open failed (\(String(describing: error), privacy: .public)); removing WAL sidecars and retrying once")
            try? FileManager.default.removeItem(atPath: url.path + "-wal")
            try? FileManager.default.removeItem(atPath: url.path + "-shm")
            do {
                database = try AppDatabase.open(at: url)
            } catch {
                fatalError(
                    "Memory Line could not open its capture database even after discarding the WAL sidecar. "
                    + "The database file at \(url.path) is unreadable; restore from a backup or reinstall. "
                    + "Underlying error: \(error)")
            }
        }

        let statuses: SQLiteCaptureStatusStore
        do {
            statuses = try SQLiteCaptureStatusStore(database: database)
        } catch {
            fatalError(
                "Memory Line could not prepare its capture-status table on an otherwise healthy "
                + "database: \(error)")
        }

        return MacAppEnvironment(database: database, statuses: statuses)
    }
}
