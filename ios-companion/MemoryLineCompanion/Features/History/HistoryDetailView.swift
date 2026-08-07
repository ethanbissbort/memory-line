import Combine
import SwiftUI

/// Detail screen for one capture: metadata, audio playback with a read-only
/// progress bar, sync status, the last error, and a retry control for
/// captures that still need to upload.
@MainActor
struct HistoryDetailView: View {
    @Environment(AppEnvironment.self) private var env
    let captureId: String

    @State private var record: CaptureRecord
    @State private var playback = PlaybackController()
    @State private var isRetrying = false

    /// Drives playback progress updates while the screen is visible.
    private let playbackTimer = Timer.publish(every: 0.25, on: .main, in: .common).autoconnect()

    /// Seeded with the row's record so the screen renders immediately;
    /// `reload()` re-reads the database whenever upload state moves.
    init(record: CaptureRecord) {
        self.captureId = record.id
        _record = State(initialValue: record)
    }

    var body: some View {
        detailList(record)
            .navigationTitle(record.type.displayName)
            .navigationBarTitleDisplayMode(.inline)
            .onAppear {
                // Playback must never touch the shared audio session while a
                // recording is live (mirrors the onDisappear guard below).
                playback.isRecordingActive = { env.recorder.phase != .idle }
                reload()
            }
            .onChange(of: env.uploads.pendingCount) { reload() }
            .onChange(of: env.uploads.isSyncing) { reload() }
            .onReceive(playbackTimer) { _ in playback.tick() }
            .onDisappear {
                // Don't tear down the shared audio session if a recording is live.
                playback.stop(deactivateSession: env.recorder.phase == .idle)
            }
    }

    private func detailList(_ record: CaptureRecord) -> some View {
        List {
            Section("Recording") {
                LabeledContent("Type", value: record.type.displayName)
                LabeledContent("Captured", value: record.capturedAt.formatted(date: .abbreviated, time: .shortened))
                LabeledContent("Duration") {
                    Text(Duration.seconds(max(record.durationSeconds, 0)),
                         format: .time(pattern: .minuteSecond))
                }
                if record.byteLength > 0 {
                    LabeledContent("Size") {
                        Text(record.byteLength, format: .byteCount(style: .file))
                    }
                }
            }

            if let note = record.userNote, !note.isEmpty {
                Section("Note") {
                    Text(note)
                }
            }

            if record.state != .recording {
                Section("Playback") {
                    playbackControls(record)
                }
            }

            Section("Sync") {
                HStack {
                    Text("Status")
                    Spacer()
                    CaptureStatusChip(state: record.state)
                }
                if record.uploadAttempts > 0 {
                    LabeledContent("Upload attempts", value: "\(record.uploadAttempts)")
                }
                if let uploadedAt = record.uploadedAt {
                    LabeledContent("Uploaded", value: uploadedAt.formatted(date: .abbreviated, time: .shortened))
                }
                if let lastError = record.lastError, record.state == .failedRecoverable {
                    Text(lastError)
                        .font(.footnote)
                        .foregroundStyle(.red)
                }
                if record.state.isUploadPending {
                    retryButton(record)
                }
            }
        }
    }

    private func playbackControls(_ record: CaptureRecord) -> some View {
        VStack(spacing: 8) {
            HStack(spacing: 14) {
                Button {
                    togglePlayback(record)
                } label: {
                    Image(systemName: playback.isPlaying ? "pause.circle.fill" : "play.circle.fill")
                        .font(.system(size: 40))
                        .foregroundStyle(.tint)
                }
                .buttonStyle(.plain)
                // No playback while the recorder is live — starting it would
                // reconfigure the shared audio session under the recording.
                .disabled(env.recorder.phase != .idle)
                .accessibilityLabel(playback.isPlaying ? "Pause" : "Play")

                VStack(spacing: 4) {
                    ProgressView(
                        value: min(playback.progress, displayDuration(record)),
                        total: max(displayDuration(record), 0.01))
                    HStack {
                        Text(Duration.seconds(max(playback.progress, 0)),
                             format: .time(pattern: .minuteSecond))
                        Spacer()
                        Text(Duration.seconds(max(displayDuration(record), 0)),
                             format: .time(pattern: .minuteSecond))
                    }
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                }
            }
            if let playbackError = playback.errorMessage {
                Text(playbackError)
                    .font(.footnote)
                    .foregroundStyle(.red)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .padding(.vertical, 4)
    }

    private func retryButton(_ record: CaptureRecord) -> some View {
        Button {
            isRetrying = true
            Task {
                await env.uploads.retry(captureId: captureId)
                isRetrying = false
                reload()
            }
        } label: {
            HStack {
                Label(
                    record.state == .failedRecoverable ? "Retry upload" : "Upload now",
                    systemImage: "arrow.clockwise")
                Spacer()
                if isRetrying {
                    ProgressView()
                }
            }
        }
        .disabled(isRetrying || env.uploads.isSyncing)
    }

    /// Prefer the player's authoritative duration once a file is loaded.
    private func displayDuration(_ record: CaptureRecord) -> TimeInterval {
        playback.duration > 0 ? playback.duration : max(record.durationSeconds, 0)
    }

    private func togglePlayback(_ record: CaptureRecord) {
        do {
            let fileURL = try AudioStorage.url(forFileName: record.fileName)
            playback.toggle(fileURL: fileURL)
        } catch {
            playback.errorMessage = "Couldn't locate the audio file for this capture."
        }
    }

    /// Re-reads this capture from the store; keeps the last known snapshot if
    /// the row vanished (e.g. deleted from another screen).
    private func reload() {
        if let fetched = try? env.captures.capture(id: captureId), fetched != record {
            record = fetched
        }
    }
}
