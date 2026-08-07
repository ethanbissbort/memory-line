import AVFAudio
import Foundation
import Observation
import os

/// Small `AVAudioPlayer` wrapper for playing a capture back in the history
/// detail screen. State is polled by the owning view (a lightweight timer
/// calls `tick()`), which sidesteps `AVAudioPlayerDelegate` entirely.
///
/// Errors surface through `errorMessage` for the UI; logs never contain audio
/// content (design §14.5) — only that playback failed.
@MainActor
@Observable
final class PlaybackController {
    /// True while audio is actually playing.
    private(set) var isPlaying = false
    /// Current position in seconds.
    private(set) var progress: TimeInterval = 0
    /// Total duration of the loaded file in seconds; 0 until a file loads.
    private(set) var duration: TimeInterval = 0
    /// Human-readable playback problem for the UI, nil when healthy.
    var errorMessage: String?

    private var player: AVAudioPlayer?
    private var loadedFileURL: URL?
    private let logger = AppLog.logger(category: "playback")

    /// Reports whether the capture recorder is live. Injected by the owning
    /// view (from `AudioRecorderService.phase`) so playback never reconfigures
    /// the shared audio session out from under an active recording — switching
    /// the category yanks the recorder's input route with a `.categoryChange`
    /// route-change reason, which its pause guards deliberately ignore.
    /// Defaults to "not recording" for owners without a recorder.
    @ObservationIgnored var isRecordingActive: @MainActor () -> Bool = { false }

    /// Plays when stopped/paused, pauses when playing.
    func toggle(fileURL: URL) {
        if isPlaying {
            pause()
        } else {
            play(fileURL: fileURL)
        }
    }

    /// Loads the file if needed, activates a playback audio session, and
    /// starts (or resumes) playing. Refuses while a recording is active —
    /// see `isRecordingActive`.
    func play(fileURL: URL) {
        errorMessage = nil
        guard !isRecordingActive() else {
            errorMessage = "Playback isn't available while a recording is in progress."
            logger.info("playback refused: recording active")
            return
        }
        do {
            if player == nil || loadedFileURL != fileURL {
                player?.stop()
                let loaded = try AVAudioPlayer(contentsOf: fileURL)
                loaded.prepareToPlay()
                player = loaded
                loadedFileURL = fileURL
                duration = loaded.duration
                progress = 0
            }
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(.playback, mode: .spokenAudio, options: [])
            try session.setActive(true)
        } catch {
            logger.error("playback setup failed: \(String(describing: error), privacy: .public)")
            errorMessage = "Couldn't play this recording. The audio file may be missing or damaged."
            return
        }
        guard player?.play() == true else {
            errorMessage = "Playback couldn't start."
            return
        }
        isPlaying = true
    }

    func pause() {
        player?.pause()
        isPlaying = false
        progress = player?.currentTime ?? progress
    }

    /// Polled by the view while visible: refreshes progress and notices the
    /// natural end of playback (the player stops itself).
    func tick() {
        guard let player else { return }
        if player.isPlaying {
            isPlaying = true
            progress = player.currentTime
        } else if isPlaying {
            // Reached the end (or the system interrupted us).
            isPlaying = false
            progress = 0
        }
    }

    /// Stops playback and releases the player. Deactivating the shared audio
    /// session is the caller's choice — skip it when a recording is active so
    /// we never yank the session out from under the recorder.
    func stop(deactivateSession: Bool) {
        player?.stop()
        player = nil
        loadedFileURL = nil
        isPlaying = false
        progress = 0
        if deactivateSession {
            try? AVAudioSession.sharedInstance().setActive(false, options: [.notifyOthersOnDeactivation])
        }
    }
}
