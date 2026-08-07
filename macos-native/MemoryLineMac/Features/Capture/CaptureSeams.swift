import Foundation

/// Protocol seams for macOS capture. Concrete types implement these in their own
/// files so the recorder, the device enumerator, the upload pipeline and the UI
/// can be built and reviewed independently.
///
/// These mirror the roles the iOS companion fills with `AudioRecorderService`
/// and `UploadCoordinator`, but are declared fresh: the iOS types depend on
/// `AVAudioSession` and `BGTaskScheduler`, neither of which exists on macOS,
/// and the Mac adds a concept the phone does not have — a choice of input
/// device (port plan §4.1).

// MARK: - Recording

/// Where a recording is in its local lifecycle. Distinct from
/// `CaptureLifecycleState`, which tracks the capture's journey to the server;
/// this is only about the microphone.
enum MacRecorderState: Equatable, Sendable {
    case idle
    case recording
    case paused
    /// Recording stopped because of an error. The message is display-safe.
    case failed(String)
}

/// Microphone authorization as the UI needs to see it.
enum MacMicrophoneAuthorization: Equatable, Sendable {
    case undetermined
    case authorized
    /// Denied or restricted — the app cannot re-prompt; the user must change it
    /// in System Settings ▸ Privacy & Security ▸ Microphone.
    case denied
}

/// Records microphone audio to a file the sync pipeline can upload.
///
/// **Output format is fixed: 16 kHz, mono, 16-bit PCM WAV.** That is what the
/// Windows side's Whisper transcription accepts (`WhisperCompatibleFormats` is
/// `.wav` only, and the model expects 16 kHz mono), and audio recorded here is
/// transcribed there. Do not "improve" the format without changing that path.
@MainActor
protocol MacAudioRecording: AnyObject {
    var state: MacRecorderState { get }
    /// Normalised 0...1 input level for a meter; 0 when not recording.
    var level: Double { get }
    /// Seconds recorded so far, excluding paused time.
    var elapsed: TimeInterval { get }

    /// Current authorization, without prompting.
    var authorization: MacMicrophoneAuthorization { get }
    /// Prompts if undetermined; returns the resulting authorization.
    func requestAuthorization() async -> MacMicrophoneAuthorization

    /// Begins recording to a new file in `AudioStorage.recordingsDirectory()`.
    /// Throws rather than entering `.failed` so the caller can surface the
    /// reason at the point of the user's action.
    func start(deviceId: String?) async throws

    func pause()
    func resume()

    /// Finalises the file and returns the persisted capture, or nil if nothing
    /// was recorded. The returned record is already in the store with state
    /// `.savedLocal`, so the upload pipeline can pick it up immediately.
    func stop() async throws -> CaptureRecord?

    /// Stops and discards: the partial file is deleted and no capture row
    /// survives.
    func cancel() async
}

// MARK: - Input devices

/// One selectable audio input.
struct MacAudioInputDevice: Identifiable, Equatable, Sendable {
    /// CoreAudio device UID — stable across reboots and reconnects, unlike the
    /// numeric AudioObjectID, so this is what gets persisted.
    let id: String
    let name: String
    /// True for the device the system currently treats as default input.
    let isSystemDefault: Bool
}

/// Enumerates audio inputs and reports changes. Macs routinely gain and lose
/// inputs mid-session (headsets, docks, virtual devices), which the phone never
/// does, so the UI has to react rather than read once.
@MainActor
protocol MacAudioDeviceEnumerating: AnyObject {
    /// Currently connected inputs, system default first.
    var devices: [MacAudioInputDevice] { get }
    /// Re-reads the device list. Safe to call repeatedly.
    func refresh()
    /// Starts observing hardware changes and refreshing `devices`.
    func startObserving()
    func stopObserving()
}

// MARK: - Upload

/// Drives a locally saved capture to the sync service: create the capture,
/// initiate the artifact, upload its parts, complete it.
///
/// Mirrors the iOS `UploadCoordinator`'s responsibilities, minus the
/// `BGTaskScheduler` retry scheduling — on macOS the app is running or it is
/// not, so retries ride a foreground loop.
@MainActor
protocol MacUploading: AnyObject {
    /// Uploads everything in the store whose state `isUploadPending`, oldest
    /// first. Safe to call while a pass is running (the second call returns).
    func drainPendingUploads() async
    /// Called when a recording finalises, so it uploads without waiting for
    /// the next tick.
    func captureFinalized(_ capture: CaptureRecord)
}

// MARK: - Settings

/// Setting keys owned by macOS capture.
enum MacCaptureSettingsKey {
    /// Persisted `MacAudioInputDevice.id` (a CoreAudio UID). Absent or unknown
    /// means "follow the system default", which is the desired behaviour when
    /// the chosen device is unplugged.
    static let inputDeviceId = "mac_input_device_id"
}
