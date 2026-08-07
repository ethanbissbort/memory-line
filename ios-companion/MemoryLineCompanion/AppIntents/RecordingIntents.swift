import AppIntents
import Foundation
import os.log

/// App Intents exposed to Siri, Shortcuts, widgets, and the Action Button
/// (design §8.6).
///
/// The intents are deliberately thin: each one opens the app
/// (`openAppWhenRun = true`) and posts one of the app-declared notification
/// names (`.mlStartRecording` / `.mlStopRecording` / `.mlToggleRecording`,
/// declared in App/). The capture shell observes those notifications and
/// drives `AudioRecorderService`; no service access happens in intent code.

private let intentLogger = Logger(subsystem: "com.memoryline.companion", category: "AppIntents")

/// Starts a new voice capture.
struct StartRecordingIntent: AppIntent {
    static var title: LocalizedStringResource = "Start Memory Recording"
    static var description = IntentDescription("Starts a new Memory Line voice capture.")
    /// The recorder lives in the app process, and foregrounding keeps the
    /// audio session reliable, so every recording intent opens the app.
    static var openAppWhenRun: Bool = true

    @MainActor
    func perform() async throws -> some IntentResult {
        intentLogger.info("StartRecordingIntent invoked")
        NotificationCenter.default.post(name: .mlStartRecording, object: nil)
        return .result()
    }
}

/// Stops and saves the in-progress voice capture.
struct StopRecordingIntent: AppIntent {
    static var title: LocalizedStringResource = "Stop Memory Recording"
    static var description = IntentDescription("Stops and saves the current Memory Line voice capture.")
    static var openAppWhenRun: Bool = true

    @MainActor
    func perform() async throws -> some IntentResult {
        intentLogger.info("StopRecordingIntent invoked")
        NotificationCenter.default.post(name: .mlStopRecording, object: nil)
        return .result()
    }
}

/// Starts a capture, or stops and saves the one in progress — the one-touch
/// action used for the Action Button and widget taps.
struct ToggleRecordingIntent: AppIntent {
    static var title: LocalizedStringResource = "Toggle Memory Recording"
    static var description = IntentDescription("Starts a Memory Line voice capture, or stops and saves the one in progress.")
    static var openAppWhenRun: Bool = true

    @MainActor
    func perform() async throws -> some IntentResult {
        intentLogger.info("ToggleRecordingIntent invoked")
        NotificationCenter.default.post(name: .mlToggleRecording, object: nil)
        return .result()
    }
}

/// App Shortcuts: available to Siri and assignable to the Action Button with
/// zero user setup the moment the app is installed.
struct MemoryLineShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(
            intent: StartRecordingIntent(),
            phrases: [
                "Start a memory in \(.applicationName)",
                "Start recording in \(.applicationName)"
            ],
            shortTitle: "Start Recording",
            systemImageName: "mic.fill"
        )
        AppShortcut(
            intent: StopRecordingIntent(),
            phrases: [
                "Stop recording in \(.applicationName)",
                "Save my memory in \(.applicationName)"
            ],
            shortTitle: "Stop Recording",
            systemImageName: "stop.fill"
        )
        AppShortcut(
            intent: ToggleRecordingIntent(),
            phrases: [
                "Capture a memory in \(.applicationName)",
                "Toggle recording in \(.applicationName)"
            ],
            shortTitle: "Capture Memory",
            systemImageName: "record.circle"
        )
    }
}
