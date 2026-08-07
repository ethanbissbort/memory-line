import Foundation

/// Contract between the app and this widget extension.
///
/// SOURCE OF TRUTH: `MemoryLineCompanion/Shared/Domain/Models.swift`
/// (`enum WidgetSharedState`). The widget process cannot import app code, so
/// BOTH sides hard-code these values; any change must be mirrored in that file
/// (and, for the group ID, in `Config/*.entitlements`).
enum WidgetSharedState {
    static let appGroupId = "group.com.memoryline.companion"
    /// String: "idle" | "recording" | "paused".
    static let recordingStateKey = "widget.recordingState"
    /// Int: captures waiting to reach the sync service.
    static let pendingUploadsKey = "widget.pendingUploads"
    /// Double: epoch seconds of the most recent finalized capture.
    static let lastCaptureAtKey = "widget.lastCaptureAt"
}

/// Deep link every widget tap opens. The app shell interprets
/// `memoryline://record` as "toggle recording" (one-touch record, design §8.6);
/// the scheme is declared in `Config/MemoryLineCompanion-Info.plist`.
enum WidgetDeepLink {
    static let record = URL(string: "memoryline://record")!
}
