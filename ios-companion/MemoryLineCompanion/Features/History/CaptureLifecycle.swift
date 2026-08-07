import Foundation
import SwiftUI

/// Presentation model for a capture's *whole* lifecycle (design §19 Phase 3).
///
/// A capture has two legs and this phone only owns the first one:
///
/// - the upload leg (`CaptureRecord.state`) — recorded, saved, uploading,
///   uploaded, or failed to upload; authored here;
/// - the processing leg (`CaptureStatusRecord`, applied from `capture_status`
///   sync changes) — received, processing, review-ready, completed, or failed;
///   authored by Windows.
///
/// `CaptureLifecycleSummary` fuses the two into one position the user can read
/// at a glance, plus the storage scope that answers the question the upload
/// state alone cannot: is this recording actually safe anywhere but here?
/// Everything here is read-only — transcript editing and event approval stay on
/// Windows, which is the boundary this phase exists to make legible.

// MARK: - Storage scope

/// Where a capture is known to exist right now. This is the difference between
/// "sent" and "actually safe", so it is always rendered as text plus a symbol
/// and never encoded in colour alone (design §15).
enum CaptureStorageScope: Equatable {
    /// Only this iPhone has the audio — losing the phone loses the recording.
    case phoneOnly
    /// The audio reached the sync service; the PC has not reported it yet.
    case syncService
    /// Windows has the capture and is the authority for it from here on.
    case computer

    var label: String {
        switch self {
        case .phoneOnly: return "On this iPhone only"
        case .syncService: return "Sent to your sync server"
        case .computer: return "On your PC"
        }
    }

    var symbolName: String {
        switch self {
        case .phoneOnly: return "iphone"
        case .syncService: return "server.rack"
        case .computer: return "desktopcomputer"
        }
    }

    /// One-sentence explanation for the detail screen.
    var explanation: String {
        switch self {
        case .phoneOnly:
            return "This recording exists only on this iPhone. Until it uploads, deleting it here deletes it everywhere."
        case .syncService:
            return "The audio reached your sync server. Your PC has not reported picking it up yet."
        case .computer:
            return "Your PC has this capture. Transcription, review, and every edit happen there."
        }
    }
}

// MARK: - Lifecycle phase

/// The single lifecycle position shown to the user: Saved → Uploading
/// → Uploaded → Processing → Ready for review → Done,
/// or one of the two failures. Failures are kept apart on purpose: an upload
/// failure is this phone's to retry, a processing failure is not.
enum CaptureLifecyclePhase: Equatable {
    case recording
    case savedOnPhone
    case uploading
    case uploaded
    case processing
    case readyForReview
    case done
    case uploadFailed
    case processingFailed
    case cancelled

    /// Monotonic position used to fuse the local and remote views of a capture:
    /// whichever side is further along wins. Failures rank with the stage they
    /// failed in, so a failure never reads as backwards progress.
    var progressRank: Int {
        switch self {
        case .recording, .cancelled: return 0
        case .savedOnPhone, .uploadFailed: return 1
        case .uploading: return 2
        case .uploaded: return 3
        case .processing, .processingFailed: return 4
        case .readyForReview: return 5
        case .done: return 6
        }
    }

    var label: String {
        switch self {
        case .recording: return "Recording"
        case .savedOnPhone: return "Saved"
        case .uploading: return "Uploading"
        case .uploaded: return "Uploaded"
        case .processing: return "Processing"
        case .readyForReview: return "Ready for review"
        case .done: return "Done"
        case .uploadFailed: return "Upload failed"
        case .processingFailed: return "Processing failed"
        case .cancelled: return "Cancelled"
        }
    }

    /// Paired with `label` everywhere so status never depends on colour alone.
    var symbolName: String {
        switch self {
        case .recording: return "record.circle"
        case .savedOnPhone: return "iphone"
        case .uploading: return "arrow.up.circle"
        case .uploaded: return "checkmark.icloud"
        case .processing: return "gearshape.2"
        case .readyForReview: return "tray.full"
        case .done: return "checkmark.seal.fill"
        case .uploadFailed, .processingFailed: return "exclamationmark.triangle.fill"
        case .cancelled: return "xmark.circle"
        }
    }

