import Foundation
import Observation
import os

/// Where a capture's audio and its temporary part slices live.
///
/// The iOS `UploadCoordinator` calls `AudioStorage` directly. On the Mac the two
/// locations are injected instead, with `AudioStorage` as the production
/// default, for one reason: the unit tests run inside the sandboxed app host, so
/// calling `AudioStorage.recordingsDirectory()` from a test would write fixture
/// audio into the *real* Application Support container alongside the user's
/// recordings. The upload sequence is what the tests are pinning, not the
/// storage layout, and the storage layout is already covered on the iOS side.
struct MacUploadFileLocations: Sendable {
    /// Absolute URL of a finalized recording, from `CaptureRecord.fileName`.
    var recordingURL: @Sendable (String) throws -> URL
    /// Directory that holds `part_*` slices while a multi-part upload runs.
    var partsDirectory: @Sendable () throws -> URL

    /// The real app locations (Application Support for recordings, Caches for
    /// slices — see `AudioStorage`).
    static let appStorage = MacUploadFileLocations(
        recordingURL: { try AudioStorage.url(forFileName: $0) },
        partsDirectory: { try AudioStorage.partsDirectory() })
}

/// Drives locally saved captures to the sync service (design §16): create the
/// capture, initiate its artifact, upload the parts, complete it — persisting
/// every state transition through `CaptureStore` *before* acting on it.
///
/// Like `MacSyncCoordinator`, this is a deliberate reimplementation of its iOS
/// counterpart rather than shared code. The iOS `UploadCoordinator` is built on
/// `BGTaskScheduler` (deferred retry requests, expiration handlers, an
/// `autoUpload` switch whose only job is to stop the system waking the app) and
/// publishes a WidgetKit snapshot after every pass. macOS has neither: the app
/// is running or it is not, and `WidgetStatusPublisher.swift` is explicitly
/// excluded from this target. Retries therefore ride a foreground loop —
/// `startPeriodicDrain` — plus whatever the UI and `captureFinalized` kick.
///
/// Everything that is a **contract with the sync service** rather than a local
/// choice is mirrored exactly, and pinned by `MacUploadCoordinatorTests`:
///
///  - the request order is create → initiate → parts → complete;
///  - the artifact ID is minted once and persisted **before** the first
///    initiate, so a retry re-declares the same artifact instead of orphaning a
///    new one on the server for every attempt;
///  - `.uploading`, the attempt count and the cleared error are persisted
///    **before** the first request goes out, so a process killed mid-upload
///    resumes (see `recoverInterruptedUploads`) instead of double-uploading;
///  - a *retryable* failure (network, 408/429/5xx, or a server-flagged
///    retryable error) parks the capture in `.failedRecoverable` and aborts the
///    rest of the pass, because every later capture would hit the same
///    condition; a *non-retryable* 4xx parks the capture the same way but lets
///    the pass continue with the next capture;
///  - a 401 parks the capture and aborts the pass with a re-pair message —
///    hammering a revoked device only burns battery and server logs;
///  - each capture is attempted **at most once per drain**, so a server that
///    fails every request can never spin this loop.
///
/// Threading: `@MainActor` for the observable state; the per-capture pipeline
/// (file slicing and network I/O) runs in `nonisolated` methods so an upload
/// never blocks the UI.
@MainActor
@Observable
final class MacUploadCoordinator: MacUploading {
    /// What the UI shows. `.failed` carries text already safe to display: it
    /// never contains transcript content, file paths or credentials
    /// (design §14.5).
    enum State: Equatable {
        case idle
        /// Bytes are moving. Set only once a drain has found work.
        case uploading
        case failed(String)
    }

    private(set) var state: State = .idle
    /// Captures waiting to reach the sync service (`state.isUploadPending`).
    private(set) var pendingCount: Int = 0
    /// When the most recent capture completed; nil until one does.
    private(set) var lastUploadedAt: Date?

    /// True from the moment a drain takes the in-flight guard until it releases
    /// it, and it *is* that guard: the class is `@MainActor`, so check-and-set
    /// is race-free and a second `drainPendingUploads()` sees `true` and
    /// returns. Observable rather than `@ObservationIgnored` because a UI
    /// "Upload now" button must stay disabled for the whole pass, including the
    /// stretch before `state` becomes `.uploading`.
    private(set) var isDraining = false

