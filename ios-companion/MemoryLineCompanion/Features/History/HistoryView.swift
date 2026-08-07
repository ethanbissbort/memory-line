import SwiftUI

/// Capture history: newest first, with per-row type, timestamp, duration, and
/// the fused lifecycle status — what this iPhone knows about the upload plus
/// what Windows reported through `capture_status` sync changes. Rows push a
/// detail view with the full timeline, transcript preview, and playback; swipe
/// to delete asks for confirmation before removing the row AND its audio file.
/// The outgoing (upload) and incoming (status) sync banners sit at the top
/// whenever there is something in flight or failed, and pull-to-refresh asks
/// the service for fresh status.
@MainActor
struct HistoryView: View {
    @Environment(AppEnvironment.self) private var env
    @State private var records: [CaptureRecord] = []
    /// Windows-authored processing status keyed by capture ID. Read-only on the
    /// phone — editing and approval stay on Windows (design §19 Phase 3).
    @State private var statuses: [String: CaptureStatusRecord] = [:]
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
        .onAppear {
            reload()
            // Opening History is exactly when the user wants Windows' latest
            // word. The coordinator drops the kick when a pull is already
            // running, sync is off, or the device is not paired.
            env.statusSync.kick()
        }
        // `uploads` and `statusSync` are @Observable — reading these here
        // re-renders on upload progress and on every status pull, and the
        // reload keeps rows in sync with the database.
        .onChange(of: env.uploads.pendingCount) { reload() }
        .onChange(of: env.uploads.isSyncing) { reload() }
        .onChange(of: env.statusSync.lastPulledAt) { reload() }
        .onChange(of: env.statusSync.isPulling) { reload() }
    }

    private var historyList: some View {
        List {
            if env.uploads.isSyncing || env.uploads.pendingCount > 0 || env.uploads.lastSyncError != nil {
                Section {
                    SyncStatusView(coordinator: env.uploads)
                }
            }
            if env.statusSync.isPulling || env.statusSync.lastPullError != nil {
                Section {
                    StatusPullRow(coordinator: env.statusSync)
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
                        HistoryRow(record: record, summary: summary(for: record))
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
        .refreshable { await refresh() }
        .confirmationDialog(
            "Delete this capture?",
            isPresented: $isDeleteConfirmationPresented,
            titleVisibility: .visible,
            presenting: deletionCandidate
        ) { record in
            Button("Delete Recording", role: .destructive) { delete(record) }
        } message: { record in
            // The warning follows where the audio actually is, not just where
            // this phone got to: only a copy the PC (or at minimum the server)
            // holds survives the deletion.
            switch summary(for: record).scope {
            case .computer:
                Text("This removes the recording and its local data from this iPhone. The copy on your PC is kept.")
            case .syncService:
                Text("This removes the recording and its local data from this iPhone. The copy on your sync server is kept, but your PC has not picked it up yet.")
            case .phoneOnly:
                Text("This recording has not left this iPhone. Deleting removes it permanently.")
            }
        }
    }

    private func summary(for record: CaptureRecord) -> CaptureLifecycleSummary {
        CaptureLifecycleSummary(record: record, status: statuses[record.id])
    }

    /// Pull-to-refresh: ask the service for status changes Windows published,
    /// then re-read both stores.
    private func refresh() async {
        await env.statusSync.pullNow()
        reload()
    }

    private func reload() {
        do {
            records = try env.captures.allCaptures()
            errorMessage = nil
        } catch {
            errorMessage = "Couldn't load captures: \(error.localizedDescription)"
        }
        do {
            // The store returns rows newest-update first; the rows are keyed by
            // capture so the list can look each one up without rescanning.
            statuses = Dictionary(
                try env.statuses.allStatuses().map { ($0.captureId, $0) },
                uniquingKeysWith: { first, _ in first })
        } catch {
            // Status is a read-only projection: a failed read must not hide the
            // captures themselves, so keep the last snapshot and carry on.
            AppLog.ui.error(
                "capture status read failed: \(String(describing: error), privacy: .public)")
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

/// One history row: type icon, timestamp, duration, the fused lifecycle chip,
/// where the recording currently lives, and one line of detail (failure reason,
/// review counts, or what the PC is doing right now).
@MainActor
struct HistoryRow: View {
    let record: CaptureRecord
    let summary: CaptureLifecycleSummary

    @Environment(\.dynamicTypeSize) private var dynamicTypeSize

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: record.type.symbolName)
                .font(.title3)
                .foregroundStyle(.tint)
                .frame(width: 30)
            VStack(alignment: .leading, spacing: 4) {
                Text(record.capturedAt, format: .dateTime.month(.abbreviated).day().year().hour().minute())
                    .font(.subheadline.weight(.medium))
                // At accessibility text sizes the duration and the chip stop
                // fitting side by side; stack them instead of truncating.
                if dynamicTypeSize.isAccessibilitySize {
                    VStack(alignment: .leading, spacing: 6) {
                        durationText
                        CaptureLifecycleChip(summary: summary)
                    }
                } else {
                    HStack(spacing: 8) {
                        durationText
                        CaptureLifecycleChip(summary: summary)
                    }
                }
                CaptureScopeLabel(scope: summary.scope)
                if let detail = summary.rowDetail, !detail.isEmpty {
                    Text(detail)
                        .font(.footnote)
                        .foregroundStyle(summary.phase.isFailure ? Color.red : Color.secondary)
                        .lineLimit(2)
                }
            }
        }
        .padding(.vertical, 2)
        .accessibilityElement(children: .combine)
    }

    private var durationText: some View {
        Text(Duration.seconds(max(record.durationSeconds, 0)),
             format: .time(pattern: .minuteSecond))
            .font(.caption)
            .foregroundStyle(.secondary)
    }
}