    var tint: Color {
        switch self {
        case .recording: return .orange
        case .savedOnPhone, .cancelled: return .gray
        case .uploading, .uploaded: return .blue
        case .processing: return .indigo
        case .readyForReview, .done: return .green
        case .uploadFailed, .processingFailed: return .red
        }
    }

    var isFailure: Bool {
        self == .uploadFailed || self == .processingFailed
    }

    /// True once Windows has finished with the capture one way or another —
    /// the states that mean "actually safe", as opposed to merely sent.
    var isReviewable: Bool {
        self == .readyForReview || self == .done
    }
}

// MARK: - Fused summary

/// Everything the History screens need to describe one capture's lifecycle.
/// Built fresh from the two stores on every reload; it stores no references.
struct CaptureLifecycleSummary {
    let phase: CaptureLifecyclePhase
    let scope: CaptureStorageScope
    /// Finer-grained Windows queue stage, humanized; nil when it would only
    /// repeat `phase.label`.
    let stageDetail: String?
    /// User-facing failure text: the local upload error for `.uploadFailed`,
    /// the Windows-authored reason for `.processingFailed`. Never contains
    /// transcript content, paths, or credentials (§14.5) — the publisher
    /// guarantees that, and nothing here is ever logged.
    let failureReason: String?
    /// Windows' own word on whether a transcript exists, which can be true even
    /// when this status carried no excerpt.
    let transcriptAvailable: Bool
    let transcriptPreview: String?
    let transcriptCharCount: Int?
    let pendingEventCount: Int?
    let approvedEventCount: Int?
    /// When Windows produced the status this summary reflects.
    let remoteUpdatedAt: Date?

    init(record: CaptureRecord, status: CaptureStatusRecord?) {
        let local = Self.localPhase(for: record.state)
        let remote = status.map { Self.remotePhase(for: $0.status) }
        let resolved = Self.resolve(local: local, remote: remote)
        self.phase = resolved

        // Windows only authors a status for a capture it actually received, so
        // any remote phase from `received` onward proves the bytes left this
        // phone. Without one, reaching `.uploaded` locally only proves the
        // sync service has them.
        let receivedRank = CaptureLifecyclePhase.uploaded.progressRank
        if let remote, remote.progressRank >= receivedRank {
            self.scope = .computer
        } else if resolved.progressRank >= receivedRank {
            self.scope = .syncService
        } else {
            self.scope = .phoneOnly
        }

        self.stageDetail = Self.stageDescription(status?.processingStage)
        switch resolved {
        case .uploadFailed: self.failureReason = record.lastError
        case .processingFailed: self.failureReason = status?.failureReason
        default: self.failureReason = nil
        }
        self.transcriptAvailable = status?.transcriptAvailable ?? false
        self.transcriptPreview = status?.transcriptPreview
        self.transcriptCharCount = status?.transcriptCharCount
        self.pendingEventCount = status?.pendingEventCount
        self.approvedEventCount = status?.approvedEventCount
        self.remoteUpdatedAt = status?.updatedAt
    }

    // MARK: Derived text

    var label: String { phase.label }
    var symbolName: String { phase.symbolName }
    var tint: Color { phase.tint }

    /// True when Windows reports a transcript exists, even if it sent no
    /// preview text with this status.
    var hasTranscript: Bool {
        if transcriptAvailable { return true }
        if let transcriptPreview, !transcriptPreview.isEmpty { return true }
        return (transcriptCharCount ?? 0) > 0
    }

    /// Characters of the full transcript the preview does not include. Measured
    /// in UTF-16 units because `transcriptCharCount` is a .NET char count.
    var transcriptRemainingCharacters: Int? {
        guard let transcriptCharCount, let transcriptPreview else { return nil }
        return max(transcriptCharCount - transcriptPreview.utf16.count, 0)
    }

    var pendingEventsDescription: String? {
        guard let pendingEventCount else { return nil }
        return pendingEventCount == 1
            ? "1 event waiting for review"
            : "\(pendingEventCount) events waiting for review"
    }