    private let store: any CaptureStore
    private let settings: any SettingsStore
    private let api: any SyncAPI
    private let locations: MacUploadFileLocations
    private let logger = AppLog.logger(category: "upload")

    /// Raised when `captureFinalized` lands while a drain is already running.
    /// The running drain consumes it and sweeps the queue again, so a recording
    /// finished mid-pass uploads now rather than waiting for the next tick.
    @ObservationIgnored private var rerunRequested = false

    @ObservationIgnored private var ticker: Task<Void, Never>?

    /// Tokens are not injected the way iOS injects them: `SyncAPIClient` owns
    /// the bearer token and its refresh, and this coordinator never touches
    /// credentials. `MacSyncCoordinator` takes the same view.
    init(
        store: any CaptureStore,
        settings: any SettingsStore,
        api: any SyncAPI,
        locations: MacUploadFileLocations = .appStorage
    ) {
        self.store = store
        self.settings = settings
        self.api = api
        self.locations = locations
        self.pendingCount = (try? store.pendingUploadCount()) ?? 0
    }

    // No deinit cancelling the ticker: the loop captures self weakly, so it
    // cannot keep this object alive. Callers that need it stopped (unpairing)
    // call stopPeriodicDrain explicitly — same contract as MacSyncCoordinator.

    /// True once this Mac is paired and sync has not been switched off. The
    /// three conditions are exactly `MacSyncCoordinator.canSync`: a device ID
    /// without a server URL (or the reverse) is a half-finished pairing, and
    /// uploading on it would only produce 401s.
    ///
    /// `AppSettingsKey.autoUpload` is deliberately **not** consulted. On iOS it
    /// gates whether the *system* is asked to wake the app in the background;
    /// on macOS every drain is already the consequence of the app running, a
    /// finished recording, or an explicit user action.
    var canUpload: Bool {
        settings.bool(AppSettingsKey.syncEnabled, default: false)
            && settings.string(AppSettingsKey.deviceId)?.isEmpty == false
            && settings.string(AppSettingsKey.serverURL)?.isEmpty == false
    }

    // MARK: - Public surface

    /// Starts the foreground retry loop. macOS has no `BGTaskScheduler` and does
    /// not need one — the app is either running (and can simply keep a timer) or
    /// not running (and has nothing to upload). Calling this again replaces the
    /// existing ticker rather than stacking a second one.
    func startPeriodicDrain(interval: Duration = .seconds(120)) {
        ticker?.cancel()
        ticker = Task { [weak self] in
            while !Task.isCancelled {
                await self?.drainPendingUploads()
                do {
                    try await Task.sleep(for: interval)
                } catch {
                    return // cancelled
                }
            }
        }
    }

    func stopPeriodicDrain() {
        ticker?.cancel()
        ticker = nil
    }

    /// Uploads everything whose state `isUploadPending`, oldest first. Safe to
    /// call while a pass is running: the second call returns immediately rather
    /// than interleaving two writers over the same capture rows and the same
    /// part-slice directory.
    func drainPendingUploads() async {
        guard !isDraining else {
            // Not a rerun request: a caller polling the drain (the ticker, a
            // toolbar button) has no new work to announce. Only a finalized
            // capture does, and captureFinalized raises the flag itself.
            return
        }
        isDraining = true
        defer { isDraining = false }

        refreshPendingCount()

        guard canUpload else {
            // Stay quiet when nothing is waiting: an unpaired Mac with an empty
            // queue is a normal state, not an error worth showing.
            if pendingCount > 0 {
                state = .failed(settings.bool(AppSettingsKey.syncEnabled, default: false)
                    ? "This Mac is not paired with a sync server. Pair in Settings to upload."
                    : "Sync is turned off. Turn it on in Settings to upload.")
            }
            return
        }
        guard let deviceId = settings.string(AppSettingsKey.deviceId), !deviceId.isEmpty else {
            return // unreachable: canUpload already required it
        }

        // Captures stranded in `.uploading` by a crash or a kill rejoin the
        // queue before it is read.
        recoverInterruptedUploads()

        // Sweep repeatedly: a capture finalized while a sweep is in flight
        // arrives as a `rerunRequested` kick (or simply as a fresh row in the
        // re-query below) and uploads in a follow-up sweep instead of stranding
        // until the next tick. `attemptedIds` bounds the whole drain to one
        // attempt per capture, so a persistently failing server cannot spin it.
        var passError: String?
        var attemptedIds: Set<String> = []

        repeat {
            rerunRequested = false

            let queue: [CaptureRecord]
            do {
                queue = try store.pendingUploads()
            } catch {
                passError = "Could not read the upload queue."
                logger.error("pendingUploads query failed: \(String(describing: error), privacy: .public)")
                break
            }
            let sweep = queue.filter { !attemptedIds.contains($0.id) }
            guard !sweep.isEmpty else { break }

            logger.info("upload sweep started pending=\(sweep.count)")
            state = .uploading

            var aborted = false
            for record in sweep {
                attemptedIds.insert(record.id)
                let outcome = await processCapture(record, deviceId: deviceId)
                refreshPendingCount()
                switch outcome {
                case .uploaded:
                    lastUploadedAt = Date()
                case .skipped:
                    break
                case .failed(let message, let abortPass):
                    passError = message
                    if abortPass {
                        aborted = true
                    }
                }
                if aborted { break }
            }
            if aborted { break }
        } while rerunRequested || hasUnattemptedPendingWork(excluding: attemptedIds)

        refreshPendingCount()
        if let passError {
            state = .failed(passError)
        } else {
            state = .idle
        }
        logger.info("upload pass finished pending=\(self.pendingCount) failed=\(passError != nil)")
    }

