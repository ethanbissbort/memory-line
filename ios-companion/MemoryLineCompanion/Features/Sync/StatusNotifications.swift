import Foundation
import UIKit
import UserNotifications
import os

/// A capture lifecycle transition worth telling the user about (design §19
/// Phase 3).
///
/// Deliberately a LOCAL notification driven by the status pull, not APNs: the
/// system is self-hosted with no accounts and no vendor push infrastructure
/// (design §22.1/§22.2), so the phone posts these itself the moment a pull
/// observes the transition.
enum CaptureStatusNotification: Equatable, Sendable {
    case reviewReady(captureId: String)
    case completed(captureId: String)
    /// `reason` is the contract's user-facing failure text, which carries no
    /// transcript content, file paths, or credentials (design §14.5).
    case failed(captureId: String, reason: String?)

    var captureId: String {
        switch self {
        case .reviewReady(let captureId), .completed(let captureId):
            return captureId
        case .failed(let captureId, _):
            return captureId
        }
    }
}

/// Presents `CaptureStatusNotification`s. Behind a protocol so the pull path
/// can be tested without UserNotifications or an app lifecycle.
@MainActor
protocol CaptureStatusNotifying: AnyObject {
    /// Asks for notification authorization at most once ever, and only while
    /// the app is in the foreground so the system prompt is actually shown.
    func requestAuthorizationIfNeeded() async
    /// Posts a notification for a transition, unless the app is foreground.
    func post(_ notification: CaptureStatusNotification) async
}

/// `CaptureStatusNotifying` over `UNUserNotificationCenter`.
///
/// Authorization is requested lazily (design §19 Phase 3): the only caller is
/// the status pull, which cannot run before the device is paired, so the prompt
/// can never appear at first launch. The "asked already" flag lives in
/// `SettingsStore` so it survives relaunches alongside the rest of the app's
/// durable state.
///
/// Never puts transcript text, notes, tokens, or the pairing code in a
/// notification or a log line (design §14.5) — only capture IDs, the fixed
/// status vocabulary, and the contract's user-facing failure reason.
@MainActor
final class CaptureStatusNotifier: CaptureStatusNotifying {
    private let settings: any SettingsStore
    private let center: UNUserNotificationCenter
    private let logger = Logger(subsystem: "com.memoryline.companion", category: "CaptureStatusNotifier")

    /// - Parameters:
    ///   - settings: durable store for the one-shot "authorization requested" flag.
    ///   - center: injectable for tests; defaults to the app's center.
    init(settings: any SettingsStore, center: UNUserNotificationCenter = .current()) {
        self.settings = settings
        self.center = center
    }

    func requestAuthorizationIfNeeded() async {
        guard !settings.bool(StatusNotificationSettingsKey.authorizationRequested, default: false) else {
            return
        }
        // A background pull cannot show the system prompt. Leaving the flag
        // unset means the next foreground pull asks, instead of burning the
        // single chance iOS gives us on a moment the user never sees.
        guard UIApplication.shared.applicationState == .active else { return }
        do {
            _ = try await center.requestAuthorization(options: [.alert, .sound])
            settings.set(true, for: StatusNotificationSettingsKey.authorizationRequested)
            logger.info("requested notification authorization")
        } catch {
            // Leave the flag unset so a later pull retries.
            logger.notice("notification authorization request failed")
        }
    }

    func post(_ notification: CaptureStatusNotification) async {
        // A foreground user is already looking at the app; a banner there would
        // only interrupt (design §15 keeps in-motion interaction minimal).
        guard UIApplication.shared.applicationState != .active else { return }
        switch await center.notificationSettings().authorizationStatus {
        case .authorized, .provisional, .ephemeral:
            break
        default:
            return
        }

        let content = UNMutableNotificationContent()
        content.title = notification.title
        content.body = notification.body
        content.sound = .default
        // One live notification per capture: a later transition replaces the
        // earlier one instead of stacking a trail of them.
        let request = UNNotificationRequest(
            identifier: "capture-status-\(notification.captureId)",
            content: content,
            trigger: nil)
        do {
            try await center.add(request)
            logger.info("posted status notification id=\(notification.captureId, privacy: .public)")
        } catch {
            logger.notice("could not post a status notification")
        }
    }
}

private extension CaptureStatusNotification {
    var title: String {
        switch self {
        case .reviewReady: return "Ready for review"
        case .completed: return "Capture completed"
        case .failed: return "Processing failed"
        }
    }

    var body: String {
        switch self {
        case .reviewReady:
            return "A capture finished processing and is waiting for review on Windows."
        case .completed:
            return "Windows finished processing a capture and added it to your timeline."
        case .failed(_, let reason):
            return reason ?? "Windows could not finish processing a capture."
        }
    }
}