    var approvedEventsDescription: String? {
        guard let approvedEventCount else { return nil }
        return approvedEventCount == 1
            ? "1 event on your timeline"
            : "\(approvedEventCount) events on your timeline"
    }

    /// The one extra line worth showing under a history row: a failure if there
    /// is one, otherwise the review counts that make "safe" visible, otherwise
    /// what the PC is currently doing.
    var rowDetail: String? {
        if let failureReason, !failureReason.isEmpty { return failureReason }
        switch phase {
        case .readyForReview: return pendingEventsDescription
        case .done: return approvedEventsDescription ?? "Finished on your PC"
        default: return stageDetail
        }
    }

    /// Spoken form for the status chip, so VoiceOver gets the whole story
    /// (phase, what the PC is doing, and where the recording lives) instead of
    /// a bare word.
    var accessibilityLabel: String {
        var parts = ["Status: \(phase.label)"]
        if let stageDetail { parts.append(stageDetail) }
        parts.append(scope.label)
        if let failureReason, !failureReason.isEmpty { parts.append(failureReason) }
        return parts.joined(separator: ". ")
    }

    // MARK: Mapping

    private static func localPhase(for state: CaptureLifecycleState) -> CaptureLifecyclePhase {
        switch state {
        case .recording: return .recording
        case .savedLocal, .queuedUpload: return .savedOnPhone
        case .uploading: return .uploading
        case .uploaded: return .uploaded
        case .processingRemote: return .processing
        case .reviewReady: return .readyForReview
        case .completed: return .done
        case .failedRecoverable: return .uploadFailed
        case .cancelled: return .cancelled
        }
    }

    /// Maps a `SyncCaptureStatus` wire value. An unknown value (a newer Windows
    /// build) is treated as "the PC has it and is working on it" rather than
    /// dropped, which keeps the phone honest about where the capture is.
    private static func remotePhase(for status: String) -> CaptureLifecyclePhase {
        switch status {
        case SyncCaptureStatus.localOnly: return .savedOnPhone
        case SyncCaptureStatus.uploading: return .uploading
        case SyncCaptureStatus.received: return .uploaded
        case SyncCaptureStatus.processing: return .processing
        case SyncCaptureStatus.reviewReady: return .readyForReview
        case SyncCaptureStatus.completed: return .done
        case SyncCaptureStatus.failed: return .processingFailed
        default: return .processing
        }
    }

    private static func resolve(
        local: CaptureLifecyclePhase,
        remote: CaptureLifecyclePhase?
    ) -> CaptureLifecyclePhase {
        // A live or abandoned recording is purely a local affair.
        if local == .recording || local == .cancelled { return local }
        guard let remote else { return local }
        // A processing failure is always the headline: it is not something
        // re-uploading from this phone can fix.
        if remote == .processingFailed { return .processingFailed }
        // From receipt onward Windows is the authority, and its statuses are
        // applied in cursor order, so its latest word wins outright — including
        // over a local `.failedRecoverable` left behind when an upload actually
        // landed but its response was lost, and over the coarser local mirror
        // the status applier writes into `CaptureRecord.state`.
        if remote.progressRank >= CaptureLifecyclePhase.uploaded.progressRank { return remote }
        // `local_only` / `uploading` only echo what this phone already knows.
        return remote.progressRank > local.progressRank ? remote : local
    }

    /// Humanizes a `QueueProcessingStage` value (windows-native
    /// MemoryTimeline.Data/Models/RecordingQueue.cs). Stages whose meaning the
    /// phase chip already carries return nil so the UI does not say it twice.
    private static func stageDescription(_ stage: String?) -> String? {
        guard let stage, !stage.isEmpty else { return nil }
        switch stage {
        case "pending_download": return "Waiting to download on your PC"
        case "verifying": return "Checking the audio"
        case "ready_for_transcription": return "Queued for transcription"
        case "transcribing": return "Transcribing"
        case "extracting": return "Finding events"
        case "review_ready", "completed",
             "failed_retryable", "failed_configuration", "failed_permanent":
            return nil
        default:
            // Forward compatible: show an unrecognized stage rather than hide
            // progress the user can see happening on Windows.
            let spaced = stage.replacingOccurrences(of: "_", with: " ")
            return spaced.prefix(1).uppercased() + String(spaced.dropFirst())
        }
    }
}

