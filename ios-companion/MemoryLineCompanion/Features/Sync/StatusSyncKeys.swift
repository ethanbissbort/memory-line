import Foundation

/// Setting keys and identifiers owned by Phase 3 status sync (design §19).
/// They live here instead of in `AppSettingsKey` / `BackgroundTaskId` so the
/// status-handoff feature stays self-contained. The values below are what lands
/// in the `settings` table and in Info.plist, so never change them.

/// Cursor bookkeeping for `GET /sync/pull` (design §12.1, "last acknowledged
/// server cursor"). Persisted as the decimal string of an `Int64`.
enum SyncCursorKey {
    static let pullCursor = "pull_cursor"
}

/// Local-notification bookkeeping. Authorization is requested lazily — never at
/// first launch — so the system prompt only appears once the phone is paired
/// and actually receiving status.
enum StatusNotificationSettingsKey {
    static let authorizationRequested = "status_notification_authorization_requested" // bool
}

extension BackgroundTaskId {
    /// `BGAppRefreshTaskRequest` identifier for the deferred status pull. Must
    /// stay listed in `BGTaskSchedulerPermittedIdentifiers` in
    /// Config/MemoryLineCompanion-Info.plist.
    static let statusRefresh = "com.memoryline.companion.statusrefresh"
}
