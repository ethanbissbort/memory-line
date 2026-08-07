import Foundation
import WidgetKit
import os

/// Pushes the app's capture/upload status into the shared app group and asks
/// WidgetKit to refresh, so the Lock Screen / Home Screen widgets stay honest.
/// Keys and value shapes are the `WidgetSharedState` contract (Models.swift) —
/// the widget process hard-codes the same constants. Safe to call from any
/// thread; both `UserDefaults` and `WidgetCenter` are thread-safe.
struct WidgetStatusPublisher {
    /// - Parameters:
    ///   - recordingState: "idle" | "recording" | "paused".
    ///   - pendingUploads: captures still waiting to reach the sync service.
    ///   - lastCaptureAt: most recent finalized capture, or nil if none yet.
    static func publish(recordingState: String, pendingUploads: Int, lastCaptureAt: Date?) {
        guard let defaults = UserDefaults(suiteName: WidgetSharedState.appGroupId) else {
            // Only possible if the App Group entitlement is missing/misconfigured.
            AppLog.widget.error("App group \(WidgetSharedState.appGroupId, privacy: .public) unavailable; widget status not published")
            return
        }
        defaults.set(recordingState, forKey: WidgetSharedState.recordingStateKey)
        defaults.set(pendingUploads, forKey: WidgetSharedState.pendingUploadsKey)
        if let lastCaptureAt {
            defaults.set(lastCaptureAt.timeIntervalSince1970, forKey: WidgetSharedState.lastCaptureAtKey)
        } else {
            defaults.removeObject(forKey: WidgetSharedState.lastCaptureAtKey)
        }
        WidgetCenter.shared.reloadAllTimelines()
    }
}