// MARK: - Timeline

/// One row of the detail screen's lifecycle timeline.
struct CaptureLifecycleStep: Identifiable {
    enum Progress: Equatable {
        case done
        case current
        case pending
        case failed

        var symbolName: String {
            switch self {
            case .done: return "checkmark.circle.fill"
            case .current: return "circle.inset.filled"
            case .pending: return "circle"
            case .failed: return "exclamationmark.circle.fill"
            }
        }

        var tint: Color {
            switch self {
            case .done: return .green
            case .current: return .accentColor
            case .pending: return .secondary
            case .failed: return .red
            }
        }

        /// Spoken instead of the symbol, which is decorative.
        var spokenState: String {
            switch self {
            case .done: return "Completed"
            case .current: return "In progress"
            case .pending: return "Not started"
            case .failed: return "Failed"
            }
        }
    }

    let id: String
    let title: String
    let detail: String?
    /// Only set where the phone genuinely knows the time: its own transitions,
    /// and the single stage Windows most recently reported.
    let timestamp: Date?
    let progress: Progress

    var accessibilityLabel: String {
        var parts = [title, progress.spokenState]
        if let detail, !detail.isEmpty { parts.append(detail) }
        if let timestamp {
            parts.append(timestamp.formatted(date: .abbreviated, time: .shortened))
        }
        return parts.joined(separator: ". ")
    }
}

extension CaptureLifecycleSummary {
    /// Builds the lifecycle timeline for one capture. Steps the capture has
    /// passed are marked done without a time: Windows reports only its current
    /// stage, so inventing earlier timestamps would be a lie.
    func steps(for record: CaptureRecord) -> [CaptureLifecycleStep] {
        if phase == .cancelled {
            return [CaptureLifecycleStep(
                id: "cancelled",
                title: "Cancelled on this iPhone",
                detail: nil,
                timestamp: record.capturedAt,
                progress: .failed)]
        }

        let saved = CaptureLifecycleStep(
            id: "saved",
            title: "Saved on this iPhone",
            detail: nil,
            timestamp: record.capturedAt,
            progress: phase == .recording ? .current : .done)
        if phase == .recording { return [saved] }

        let rank = phase.progressRank
        let uploadedRank = CaptureLifecyclePhase.uploaded.progressRank
        let processingRank = CaptureLifecyclePhase.processing.progressRank
        let reviewRank = CaptureLifecyclePhase.readyForReview.progressRank

        let uploadProgress: CaptureLifecycleStep.Progress
        switch phase {
        case .uploadFailed: uploadProgress = .failed
        case .uploading: uploadProgress = .current
        default: uploadProgress = rank >= uploadedRank ? .done : .pending
        }

        // Being past receipt proves receipt happened even if no `received`
        // status was ever seen (the phone may have missed that change).
        let receivedProgress: CaptureLifecycleStep.Progress
        if rank > uploadedRank {
            receivedProgress = .done
        } else if scope == .computer {
            receivedProgress = .current
        } else {
            receivedProgress = .pending
        }

        let processingProgress: CaptureLifecycleStep.Progress
        if phase == .processingFailed {
            processingProgress = .failed
        } else if rank > processingRank {
            processingProgress = .done
        } else if rank == processingRank {
            processingProgress = .current
        } else {
            processingProgress = .pending
        }

        return [
            saved,
            CaptureLifecycleStep(
                id: "uploaded",
                title: "Uploaded to your sync server",
                detail: phase == .uploadFailed ? failureReason : nil,
                timestamp: record.uploadedAt,
                progress: uploadProgress),
            CaptureLifecycleStep(
                id: "received",
                title: "Picked up by your PC",
                detail: nil,
                // The PC's receipt time is only known while `received` is still
                // the current status; later statuses overwrite it.
                timestamp: (scope == .computer && rank == uploadedRank) ? remoteUpdatedAt : nil,
                progress: receivedProgress),
            CaptureLifecycleStep(
                id: "processing",
                title: "Transcribed and analyzed on your PC",
                detail: phase == .processingFailed ? failureReason : stageDetail,
                timestamp: rank == processingRank ? remoteUpdatedAt : nil,
                progress: processingProgress),
            CaptureLifecycleStep(
                id: "review",
                title: "Ready for review in Windows",
                detail: pendingEventsDescription,
                timestamp: rank == reviewRank ? remoteUpdatedAt : nil,
                progress: rank > reviewRank ? .done : (rank == reviewRank ? .current : .pending)),
            CaptureLifecycleStep(
                id: "completed",
                title: "Finished on your PC",
                detail: approvedEventsDescription,
                timestamp: phase == .done ? remoteUpdatedAt : nil,
                progress: phase == .done ? .done : .pending)
        ]
    }
}

