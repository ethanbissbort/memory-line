import SwiftUI

/// Lists the captures held in the local database, newest first, with the
/// Windows-authored processing status alongside each one.
///
/// This is the walking skeleton: it exercises the whole shared stack —
/// `SQLiteCaptureStore` for the rows, `SQLiteCaptureStatusStore` for the
/// Phase 3 status projection — without needing any macOS-specific capture or
/// upload code to exist yet. Recording on the Mac comes later (port plan §4.1).
struct LibraryView: View {
    @Environment(MacAppEnvironment.self) private var environment

    @State private var captures: [CaptureRecord] = []
    @State private var statuses: [String: CaptureStatusRecord] = [:]
    @State private var loadError: String?

    var body: some View {
        Group {
            if let loadError {
                ContentUnavailableView {
                    Label("Could not read the capture database", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(loadError)
                } actions: {
                    Button("Try Again") { load() }
                }
            } else if captures.isEmpty {
                ContentUnavailableView {
                    Label("No captures yet", systemImage: "waveform")
                } description: {
                    Text(environment.isPaired
                         ? "Captures recorded on a paired device will appear here once they sync."
                         : "Pair this Mac with your sync server in Settings to see captures from your other devices.")
                }
            } else {
                List(captures) { capture in
                    CaptureRow(capture: capture, status: statuses[capture.id])
                }
            }
        }
        .navigationTitle("Library")
        .toolbar {
            ToolbarItem {
                Button {
                    Task {
                        await environment.sync.pullNow()
                        load()
                    }
                } label: {
                    Label("Sync Now", systemImage: "arrow.triangle.2.circlepath")
                }
                .disabled(!environment.sync.canSync || environment.sync.state == .syncing)
                .help(environment.sync.canSync
                      ? "Pull the latest capture status from the sync server"
                      : "Pair this Mac in Settings to sync")
            }
            ToolbarItem {
                Button {
                    load()
                } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
                }
            }
        }
        .task {
            load()
            // Start the loop here rather than at launch so an unpaired Mac
            // never spins a ticker it cannot use; startPeriodicSync replaces
            // any existing one, so re-entering the view is harmless.
            if environment.sync.canSync {
                environment.sync.startPeriodicSync()
            }
        }
        .onChange(of: environment.sync.lastPulledAt) { _, _ in
            // A completed pull may have written new status rows.
            load()
        }
    }

    private func load() {
        do {
            captures = try environment.captures.allCaptures()
            // One query for every status rather than one per capture; rows for
            // captures this Mac has not seen are simply unused.
            statuses = Dictionary(
                try environment.statuses.allStatuses().map { ($0.captureId, $0) },
                uniquingKeysWith: { first, _ in first })
            loadError = nil
        } catch {
            loadError = String(describing: error)
        }
    }
}

private struct CaptureRow: View {
    let capture: CaptureRecord
    let status: CaptureStatusRecord?

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: capture.type.symbolName)
                .font(.title3)
                .foregroundStyle(.secondary)
                .frame(width: 24)

            VStack(alignment: .leading, spacing: 2) {
                Text(capture.titleHint ?? capture.capturedAt.formatted(date: .abbreviated, time: .shortened))
                    .font(.body)
                Text(capture.capturedAt.formatted(date: .abbreviated, time: .shortened))
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Spacer()

            if let status {
                // Prefer the finer-grained Windows queue stage when it sent one.
                Text(status.processingStage ?? status.status)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Text(capture.state.rawValue)
                .font(.caption.monospaced())
                .foregroundStyle(.secondary)
        }
        .padding(.vertical, 4)
    }
}