    /// Called by the recorder right after a capture is finalized and saved.
    /// Persists nothing — the recorder already did, which is what makes the
    /// capture durable whether or not this kick ever runs.
    func captureFinalized(_ capture: CaptureRecord) {
        refreshPendingCount()
        logger.info("capture finalized id=\(capture.id, privacy: .public) pending=\(self.pendingCount)")
        guard !isDraining else {
            // A drain is mid-sweep; tell it to look again rather than starting a
            // second one over the same rows.
            rerunRequested = true
            return
        }
        Task { await self.drainPendingUploads() }
    }

    // MARK: - One capture

    /// True when the queue holds captures this drain has not attempted yet —
    /// e.g. finalized while the last sweep was transferring. A query failure
    /// reads as "no new work" so it cannot keep the loop alive.
    private func hasUnattemptedPendingWork(excluding attemptedIds: Set<String>) -> Bool {
        guard let queue = try? store.pendingUploads() else { return false }
        return queue.contains { !attemptedIds.contains($0.id) }
    }

    /// Outcome of one capture's trip through the pipeline.
    private enum CaptureOutcome {
        /// Uploaded and persisted as `.uploaded`.
        case uploaded
        /// No longer pending (uploaded elsewhere, deleted, ...); nothing done.
        case skipped
        /// Failed; `abortPass` stops the drain (auth or network-wide problems).
        case failed(message: String, abortPass: Bool)
    }

