import Combine
import SwiftUI
import os

/// In-process control-channel notifications. The App Intents (Siri, Shortcuts,
/// Action Button — same app target) post these to reach the live recorder;
/// this file owns the declaration and the app struct below routes them.
extension Notification.Name {
    /// Start (or resume) a recording with the default capture type.
    static let mlStartRecording = Notification.Name("ml.startRecording")
    /// Stop and finalize the active recording, if any.
    static let mlStopRecording = Notification.Name("ml.stopRecording")
    /// Toggle: start when idle, stop when recording/paused.
    static let mlToggleRecording = Notification.Name("ml.toggleRecording")
}

/// App entry point. Builds the `AppEnvironment` once, registers background
/// tasks before launch completes (a BGTaskScheduler requirement), and routes
/// the external control surfaces — widget deep links, intent notifications,
/// and scene-phase changes — to the recorder and upload coordinator.
@main
struct MemoryLineCompanionApp: App {
    @Environment(\.scenePhase) private var scenePhase
    @State private var env: AppEnvironment

    init() {
        let environment = AppEnvironment()
        // BGTaskScheduler requires every launch handler to be registered
        // before the app finishes launching; App.init runs early enough.
        environment.uploads.registerBackgroundTasks()
        _env = State(initialValue: environment)
    }

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(env)
                .onOpenURL(perform: handleDeepLink)
                .onReceive(controlPublisher(for: .mlStartRecording)) { _ in startRecording() }
                .onReceive(controlPublisher(for: .mlStopRecording)) { _ in stopRecording() }
                .onReceive(controlPublisher(for: .mlToggleRecording)) { _ in toggleRecording() }
                .onChange(of: scenePhase) { _, newPhase in
                    guard newPhase == .active else { return }
                    // Surface crash-interrupted recordings (design §8.4) and
                    // push anything queued while we were backgrounded.
                    env.recorder.recoverAbandonedRecordings()
                    env.uploads.kick()
                }
        }
    }

    // MARK: - Control routing

    /// Intents may post from any thread; hop to the main queue for the
    /// MainActor recorder.
    private func controlPublisher(for name: Notification.Name) -> AnyPublisher<Notification, Never> {
        NotificationCenter.default.publisher(for: name)
            .receive(on: DispatchQueue.main)
            .eraseToAnyPublisher()
    }

    /// Handles `memoryline://` deep links from the widgets:
    /// `memoryline://record` toggles recording; `memoryline://open` just
    /// foregrounds the app.
    private func handleDeepLink(_ url: URL) {
        guard url.scheme?.lowercased() == "memoryline" else { return }
        let target = (url.host() ?? url.path)
            .trimmingCharacters(in: CharacterSet(charactersIn: "/"))
            .lowercased()
        switch target {
        case "record":
            toggleRecording()
        case "open", "":
            break // Opening the URL already foregrounded the app.
        default:
            AppLog.ui.notice("ignored unknown deep link target \(target, privacy: .public)")
        }
    }

    private var defaultCaptureType: CaptureType {
        CaptureType(rawValue: env.settings.string(AppSettingsKey.defaultCaptureType) ?? "") ?? .memory
    }

    private func startRecording() {
        switch env.recorder.phase {
        case .idle:
            let type = defaultCaptureType
            Task { await env.recorder.start(type: type) }
        case .paused:
            env.recorder.resume()
        case .preparing, .recording, .finalizing:
            break
        }
    }

    private func stopRecording() {
        switch env.recorder.phase {
        case .preparing, .recording, .paused:
            Task { await env.recorder.stop() }
        case .idle, .finalizing:
            break
        }
    }

    private func toggleRecording() {
        switch env.recorder.phase {
        case .idle, .paused:
            startRecording()
        case .preparing, .recording:
            stopRecording()
        case .finalizing:
            break
        }
    }
}
