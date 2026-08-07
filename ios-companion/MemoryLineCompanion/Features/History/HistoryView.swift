import SwiftUI

/// Capture history: newest first, with per-row type, timestamp, duration, and
/// lifecycle status. Rows push a detail view with playback and retry; swipe to
/// delete asks for confirmation before removing the row AND its audio file.
/// The sync pipeline status banner sits at the top whenever there is
/// something in flight or failed.
@MainActor
struct HistoryView: View {
    @Environment(AppEnvironment.self) private var env
    @State private var records: [CaptureRecord] = []
    @State private var errorMessage: String?
    @State private var deletionCandidate: CaptureRecord?
    @State private var isDeleteConfirmationPresented = false

    var body: some View {
        NavigationStack {
            Group {
                if records.isEmpty && errorMessage == nil {
                    ContentUnavailableView(
                        "No captures yet",
                        systemImage: "waveform",
                        description: Text("Recordings you make appear here, newest first."))
                } else {
                    historyList
                }
            }
            .navigationTitle("History")
        }
        .onAppear(perform: reload)
        // `uploads` is @Observable — reading these here re-renders on upload
        // progress, and the reload keeps rows in sync with the database.
        .onChange(of: env.uploads.pendingCount) { reload() }
        .onChange(of: env.uploads.isSyncing) { reload() }
    }

    private var historyList: some View {
        List {
            if env.uploads.isSyncing || env.uploads.pendingCount > 0 || env.uploads.lastSyncError != nil {
                Section {
                    SyncStatusView(coordinator: env.uploads)
                }
            }
            if let errorMessage {
                Section {
                    Label(errorMessage, systemImage: "exclamationmark.triangle.fill")
                        .font(.footnote)
                        .foregroundStyle(.red)
                }
            }
            Section {
                ForEach(records) { record in
                    NavigationLink {
                        HistoryDetailView(record: record)
                    } label: {
                        HistoryRow(record: record)
                    }
                    .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                        // Active recordings can't be deleted from here; the
                        // Capture screen owns cancelling those.
                        if record.state != .recording {
                            Button {
                                deletionCandidate = record
                                isDeleteConfirmationPresented = true
                            } label: {
                                Label("Delete", systemImage: "trash")
                            }
                            .tint(.red)
                        }
                    }
                }
            }
        }
        .refreshable { await reload() }
        .confirmationDialog(
            "Delete this capture?",
            isPresented: $isDeleteConfirmationPresented,
            titleVisibility: .visible,
            presenting: deletionCandidate
        ) { record in
            Button("Delete Recording", role: .destructive) { delete(record) }
        } message: { record in
            if record.state.isDoneUploading {
                Text("This removes the recording and its local data from this iPhone. The copy already uploaded to your computer is kept.")
            } else {
                Text("This recording has not reached your computer yet. Deleting removes it permanently.")
            }
        }
    }

    private func reload() {
        do {
            records = try env.captures.allCaptures()
            errorMessage = nil
        } catch {
            errorMessage = "Couldn't load captures: \(error.localizedDescription)"
        }
    }

    /// Deletes the database row first (so the upload pipeline can never retry
    /// a capture whose file is gone), then the audio file. A failed file
    /// removal leaves an orphaned file, which is harmless.
    private func delete(_ record: CaptureRecord) {
        do {
            try env.captures.delete(id: record.id)
        } catch {
            errorMessage = "Couldn't delete the capture: \(error.localizedDescription)"
            return
        }
        if let fileURL = try? AudioStorage.url(forFileName: record.fileName) {
            try? FileManager.default.removeItem(at: fileURL)
        }
        reload()
        // Rescan so the coordinator's pending count reflects the deletion.
        env.uploads.kick()
    }
}

/// One history row: type icon, timestamp, duration, status chip, and the
/// failure reason for failed captures.
@MainActor
struct HistoryRow: View {
    let record: CaptureRecord

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: record.type.symbolName)
                .font(.title3)
                .foregroundStyle(.tint)
                .frame(width: 30)
            VStack(alignment: .leading, spacing: 4) {
                Text(record.capturedAt, format: .dateTime.month(.abbreviated).day().year().hour().minute())
                    .font(.subheadline.weight(.medium))
                HStack(spacing: 8) {
                    Text(Duration.seconds(max(record.durationSeconds, 0)),
                         format: .time(pattern: .minuteSecond))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    CaptureStatusChip(state: record.state)
                }
                if record.state == .failedRecoverable, let reason = record.lastError {
                    Text(reason)
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
            }
        }
        .padding(.vertical, 2)
        .accessibilityElement(children: .combine)
    }
}

/// Compact capsule describing a capture's lifecycle state.
@MainActor
struct CaptureStatusChip: View {
    let state: CaptureLifecycleState

    var body: some View {
        Text(label)
            .font(.caption2.weight(.semibold))
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(color.opacity(0.15), in: Capsule())
            .foregroundStyle(color)
    }

    private var label: String {
        switch state {
        case .recording: return "Recording"
        case .savedLocal: return "Saved locally"
        case .queuedUpload: return "Waiting to upload"
        case .uploading: return "Uploading"
        case .uploaded: return "Uploaded"
        case .processingRemote: return "Processing on PC"
        case .reviewReady: return "Ready for review"
        case .completed: return "Completed"
        case .failedRecoverable: return "Failed"
        case .cancelled: return "Cancelled"
        }
    }

    private var color: Color {
        switch state {
        case .recording: return .orange
        case .savedLocal, .queuedUpload, .cancelled: return .gray
        case .uploading: return .blue
        case .uploaded, .processingRemote, .reviewReady, .completed: return .green
        case .failedRecoverable: return .red
        }
    }
}
