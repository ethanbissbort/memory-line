import AVFAudio
import SwiftUI
import UIKit

/// Main capture screen, built for use on the road (design §15): one huge round
/// record button with unmistakable states, a monospaced elapsed timer, a subtle
/// input level meter, pause/discard secondary controls, and a capture-type
/// selector persisted as the default. Errors surface in a banner; a denied
/// microphone permission gets a dedicated state with a Settings link.
struct CaptureView: View {
    @Environment(AppEnvironment.self) private var env
    @Environment(\.openURL) private var openURL
    @Environment(\.scenePhase) private var scenePhase

    @State private var showCancelConfirmation = false
    @State private var permissionDenied = false
    @State private var isPulsing = false

    var body: some View {
        let recorder = env.recorder
        let uploads = env.uploads

        VStack(spacing: 20) {
            errorBanner(recorder)

            Spacer(minLength: 0)

            if permissionDenied {
                microphoneDeniedView
            } else {
                typePicker(recorder)
                statusReadout(recorder)
                recordButton(recorder)
                secondaryControls(recorder)
            }

            Spacer(minLength: 0)

            syncFooter(uploads)
        }
        .padding()
        .onAppear { refreshPermissionState() }
        .onChange(of: scenePhase) { _, newPhase in
            // Re-check after a round trip to the Settings app.
            if newPhase == .active { refreshPermissionState() }
        }
        .onChange(of: recorder.phase) { _, newPhase in
            if newPhase == .recording {
                withAnimation(.easeInOut(duration: 0.8).repeatForever(autoreverses: true)) {
                    isPulsing = true
                }
            } else {
                withAnimation(.easeOut(duration: 0.2)) {
                    isPulsing = false
                }
            }
        }
        .confirmationDialog(
            "Discard this recording?",
            isPresented: $showCancelConfirmation,
            titleVisibility: .visible
        ) {
            Button("Discard Recording", role: .destructive) {
                Task { await recorder.cancelAndDiscard() }
            }
            Button("Keep Recording", role: .cancel) {}
        } message: {
            Text("The audio recorded so far will be deleted.")
        }
    }

    // MARK: - Error banner

