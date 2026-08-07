import SwiftUI
import WidgetKit

// MARK: - Entry

/// Snapshot of the capture pipeline as last published by the app process
/// (`WidgetStatusPublisher` writes app-group defaults and reloads timelines
/// on every state change).
struct CaptureStatusEntry: TimelineEntry {
    let date: Date
    /// "idle" | "recording" | "paused" (see `WidgetSharedState`).
    let recordingState: String
    let pendingUploads: Int
    let lastCaptureAt: Date?

    var isRecording: Bool { recordingState == "recording" }
    var isPaused: Bool { recordingState == "paused" }

    static let placeholder = CaptureStatusEntry(
        date: Date(),
        recordingState: "idle",
        pendingUploads: 0,
        lastCaptureAt: nil
    )

    /// Sample shown in the widget gallery.
    static let previewSample = CaptureStatusEntry(
        date: Date(),
        recordingState: "idle",
        pendingUploads: 2,
        lastCaptureAt: Date().addingTimeInterval(-15 * 60)
    )
}

// MARK: - Provider

/// Reads current state from the shared app-group `UserDefaults`. The app
/// reloads widget timelines whenever the state changes, so a single entry
/// with `.never` policy is sufficient.
struct CaptureStatusProvider: TimelineProvider {
    func placeholder(in context: Context) -> CaptureStatusEntry {
        .placeholder
    }

    func getSnapshot(in context: Context, completion: @escaping (CaptureStatusEntry) -> Void) {
        completion(context.isPreview ? .previewSample : Self.currentEntry())
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<CaptureStatusEntry>) -> Void) {
        completion(Timeline(entries: [Self.currentEntry()], policy: .never))
    }

    /// Builds an entry from the app-group defaults; falls back to the idle
    /// placeholder when the group is unavailable (e.g. entitlement mismatch).
    static func currentEntry() -> CaptureStatusEntry {
        guard let defaults = UserDefaults(suiteName: WidgetSharedState.appGroupId) else {
            return .placeholder
        }
        let state = defaults.string(forKey: WidgetSharedState.recordingStateKey) ?? "idle"
        let pending = defaults.integer(forKey: WidgetSharedState.pendingUploadsKey)
        let epoch = defaults.double(forKey: WidgetSharedState.lastCaptureAtKey)
        return CaptureStatusEntry(
            date: Date(),
            recordingState: state,
            pendingUploads: max(0, pending),
            lastCaptureAt: epoch > 0 ? Date(timeIntervalSince1970: epoch) : nil
        )
    }
}

// MARK: - Views

/// Family switcher. The whole body is a single `memoryline://record` tap
/// target: tapping anywhere toggles recording via the app's deep link.
struct CaptureStatusWidgetView: View {
    @Environment(\.widgetFamily) private var family
    let entry: CaptureStatusEntry

    var body: some View {
        Group {
            switch family {
            case .accessoryCircular:
                CaptureStatusCircularView(entry: entry)
            case .accessoryRectangular:
                CaptureStatusRectangularView(entry: entry)
            default:
                CaptureStatusSmallView(entry: entry)
            }
        }
        .containerBackground(.fill.tertiary, for: .widget)
        .widgetURL(WidgetDeepLink.record)
    }
}

/// Lock Screen circular: mic symbol, red while recording, pending-count badge
/// when idle with uploads waiting.
struct CaptureStatusCircularView: View {
    let entry: CaptureStatusEntry

    var body: some View {
        ZStack {
            AccessoryWidgetBackground()
            Image(systemName: symbolName)
                .font(.title2)
                .foregroundStyle(entry.isRecording ? Color.red : Color.primary)
                .widgetAccentable()
            if !entry.isRecording && !entry.isPaused && entry.pendingUploads > 0 {
                Text(badgeText)
                    .font(.system(size: 11, weight: .bold, design: .rounded))
                    .padding(3)
                    .background(.quaternary, in: Circle())
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomTrailing)
            }
        }
    }

    private var symbolName: String {
        if entry.isRecording { return "mic.fill" }
        if entry.isPaused { return "pause.fill" }
        return "mic"
    }

    private var badgeText: String {
        entry.pendingUploads > 9 ? "9+" : "\(entry.pendingUploads)"
    }
}

/// Lock Screen rectangular: app name, recording state, pending-upload line.
struct CaptureStatusRectangularView: View {
    let entry: CaptureStatusEntry

    var body: some View {
        VStack(alignment: .leading, spacing: 1) {
            HStack(spacing: 4) {
                Image(systemName: "waveform")
                    .widgetAccentable()
                Text("Memory Line")
                    .font(.headline)
            }
            Text(stateLine)
                .font(.subheadline)
            Text(pendingLine)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var stateLine: String {
        if entry.isRecording { return "Recording…" }
        if entry.isPaused { return "Paused" }
        return "Tap to record"
    }

    private var pendingLine: String {
        switch entry.pendingUploads {
        case 0: return "All synced"
        case 1: return "1 upload waiting"
        default: return "\(entry.pendingUploads) uploads waiting"
        }
    }
}

/// Home Screen small: bigger version of the same status.
struct CaptureStatusSmallView: View {
    let entry: CaptureStatusEntry

    var body: some View {
        VStack(spacing: 8) {
            ZStack {
                Circle()
                    .fill(entry.isRecording ? Color.red : Color.red.opacity(0.15))
                    .frame(width: 56, height: 56)
                Image(systemName: symbolName)
                    .font(.title2)
                    .foregroundStyle(entry.isRecording ? Color.white : Color.red)
            }
            Text(stateLine)
                .font(.headline)
            detailLine
                .font(.caption)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
    }

    private var symbolName: String {
        entry.isPaused ? "pause.fill" : "mic.fill"
    }

    private var stateLine: String {
        if entry.isRecording { return "Recording…" }
        if entry.isPaused { return "Paused" }
        return "Record"
    }

    @ViewBuilder
    private var detailLine: some View {
        if entry.pendingUploads > 0 {
            Label(pendingText, systemImage: "arrow.up.circle")
        } else if let last = entry.lastCaptureAt {
            Text("Saved \(Text(last, style: .relative)) ago")
        } else {
            Text("All synced")
        }
    }

    private var pendingText: String {
        entry.pendingUploads == 1 ? "1 to sync" : "\(entry.pendingUploads) to sync"
    }
}

// MARK: - Widget

/// Recording state + sync status at a glance; a tap toggles recording
/// through the app (design §9.1).
struct CaptureStatusWidget: Widget {
    let kind: String = "MemoryLineCaptureStatus"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: CaptureStatusProvider()) { entry in
            CaptureStatusWidgetView(entry: entry)
        }
        .configurationDisplayName("Capture Status")
        .description("Recording state and pending uploads. Tap to start or stop recording.")
        .supportedFamilies([.accessoryCircular, .accessoryRectangular, .systemSmall])
    }
}