// MARK: - Shared views

/// Compact capsule describing a capture's fused lifecycle position. Colour is
/// always paired with a symbol and a word (design §15).
@MainActor
struct CaptureLifecycleChip: View {
    let summary: CaptureLifecycleSummary

    var body: some View {
        Label(summary.label, systemImage: summary.symbolName)
            .font(.caption2.weight(.semibold))
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(summary.tint.opacity(0.15), in: Capsule())
            .foregroundStyle(summary.tint)
            .accessibilityLabel(summary.accessibilityLabel)
    }
}

/// Where the recording lives, in words plus a symbol. Rendered next to the chip
/// so "uploaded" is never mistaken for "only on this phone".
@MainActor
struct CaptureScopeLabel: View {
    let scope: CaptureStorageScope

    var body: some View {
        Label(scope.label, systemImage: scope.symbolName)
            .font(.caption)
            .foregroundStyle(scope == .phoneOnly ? Color.orange : Color.secondary)
            .accessibilityLabel(scope.label)
    }
}

/// Vertical lifecycle timeline for the detail screen.
@MainActor
struct CaptureLifecycleTimelineView: View {
    let steps: [CaptureLifecycleStep]

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            ForEach(steps) { step in
                HStack(alignment: .top, spacing: 12) {
                    Image(systemName: step.progress.symbolName)
                        .foregroundStyle(step.progress.tint)
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(step.title)
                            .font(.subheadline.weight(step.progress == .current ? .semibold : .regular))
                            .foregroundStyle(step.progress == .pending ? Color.secondary : Color.primary)
                        if let detail = step.detail, !detail.isEmpty {
                            Text(detail)
                                .font(.footnote)
                                .foregroundStyle(step.progress == .failed ? Color.red : Color.secondary)
                        }
                        if let timestamp = step.timestamp {
                            Text(timestamp.formatted(date: .abbreviated, time: .shortened))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                    Spacer(minLength: 0)
                }
                .accessibilityElement(children: .combine)
                .accessibilityLabel(step.accessibilityLabel)
            }
        }
        .padding(.vertical, 4)
    }
}

/// Incoming half of sync: status changes pulled from Windows. Sits beside
/// `SyncStatusView`, which covers the outgoing uploads. Renders nothing when
/// there is nothing to say.
@MainActor
struct StatusPullRow: View {
    let coordinator: StatusSyncCoordinator

    var body: some View {
        HStack(spacing: 10) {
            if coordinator.isPulling {
                ProgressView()
                    .controlSize(.small)
                Text("Checking your PC for updates\u{2026}")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Spacer(minLength: 0)
            } else if let error = coordinator.lastPullError {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundStyle(.orange)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Couldn't check your PC")
                        .font(.subheadline.weight(.semibold))
                    Text(error)
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
                Spacer(minLength: 8)
                Button("Retry") {
                    Task { await coordinator.pullNow() }
                }
                .buttonStyle(.bordered)
                .controlSize(.small)
                .accessibilityLabel("Retry checking your PC for updates")
            }
        }
        .padding(.vertical, 4)
        .accessibilityElement(children: .contain)
    }
}
