import SwiftUI

/// The extraction review queue: events Windows pulled out of a recording that
/// nobody has approved or rejected yet.
///
/// This is the one screen where a companion is not purely a reader. Approving or
/// rejecting is a *decision*, not an edit — one bit, idempotent, and the same
/// answer twice means the same thing — which is what makes it safe to accept
/// from a device that does not own the archive without opening general
/// multi-writer editing (`TimelineProjectionContracts.cs`,
/// `PendingEventDecisionPayload`). Windows still performs the approval, in one
/// transaction over the event and its tags, people and locations, so the
/// extraction rules stay in exactly one place.
///
/// A verdict is queued to disk before any network call, so closing the lid
/// mid-pass does not lose it, and each item shows where its decision has got to
/// rather than pretending the click was the delivery. See `MacReviewCoordinator`.
struct ReviewView: View {
    @Environment(MacAppEnvironment.self) private var environment

    @State private var pending: [PendingEventProjectionPayload] = []
    @State private var loadError: String?
    @State private var selectedId: String?

    var body: some View {
        Group {
            if let loadError {
                ContentUnavailableView {
                    Label("Could not read the review queue", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(loadError)
                } actions: {
                    Button("Try Again") { load() }
                }
            } else if pending.isEmpty {
                ContentUnavailableView {
                    Label("Nothing to review", systemImage: "checkmark.circle")
                } description: {
                    Text(environment.isPaired
                         ? "Events extracted from your recordings appear here while they wait for review."
                         : "Pair this Mac with your sync server in Settings to see the review queue.")
                }
            } else {
                List(pending, id: \.pendingEventId, selection: $selectedId) { item in
                    PendingEventRow(
                        item: item,
                        decision: environment.reviews.decisions[item.pendingEventId],
                        decide: { verdict in
                            Task { await environment.reviews.decide(
                                pendingEventId: item.pendingEventId, verdict: verdict) }
                        })
                }
                .listStyle(.inset)
            }
        }
        .navigationTitle("Review")
        .safeAreaInset(edge: .bottom) {
            if case .failed(let message) = environment.reviews.state {
                // Undelivered verdicts are not lost, and saying so is the
                // difference between a user waiting and a user deciding twice.
                Label(message, systemImage: "exclamationmark.triangle")
                    .font(.caption)
                    .padding(8)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(.regularMaterial)
            }
        }
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
            }
        }
        .task {
            load()
            environment.reviews.reload()
        }
        .onChange(of: environment.sync.lastPulledAt) { _, _ in
            // A pull may have brought Windows' confirmation that a decision was
            // acted on, which is what retires a delivered row.
            environment.reviews.pruneConfirmed()
            load()
        }
    }

    private func load() {
        do {
            pending = try environment.projections.allPendingEvents()
            loadError = nil
        } catch {
            loadError = String(describing: error)
        }
    }
}

private struct PendingEventRow: View {
    let item: PendingEventProjectionPayload
    let decision: ReviewDecisionRecord?
    let decide: (ReviewVerdict) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(alignment: .firstTextBaseline) {
                Text(item.title)
                    .font(.body)
                Spacer()
                ConfidenceBadge(score: item.confidenceScore)
            }

            HStack(spacing: 6) {
                // Windows' own precision-honest string, never re-derived here —
                // an extraction that only knew a season must not be shown as a
                // precise day just because this screen had a date to format.
                Text(item.displayDate ?? "Date unknown")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                if let category = item.category, !category.isEmpty {
                    Text(category)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                if !item.peopleNames.isEmpty {
                    // Names, not ids: an extraction found these in the audio and
                    // they may not exist in the contact book yet.
                    Text(item.peopleNames.joined(separator: ", "))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }

            if let preview = item.transcriptPreview, !preview.isEmpty {
                // A bounded excerpt the publisher trimmed, never the transcript
                // itself (§14.5). Shown because the reviewer's whole job is
                // judging the extraction against what was actually said.
                Text(preview)
                    .font(.caption)
                    .foregroundStyle(.tertiary)
                    .lineLimit(2)
            }

            if !item.tags.isEmpty {
                Text(item.tags.joined(separator: " · "))
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }

            // Actions or status, never both: once a verdict exists the buttons
            // would only invite a second answer to a question already sent, and
            // the service drops a decision for a review Windows has resolved.
            if let decision {
                DecisionStatus(decision: decision)
            } else {
                HStack(spacing: 8) {
                    Button("Approve") { decide(.approve) }
                        .buttonStyle(.borderedProminent)
                        .controlSize(.small)
                    Button("Reject") { decide(.reject) }
                        .controlSize(.small)
                }
                .padding(.top, 2)
            }
        }
        .padding(.vertical, 4)
    }
}

/// Where a queued verdict has got to. Deliberately explicit about the middle
/// state: a decision that is saved but not yet delivered is the normal case on a
/// laptop, and showing it as though it were done would be a lie the user
/// discovers later on Windows.
private struct DecisionStatus: View {
    let decision: ReviewDecisionRecord

    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: symbol)
            Text(text)
            if let error = decision.lastError, decision.state == .failed {
                Text("— \(error)")
                    .foregroundStyle(.secondary)
            }
        }
        .font(.caption)
        .foregroundStyle(tint)
        .padding(.top, 2)
    }

    private var text: String {
        switch decision.state {
        case .pending: return "\(decision.verdict.pastTense) — waiting to send"
        case .sent: return "\(decision.verdict.pastTense) — waiting for Windows"
        case .failed: return "\(decision.verdict.pastTense) — could not be sent"
        }
    }

    private var symbol: String {
        switch decision.state {
        case .pending: return "clock"
        case .sent: return "paperplane"
        case .failed: return "exclamationmark.triangle"
        }
    }

    private var tint: Color {
        switch decision.state {
        case .pending, .sent: return .secondary
        case .failed: return .orange
        }
    }
}

/// Extraction confidence, so a reviewer can triage the queue rather than reading
/// it in order. The thresholds are presentation only — nothing acts on them.
private struct ConfidenceBadge: View {
    let score: Double

    var body: some View {
        Text(score.formatted(.percent.precision(.fractionLength(0))))
            .font(.caption2.monospacedDigit())
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(Capsule().fill(tint.opacity(0.15)))
            .foregroundStyle(tint)
    }

    private var tint: Color {
        switch score {
        case ..<0.5: return .orange
        case ..<0.8: return .yellow
        default: return .green
        }
    }
}
