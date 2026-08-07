import SwiftUI

/// Compact sync pipeline status row embedded at the top of the History screen:
/// a spinner while uploading, the pending-upload count, and the last sync
/// error with a Retry button (which kicks a new upload pass).
@MainActor
struct SyncStatusView: View {
    let coordinator: UploadCoordinator

    var body: some View {
        HStack(spacing: 10) {
            if coordinator.isSyncing {
                ProgressView()
                    .controlSize(.small)
                Text(syncingLabel)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Spacer(minLength: 0)
            } else if let error = coordinator.lastSyncError {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundStyle(.orange)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Sync problem")
                        .font(.subheadline.weight(.semibold))
                    Text(error)
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
                Spacer(minLength: 8)
                retryButton
            } else if coordinator.pendingCount > 0 {
                Image(systemName: "arrow.up.circle")
                    .foregroundStyle(.secondary)
                Text("\(coordinator.pendingCount) waiting to upload")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Spacer(minLength: 8)
                retryButton
            } else {
                Image(systemName: "checkmark.circle")
                    .foregroundStyle(.green)
                Text("All captures synced")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Spacer(minLength: 0)
            }
        }
        .padding(.vertical, 4)
        .animation(.default, value: coordinator.isSyncing)
        .accessibilityElement(children: .combine)
    }

    private var syncingLabel: String {
        coordinator.pendingCount > 0
            ? "Uploading — \(coordinator.pendingCount) remaining"
            : "Uploading…"
    }

    private var retryButton: some View {
        Button("Retry") {
            coordinator.kick()
        }
        .buttonStyle(.bordered)
        .controlSize(.small)
    }
}
