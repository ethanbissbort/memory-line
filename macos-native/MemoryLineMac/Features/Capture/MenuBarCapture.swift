import SwiftUI

/// Menu-bar quick capture: start and stop a recording without bringing the main
/// window forward (port plan §4.4, the Mac's answer to the Windows JumpList).
///
/// This is intentionally the *smaller* of the two capture surfaces. It offers
/// the transport and nothing else — no device picker, no capture type, no
/// meter. Anything that needs a considered choice belongs in `CaptureView`,
/// where there is room to explain it; a menu that grows a second full UI is a
/// menu people stop reading.
///
/// Wire it up from the app entry point, which owns the scene:
///
/// ```swift
/// MenuBarExtra {
///     MenuBarCaptureView(recorder: recorder, settings: environment.settings)
/// } label: {
///     MenuBarCaptureLabel(recorder: recorder)
/// }
/// ```
///
/// Written for the default `.menu` style, where each `Button` becomes a menu
/// item and a bare `Text` becomes a disabled one. It still renders sensibly
/// under `.window`, but the item ordering is chosen for a menu.
///
/// Dependencies come in through the initializer as `any` existentials over the
/// seams in `CaptureSeams.swift`, matching `CaptureView` — the menu-bar scene
/// is built at the composition root, which is the one place that knows the
/// concrete types.
struct MenuBarCaptureView: View {
    private let recorder: any MacAudioRecording
    private let settings: any SettingsStore

    @Environment(\.openURL) private var openURL

    /// Held for the same reason `CaptureView` holds it: after a prompt this is
    /// a fresher answer than the recorder's cached `authorization`.
    @State private var authorizationOverride: MacMicrophoneAuthorization?
    /// `start` and `stop` throw rather than entering `.failed`, and a menu has
    /// nowhere to put an alert, so the reason is parked here and shown as a
    /// menu line the next time the menu opens.
    @State private var transportFailure: String?

    init(recorder: any MacAudioRecording, settings: any SettingsStore) {
        self.recorder = recorder
        self.settings = settings
    }

    var body: some View {
        Text(statusLine)

        if let transportFailure {
            Text(transportFailure)
        }

        Divider()

        if authorization == .denied {
            // No dead "Start Recording" item that would silently do nothing.
            Text("Microphone access is off. Memory Line cannot ask again.")
            Button("Open Privacy Settings") {
                MicrophoneSettingsLink.open(with: openURL)
            }
        } else {
            Button(isActive ? "Stop and Save" : "Start Recording") {
                toggleRecording()
            }

            if isActive {
                Button(isPaused ? "Resume" : "Pause") {
                    if isPaused { recorder.resume() } else { recorder.pause() }
                }
                // No confirmation: a menu closes the moment it is clicked, so
                // there is nowhere to host a dialog. The destructive role gives
                // it the standard warning treatment, and the confirming path
                // stays in the main window.
                Button("Discard Recording", role: .destructive) {
                    Task { await recorder.cancel() }
                }
            }
        }
    }

    /// macOS builds menu content when the menu opens, so the elapsed figure
    /// here is a snapshot from that moment rather than a ticking clock. That is
    /// deliberate — the live timer is in `CaptureView`, and driving one from a
    /// `TimelineView` inside a menu is not something the menu will honour.
    private var statusLine: String {
        switch recorder.state {
        case .idle:
            return "Not recording"
        case .recording:
            return "Recording — \(CaptureElapsedFormat.clock(recorder.elapsed))"
        case .paused:
            return "Paused — \(CaptureElapsedFormat.clock(recorder.elapsed))"
        case .failed(let message):
            return message
        }
    }

    private func toggleRecording() {
        switch recorder.state {
        case .idle, .failed:
            Task { await startRecording() }
        case .recording, .paused:
            Task { await stopRecording() }
        }
    }

    private func startRecording() async {
        transportFailure = nil

        var status = authorization
        if status == .undetermined {
            // The prompt is system-modal, so it reaches the user even though
            // this app has no window in front of them.
            status = await recorder.requestAuthorization()
            authorizationOverride = status
        }
        guard status == .authorized else { return }

        do {
            // Reads the id the capture window persisted instead of enumerating
            // devices: the menu has no room to choose one, and the seam defines
            // an id that is no longer connected exactly like an absent one.
            try await recorder.start(deviceId: settings.string(MacCaptureSettingsKey.inputDeviceId))
        } catch {
            transportFailure = error.localizedDescription
        }
    }

    /// The saved capture is dropped on the floor on purpose: the menu is a
    /// transport, and the library window is where a capture is looked at.
    private func stopRecording() async {
        transportFailure = nil
        do {
            _ = try await recorder.stop()
        } catch {
            transportFailure = error.localizedDescription
        }
    }

    private var authorization: MacMicrophoneAuthorization {
        authorizationOverride ?? recorder.authorization
    }

    private var isPaused: Bool { recorder.state == .paused }
    private var isActive: Bool { recorder.state == .recording || isPaused }
}

/// The menu-bar item itself: one symbol carrying the recorder's state.
///
/// The menu bar draws this as a template image, so colour cannot carry meaning
/// here — a red dot would come out the same grey as everything else. The fill
/// does the work instead: hollow when idle, filled while a recording is live,
/// which also reads correctly for anyone who cannot distinguish the tint.
struct MenuBarCaptureLabel: View {
    private let recorder: any MacAudioRecording

    init(recorder: any MacAudioRecording) {
        self.recorder = recorder
    }

    var body: some View {
        Image(systemName: symbolName)
            // The symbol is the only thing in the menu bar, so it carries the
            // whole announcement; without this VoiceOver reads the SF Symbol
            // name.
            .accessibilityLabel(stateDescription)
    }

    private var symbolName: String {
        switch recorder.state {
        case .idle: return "waveform.circle"
        case .recording: return "record.circle.fill"
        case .paused: return "pause.circle.fill"
        case .failed: return "exclamationmark.triangle"
        }
    }

    private var stateDescription: String {
        switch recorder.state {
        case .idle: return "Memory Line, not recording"
        case .recording: return "Memory Line, recording"
        case .paused: return "Memory Line, recording paused"
        case .failed: return "Memory Line, recording stopped by an error"
        }
    }
}