    /// Uploads one capture: create (idempotent by captureId) → initiate
    /// (idempotent for an identical re-declaration) → parts (a re-PUT
    /// overwrites) → complete (idempotent for the same sha256). Runs off the
    /// main actor.
    nonisolated private func processCapture(_ pending: CaptureRecord, deviceId: String) async -> CaptureOutcome {
        // Re-read rather than trusting the queued snapshot: the row may have
        // moved on since the sweep query (a manual retry, a delete).
        var capture: CaptureRecord
        do {
            guard let fresh = try store.capture(id: pending.id) else { return .skipped }
            capture = fresh
        } catch {
            return .failed(message: "Could not read the capture from local storage.", abortPass: false)
        }
        // Entry states are whatever `isUploadPending` covers: `.savedLocal`
        // straight from the recorder, `.queuedUpload` if the recorder queued it
        // explicitly, or `.failedRecoverable` left by an earlier attempt. From
        // here the row moves to `.uploading` and then either `.uploaded` or back
        // to `.failedRecoverable`. Anything else — including a capture another
        // pass finished while this one was queued — is skipped untouched.
        guard capture.state.isUploadPending else { return .skipped }

        // Preconditions: the finalized audio file and its integrity metadata.
        // All three failures are permanent for this capture, so they park it
        // without ever touching the network.
        let fileURL: URL
        do {
            fileURL = try locations.recordingURL(capture.fileName)
        } catch {
            return markFailed(&capture, message: "Could not locate the recording file.", abortPass: false)
        }
        guard FileManager.default.fileExists(atPath: fileURL.path) else {
            return markFailed(
                &capture,
                message: "The audio file for this capture is missing on this Mac.",
                abortPass: false)
        }
        guard !capture.sha256.isEmpty, capture.byteLength > 0 else {
            // Finalization never completed its metadata write — treat as corrupt.
            return markFailed(
                &capture,
                message: "The recording was not finalized correctly (missing hash).",
                abortPass: false)
        }
        if let attributes = try? FileManager.default.attributesOfItem(atPath: fileURL.path),
           let size = attributes[.size] as? NSNumber,
           size.int64Value != capture.byteLength {
            // The declared sha256 would no longer match, so the server would
            // reject the completion claim anyway; fail here with a reason a
            // human can act on.
            return markFailed(
                &capture,
                message: "The audio file changed after it was finalized.",
                abortPass: false)
        }

        // Durability: `.uploading` is persisted BEFORE the first request. A
        // crash from here on leaves a row `recoverInterruptedUploads` can find,
        // rather than a capture that looks pending while its bytes are already
        // on the server.
        capture.state = .uploading
        capture.uploadAttempts += 1
        capture.lastError = nil
        do {
            try store.update(capture)
        } catch {
            return .failed(message: "Could not update local storage.", abortPass: false)
        }

        do {
            // 1. Capture metadata. An idempotent replay returns 200 with the
            //    existing capture, so re-runs after partial failures are safe.
            _ = try await api.createCapture(CaptureCreateRequest(
                captureId: capture.id,
                sourceDeviceId: deviceId,
                captureType: capture.type.rawValue,
                capturedAtUtc: capture.capturedAt,
                timezoneId: capture.timezoneId,
                localOffsetMinutes: capture.localOffsetMinutes,
                tripId: nil,
                titleHint: capture.titleHint,
                userNote: capture.userNote,
                // The Mac has no location capture (port plan §4.1); the phone's
                // trip context has no counterpart here.
                locationContext: nil,
                clientSchemaVersion: 1))

            // 2. Stable artifact ID, persisted BEFORE initiate so retries
            //    re-declare the same artifact instead of minting duplicates.
            let artifactId: String
            if let existing = capture.artifactId, !existing.isEmpty {
                artifactId = existing
            } else {
                artifactId = UUID().uuidString.lowercased()
                capture.artifactId = artifactId
                try store.update(capture)
            }

            // 3. Declare the artifact; the server answers with the part plan.
            let plan = try await api.initiateArtifact(captureId: capture.id, ArtifactInitiateRequest(
                artifactId: artifactId,
                artifactType: "audio_original",
                mediaType: Self.mediaType(forFileName: capture.fileName),
                originalFileName: capture.fileName,
                expectedByteLength: capture.byteLength,
                expectedSha256: capture.sha256))
            if plan.artifactId != artifactId {
                // Defensive: keep the record aligned with the server's ID.
                capture.artifactId = plan.artifactId
                try store.update(capture)
            }

            // 4. Upload the bytes. A 409 here is `artifact_invalid_state` — the
            //    server already completed this artifact and an earlier pass lost
            //    the /complete response, so it now rejects part re-PUTs. Fall
            //    through to the completion claim, which the server replays with
            //    200 for a matching sha256; the capture only fails if that
            //    completion fails too. (Initiate needs no such fallback: an
            //    identical re-declaration replays 200 even for a completed
            //    artifact, and its only 409, `artifact_conflict`, means
            //    genuinely mismatched declarations.)
            do {
                try await uploadParts(
                    for: capture,
                    artifactId: plan.artifactId,
                    partSizeBytes: plan.partSizeBytes,
                    expectedPartCount: plan.expectedPartCount,
                    fileURL: fileURL)
            } catch let error as SyncAPIError
                where error.statusCode == 409 || error.apiError?.code == "artifact_invalid_state" {
                logger.info("part upload got 409 for id=\(capture.id, privacy: .public); replaying completion for the finished artifact")
            }

            // 5. Completion claim — validated server-side against the
            //    declaration and the assembled bytes.
            _ = try await api.completeArtifact(artifactId: plan.artifactId, ArtifactCompleteRequest(
                partCount: plan.expectedPartCount,
                totalByteLength: capture.byteLength,
                sha256: capture.sha256))

            capture.state = .uploaded
            capture.uploadedAt = Date()
            capture.lastError = nil
            try store.update(capture)
            logger.info("uploaded capture id=\(capture.id, privacy: .public) attempts=\(capture.uploadAttempts)")
            return .uploaded
        } catch let error as SyncAPIError {
            let message: String
            let abortPass: Bool
            if error.needsReauthentication {
                message = "This Mac is no longer paired. Re-pair in Settings to resume uploads."
                abortPass = true
            } else if error.retryable {
                // Network or server-wide condition — later captures would hit it
                // too, so stop and let the next drain retry the whole queue.
                message = error.message
                abortPass = true
            } else {
                // A 4xx that will never succeed for this capture. Park it and
                // move on: the rest of the queue may be perfectly uploadable.
                message = error.message
                abortPass = false
            }
            logger.warning("upload failed id=\(capture.id, privacy: .public) status=\(error.statusCode ?? -1) retryable=\(error.retryable)")
            return markFailed(&capture, message: message, abortPass: abortPass)
        } catch {
            // Anything else is local and capture-specific: a file-system error
            // out of the slicer, or a cancellation.
            //
            // Deliberate divergence from iOS, which surfaces
            // `error.localizedDescription` here: Foundation's file errors name
            // the file (and sometimes its path) in that string, and
            // `lastError` is display text (design §14.5). The type goes to the
            // log instead, where a developer can pair it with the capture ID.
            logger.error("upload failed id=\(capture.id, privacy: .public) error=\(String(describing: type(of: error)), privacy: .public)")
            return markFailed(
                &capture,
                message: "The upload could not be completed on this Mac; it will be retried.",
                abortPass: false)
        }
    }