    @ViewBuilder
    private func errorBanner(_ recorder: AudioRecorderService) -> some View {
        if let message = recorder.lastError {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundStyle(.yellow)
                    .accessibilityHidden(true)
                Text(message)
                    .font(.callout)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Button {
                    recorder.lastError = nil
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .foregroundStyle(.secondary)
                }
                .accessibilityLabel("Dismiss error")
            }
            .padding(12)
            .background(.yellow.opacity(0.15), in: RoundedRectangle(cornerRadius: 12))
            .transition(.opacity)
        }
    }

    // MARK: - Capture type

    private func typePicker(_ recorder: AudioRecorderService) -> some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(CaptureType.allCases) { type in
                    let isSelected = recorder.currentType == type
                    Button {
                        recorder.currentType = type
                        env.settings.set(type.rawValue, for: AppSettingsKey.defaultCaptureType)
                    } label: {
                        Label(type.displayName, systemImage: type.symbolName)
                            .font(.callout.weight(isSelected ? .semibold : .regular))
                            .padding(.horizontal, 14)
                            .padding(.vertical, 10)
                            .background(
                                isSelected
                                    ? Color.accentColor.opacity(0.2)
                                    : Color(uiColor: .secondarySystemBackground),
                                in: Capsule()
                            )
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("\(type.displayName) capture type")
                    .accessibilityAddTraits(isSelected ? [.isSelected] : [])
                }
            }
            .padding(.horizontal, 4)
        }
        .disabled(recorder.phase != .idle)
        .opacity(recorder.phase == .idle ? 1 : 0.4)
    }

    // MARK: - Timer + meter

    private func statusReadout(_ recorder: AudioRecorderService) -> some View {
        VStack(spacing: 10) {
            Text(Self.timeString(recorder.elapsed))
                .font(.system(.largeTitle, design: .monospaced).weight(.medium))
                .foregroundStyle(recorder.phase == .recording ? .primary : .secondary)
                .opacity(recorder.phase == .idle ? 0 : 1)
                .accessibilityLabel("Elapsed time \(Self.timeString(recorder.elapsed))")

            Text("Paused")
                .font(.headline)
                .foregroundStyle(.orange)
                .opacity(recorder.phase == .paused ? 1 : 0)

            levelMeter(recorder)
        }
    }

    private func levelMeter(_ recorder: AudioRecorderService) -> some View {
        GeometryReader { proxy in
            ZStack(alignment: .leading) {
                Capsule()
                    .fill(Color(uiColor: .secondarySystemFill))
                Capsule()
                    .fill(recorder.phase == .recording ? Color.red : Color.gray)
                    .frame(width: max(4, proxy.size.width * CGFloat(recorder.meterLevel)))
                    .animation(.linear(duration: 0.08), value: recorder.meterLevel)
            }
        }
        .frame(width: 180, height: 6)
        .opacity(recorder.phase == .recording || recorder.phase == .paused ? 1 : 0)
        .accessibilityHidden(true)
    }

    // MARK: - Record button

    private func recordButton(_ recorder: AudioRecorderService) -> some View {
        Button {
            handleRecordTap(recorder)
        } label: {
            ZStack {
                Circle()
                    .stroke(Color.red.opacity(0.3), lineWidth: 8)
                    .scaleEffect(isPulsing ? 1.12 : 0.98)
                    .opacity(recorder.phase == .recording ? 1 : 0)
                Circle()
                    .fill(buttonBackground(for: recorder.phase))
                buttonGlyph(for: recorder.phase)
            }
            .frame(width: 160, height: 160)
            .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .disabled(recorder.phase == .preparing || recorder.phase == .finalizing)
        .accessibilityLabel(recordButtonAccessibilityLabel(for: recorder.phase))
        .accessibilityHint(recorder.phase == .idle ? "Starts a new voice capture." : "")
    }

    private func handleRecordTap(_ recorder: AudioRecorderService) {
        switch recorder.phase {
        case .idle:
            Task {
                if await recorder.requestPermission() {
                    permissionDenied = false
                    await recorder.start(type: recorder.currentType)
                } else {
                    permissionDenied = true
                }
            }
        case .recording, .paused:
            Task { await recorder.stop() }
        case .preparing, .finalizing:
            break
        }
    }

    private func buttonBackground(for phase: AudioRecorderService.RecorderPhase) -> Color {
        switch phase {
        case .idle: return .accentColor
        case .preparing, .finalizing: return .gray
        case .recording: return .red
        case .paused: return .orange
        }
    }

    @ViewBuilder
    private func buttonGlyph(for phase: AudioRecorderService.RecorderPhase) -> some View {
        switch phase {
        case .idle:
            Image(systemName: "mic.fill")
                .font(.system(size: 56))
                .foregroundStyle(.white)
        case .preparing, .finalizing:
            ProgressView()
                .tint(.white)
                .controlSize(.large)
        case .recording, .paused:
            Image(systemName: "stop.fill")
                .font(.system(size: 52))
                .foregroundStyle(.white)
        }
    }

    private func recordButtonAccessibilityLabel(for phase: AudioRecorderService.RecorderPhase) -> String {
        switch phase {
        case .idle: return "Start recording"
        case .preparing: return "Preparing to record"
        case .recording, .paused: return "Stop and save recording"
        case .finalizing: return "Saving recording"
        }
    }

    // MARK: - Secondary controls

    @ViewBuilder
    private func secondaryControls(_ recorder: AudioRecorderService) -> some View {
        if recorder.phase == .recording || recorder.phase == .paused {
            HStack(spacing: 44) {
                Button {
                    if recorder.phase == .recording {
                        recorder.pause()
                    } else {
                        recorder.resume()
                    }
                } label: {
                    VStack(spacing: 6) {
                        Image(systemName: recorder.phase == .recording ? "pause.fill" : "play.fill")
                            .font(.title2)
                            .frame(width: 64, height: 64)
                            .background(Color(uiColor: .secondarySystemBackground), in: Circle())
                        Text(recorder.phase == .recording ? "Pause" : "Resume")
                            .font(.caption)
                    }
                }
                .buttonStyle(.plain)
                .accessibilityLabel(recorder.phase == .recording ? "Pause recording" : "Resume recording")

                Button(role: .destructive) {
                    showCancelConfirmation = true
                } label: {
                    VStack(spacing: 6) {
                        Image(systemName: "trash")
                            .font(.title2)
                            .foregroundStyle(.red)
                            .frame(width: 64, height: 64)
                            .background(Color(uiColor: .secondarySystemBackground), in: Circle())
                        Text("Discard")
                            .font(.caption)
                    }
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Discard recording")
                .accessibilityHint("Deletes the audio recorded so far.")
            }
        }
    }

    // MARK: - Microphone permission

    private var microphoneDeniedView: some View {
        VStack(spacing: 16) {
            Image(systemName: "mic.slash.fill")
                .font(.system(size: 48))
                .foregroundStyle(.secondary)
                .accessibilityHidden(true)
            Text("Microphone access is off")
                .font(.title3.weight(.semibold))
            Text("Memory Line needs the microphone to record captures. Turn it on in Settings.")
                .font(.callout)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
            Button("Open Settings") {
                if let url = URL(string: UIApplication.openSettingsURLString) {
                    openURL(url)
                }
            }
            .buttonStyle(.borderedProminent)
        }
        .padding()
    }

    private func refreshPermissionState() {
        permissionDenied = AVAudioApplication.shared.recordPermission == .denied
    }

    // MARK: - Sync footer

    @ViewBuilder
    private func syncFooter(_ uploads: UploadCoordinator) -> some View {
        VStack(spacing: 6) {
            if uploads.isSyncing {
                HStack(spacing: 8) {
                    ProgressView()
                    Text("Uploading…")
                }
                .font(.footnote)
                .foregroundStyle(.secondary)
            } else if uploads.pendingCount > 0 {
                Label(
                    "\(uploads.pendingCount) capture\(uploads.pendingCount == 1 ? "" : "s") waiting to upload",
                    systemImage: "arrow.up.circle"
                )
                .font(.footnote)
                .foregroundStyle(.secondary)
            }
            if let syncError = uploads.lastSyncError {
                Label(syncError, systemImage: "wifi.exclamationmark")
                    .font(.footnote)
                    .foregroundStyle(.orange)
                    .multilineTextAlignment(.center)
            }
        }
    }

    // MARK: - Formatting

    private static func timeString(_ interval: TimeInterval) -> String {
        let total = Int(interval.rounded(.down))
        let hours = total / 3600
        let minutes = (total % 3600) / 60
        let seconds = total % 60
        if hours > 0 {
            return String(format: "%d:%02d:%02d", hours, minutes, seconds)
        }
        return String(format: "%02d:%02d", minutes, seconds)
    }
}