    /// Parks the capture in `.failedRecoverable` with a user-facing reason and
    /// persists it, so the Library can surface the failure and the next drain
    /// picks it up again (`.failedRecoverable.isUploadPending` is true).
    nonisolated private func markFailed(
        _ capture: inout CaptureRecord,
        message: String,
        abortPass: Bool
    ) -> CaptureOutcome {
        capture.state = .failedRecoverable
        capture.lastError = message
        // Copied out before logging: Logger's interpolation is an @autoclosure,
        // which cannot capture an inout parameter.
        let captureId = capture.id
        do {
            try store.update(capture)
        } catch {
            logger.error("could not persist failure state for id=\(captureId, privacy: .public)")
        }
        return .failed(message: message, abortPass: abortPass)
    }

    // MARK: - Part slicing and upload

    /// Uploads the audio bytes according to the server's part plan. A file that
    /// fits one part is streamed directly from its own URL (no copy); larger
    /// files are sliced into `part_%05d` files in the parts directory, uploaded
    /// in order, and each slice is deleted as soon as its part succeeds.
    /// Leftover slices (from an earlier interrupted pass) are removed before
    /// slicing and after completion.
    nonisolated private func uploadParts(
        for capture: CaptureRecord,
        artifactId: String,
        partSizeBytes: Int64,
        expectedPartCount: Int,
        fileURL: URL
    ) async throws {
        guard partSizeBytes > 0, expectedPartCount >= 1 else {
            throw SyncAPIError(
                statusCode: nil, apiError: nil, retryable: false,
                message: "The server returned an invalid upload plan.")
        }

        if expectedPartCount == 1 {
            try await api.uploadArtifactPart(artifactId: artifactId, partNumber: 1, fileURL: fileURL)
            return
        }

        // The part count is derived from the byte length the server was told, so
        // a mismatch means the plan and the declaration disagree; uploading
        // anyway would fail the completion claim after moving every byte.
        let localPartCount = Int((capture.byteLength + partSizeBytes - 1) / partSizeBytes)
        guard localPartCount == expectedPartCount else {
            throw SyncAPIError(
                statusCode: nil, apiError: nil, retryable: false,
                message: "The server's upload plan does not match the file size.")
        }

        let partsDirectory = try locations.partsDirectory()
        cleanUpPartFiles(in: partsDirectory)

        let reader = try FileHandle(forReadingFrom: fileURL)
        defer { try? reader.close() }

        var remainingTotal = capture.byteLength
        for partNumber in 1...expectedPartCount {
            let partURL = partsDirectory.appendingPathComponent(String(format: "part_%05ld", partNumber))
            let partLength = Swift.min(partSizeBytes, remainingTotal)
            try slice(from: reader, to: partURL, byteCount: partLength)
            try await api.uploadArtifactPart(artifactId: artifactId, partNumber: partNumber, fileURL: partURL)
            try? FileManager.default.removeItem(at: partURL)
            remainingTotal -= partLength
        }

        cleanUpPartFiles(in: partsDirectory)
    }

    /// Streams `byteCount` bytes from `reader` into a fresh file at `partURL`
    /// using bounded 1 MiB chunks, so slicing never loads a part into memory —
    /// a Mac recording an hour of a meeting has the same shape as a phone
    /// recording an hour of driving.
    nonisolated private func slice(from reader: FileHandle, to partURL: URL, byteCount: Int64) throws {
        let fileManager = FileManager.default
        if fileManager.fileExists(atPath: partURL.path) {
            try fileManager.removeItem(at: partURL)
        }
        guard fileManager.createFile(atPath: partURL.path, contents: nil) else {
            throw SyncAPIError(
                statusCode: nil, apiError: nil, retryable: false,
                message: "Could not create a temporary upload file.")
        }
        let writer = try FileHandle(forWritingTo: partURL)
        // FileHandle.write(contentsOf:) writes through to disk before returning,
        // so the deferred close only releases the descriptor.
        defer { try? writer.close() }

        var remaining = byteCount
        while remaining > 0 {
            let chunkSize = Int(Swift.min(remaining, 1_048_576))
            guard let chunk = try reader.read(upToCount: chunkSize), !chunk.isEmpty else {
                throw SyncAPIError(
                    statusCode: nil, apiError: nil, retryable: false,
                    message: "The audio file is shorter than its recorded size.")
            }
            try writer.write(contentsOf: chunk)
            remaining -= Int64(chunk.count)
        }
    }

    /// Removes every `part_*` slice in the parts directory. The in-flight guard
    /// means only one drain runs at a time, so a blanket sweep is safe.
    nonisolated private func cleanUpPartFiles(in directory: URL) {
        let fileManager = FileManager.default
        guard let names = try? fileManager.contentsOfDirectory(atPath: directory.path) else { return }
        for name in names where name.hasPrefix("part_") {
            try? fileManager.removeItem(at: directory.appendingPathComponent(name))
        }
    }

    /// Wire media type for a recording, derived from its file extension.
    ///
    /// iOS hard-codes `audio/mp4` because its recorder only ever writes AAC in
    /// an m4a container. The Mac recorder writes 16 kHz mono PCM WAV — the only
    /// format the Windows side's Whisper transcription accepts (see
    /// `MacAudioRecording`) — so copying the iOS constant would declare a lie
    /// about the bytes. Windows falls back to this value when it needs a file
    /// extension for a downloaded artifact
    /// (`MemoryTimeline.Sync/RemoteChangeApplier.ExtensionFor`) and the sync
    /// service echoes it as the download `Content-Type`.
    ///
    /// An unrecognised extension is declared as opaque bytes rather than
    /// guessed; `originalFileName` still carries the real name, which is what
    /// Windows prefers anyway.
    nonisolated private static func mediaType(forFileName fileName: String) -> String {
        switch URL(fileURLWithPath: fileName).pathExtension.lowercased() {
        case "wav": return "audio/wav"
        case "m4a", "mp4": return "audio/mp4"
        case "mp3": return "audio/mpeg"
        case "aac": return "audio/aac"
        case "flac": return "audio/flac"
        default: return "application/octet-stream"
        }
    }

    // MARK: - Housekeeping

    /// Captures left in `.uploading` by a crash or a process kill would never be
    /// picked up again (`isUploadPending` excludes `.uploading`); repark them as
    /// `.failedRecoverable` so this drain retries them. The artifact ID minted
    /// by the interrupted attempt survives on the row, which is what makes the
    /// retry a resume rather than a duplicate upload.
    ///
    /// Only called while the in-flight guard is held, so no live upload can be
    /// reparked out from under itself.
    private func recoverInterruptedUploads() {
        guard let all = try? store.allCaptures() else { return }
        for var record in all where record.state == .uploading {
            record.state = .failedRecoverable
            record.lastError = "Upload was interrupted."
            try? store.update(record)
            logger.warning("recovered interrupted upload id=\(record.id, privacy: .public)")
        }
    }

    /// Refreshes the queue depth for the UI. A failed count keeps the previous
    /// value rather than showing a misleading zero.
    private func refreshPendingCount() {
        pendingCount = (try? store.pendingUploadCount()) ?? pendingCount
    }
}
