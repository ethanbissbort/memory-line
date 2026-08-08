import SwiftUI

/// The archive, as this Mac has been sent it: events grouped by year, each
/// carrying the era it belongs to.
///
/// **Read-only, and that is the design rather than a shortfall.** Windows owns
/// the archive and is the only editing surface (design §19; the direction note
/// in `TimelineProjectionContracts.cs`). What this renders is a projection —
/// denormalised, published by Windows, applied by `TimelineProjectionApplier`.
/// There is no merge policy here because there is nothing to merge.
///
/// Two rules this view follows that are easy to get wrong:
///
///  - **`displayDate` is rendered, never re-derived.** Windows formats it with
///    the same code its own timeline uses, honouring a date's precision, so a
///    memory the user placed in a summer stays a summer here. Formatting
///    `startDate` locally would invent a precise day for it.
///  - **Grouping uses a UTC calendar** (`TimelineCalendar`), because these dates
///    are calendar dates pinned to UTC midnight, not instants.
///
/// **Not named `TimelineView`**, which every other screen here would suggest
/// (`LibraryView`, `PeopleView`, `ReviewView`). SwiftUI already ships a
/// `TimelineView` — the one that redraws on a schedule — and a same-named type
/// in this module shadows it everywhere, including in `CaptureView`, whose live
/// recording readout is built on it. The resulting error points at the innocent
/// file and says nothing about the collision, so the app's central noun gives
/// way to the framework's here rather than leaving that trap in place.
struct ArchiveTimelineView: View {
    @Environment(MacAppEnvironment.self) private var environment

    @State private var years: [YearGroup] = []
    @State private var eras: [String: EraProjectionPayload] = [:]
    @State private var loadError: String?
    /// Selection is the event id rather than the payload: the id is the identity
    /// the whole system agrees on, and holding a copy of the struct would go
    /// stale the moment a pull republishes that event.
    @State private var selectedEventId: String?
    @State private var showInspector = true

    /// Events sharing a calendar year, newest year first.
    private struct YearGroup: Identifiable {
        let year: Int
        let events: [EventProjectionPayload]
        var id: Int { year }
    }

    var body: some View {
        Group {
            if let loadError {
                ContentUnavailableView {
                    Label("Could not read the timeline", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(loadError)
                } actions: {
                    Button("Try Again") { load() }
                }
            } else if years.isEmpty {
                emptyState
            } else {
                List(selection: $selectedEventId) {
                    ForEach(years) { group in
                        Section(String(group.year)) {
                            ForEach(group.events, id: \.eventId) { event in
                                TimelineEventRow(event: event, era: event.eraId.flatMap { eras[$0] })
                            }
                        }
                    }
                }
                .listStyle(.inset)
            }
        }
        .navigationTitle("Timeline")
        .inspector(isPresented: $showInspector) {
            if let event = selectedEvent {
                TimelineEventDetail(event: event, era: event.eraId.flatMap { eras[$0] })
            } else {
                ContentUnavailableView(
                    "No event selected",
                    systemImage: "sidebar.right",
                    description: Text("Select a memory to see everything the projection carries for it."))
            }
        }
        .toolbar {
            ToolbarItem {
                Button {
                    showInspector.toggle()
                } label: {
                    Label("Details", systemImage: "sidebar.right")
                }
            }
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
        .task { load() }
        // A completed pull may have applied new projections.
        .onChange(of: environment.sync.lastPulledAt) { _, _ in load() }
    }

    /// Empty means one of three quite different things, and saying which is the
    /// difference between a user waiting and a user filing a bug: this Mac is
    /// not paired, or it is paired and has not been sent anything yet, or
    /// Windows genuinely has no events.
    private var emptyState: some View {
        ContentUnavailableView {
            Label("No timeline yet", systemImage: "calendar.day.timeline.left")
        } description: {
            if !environment.isPaired {
                Text("Pair this Mac with your sync server in Settings to see the timeline from your Windows archive.")
            } else if environment.sync.lastPulledAt == nil {
                Text("Paired, but nothing has synced yet. Choose Sync Now to pull from your Windows archive.")
            } else {
                Text("Your Windows archive has not published any events. Memories are added and edited on Windows; this Mac shows a copy.")
            }
        }
    }

    /// The selected event, looked up fresh from what is loaded. Returns nil once
    /// a pull deletes the selected event, which is the correct outcome: the
    /// inspector falls back to its placeholder rather than showing a memory that
    /// no longer exists on Windows.
    private var selectedEvent: EventProjectionPayload? {
        guard let selectedEventId else { return nil }
        for group in years {
            if let match = group.events.first(where: { $0.eventId == selectedEventId }) {
                return match
            }
        }
        return nil
    }

    private func load() {
        do {
            // The whole projection: it is a copy of one person's archive, bounded
            // by a life rather than by a query, so paging it would add a cursor
            // and a scroll-position problem to save an amount of memory that a
            // single capture's audio dwarfs. `events(from:to:)` is the store's
            // only event query and windowing is what it is for, so the unbounded
            // range is spelled out rather than hidden behind a second method that
            // would only ever be called with these two values.
            let events = try environment.projections.events(from: .distantPast, to: .distantFuture)
            eras = Dictionary(
                try environment.projections.allEras().map { ($0.eraId, $0) },
                uniquingKeysWith: { first, _ in first })

            years = Dictionary(grouping: events, by: { TimelineCalendar.year(of: $0.startDate) })
                // Newest first: the store returns ascending because a range query
                // should, but a timeline someone opens is read from the recent
                // end. Within a year the store's order is kept.
                .sorted { $0.key > $1.key }
                .map { YearGroup(year: $0.key, events: $0.value) }
            loadError = nil
        } catch {
            loadError = String(describing: error)
        }
    }
}

// MARK: - Row

private struct TimelineEventRow: View {
    let event: EventProjectionPayload
    let era: EraProjectionPayload?

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            // The era as a colour spine rather than a section: eras overlap in
            // time and an event may have none, so grouping by era would need an
            // arbitrary rule for both cases. A stripe carries the same
            // information without inventing one.
            RoundedRectangle(cornerRadius: 2)
                .fill(eraColor)
                .frame(width: 4)
                .frame(maxHeight: .infinity)

            VStack(alignment: .leading, spacing: 3) {
                Text(event.title)
                    .font(.body)

                HStack(spacing: 6) {
                    // Windows' own precision-honest string. See the type note.
                    Text(event.displayDate ?? "Date unknown")
                        .font(.caption)
                        .foregroundStyle(.secondary)

                    if let category = event.category, !category.isEmpty {
                        Text(category)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }

                    if let era {
                        Text(era.name)
                            .font(.caption)
                            .foregroundStyle(eraColor)
                    }
                }

                if !event.tags.isEmpty {
                    Text(event.tags.joined(separator: " · "))
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                }
            }

            Spacer(minLength: 8)

            // Counts, not content: media lives on Windows and is fetched through
            // the artifact endpoints, and people are ids until the person
            // projection resolves them (§14.5 keeps both off this row).
            VStack(alignment: .trailing, spacing: 3) {
                if event.mediaCount > 0 {
                    Label("\(event.mediaCount)", systemImage: "photo")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                if !event.personIds.isEmpty {
                    Label("\(event.personIds.count)", systemImage: "person.2")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
        }
        .padding(.vertical, 4)
    }

    /// The era's colour, or a neutral when the event has no era or the payload
    /// carried something unparseable. Never a guess at what was meant.
    private var eraColor: Color {
        era.flatMap { Color(projectionHex: $0.colorCode) } ?? .secondary.opacity(0.35)
    }
}

// MARK: - Detail

/// The inspector pane for a selected event. Shows only what the projection
/// carries — there is no "fetch the rest from Windows" call, because a
/// projection is the whole of what crossed.
private struct TimelineEventDetail: View {
    let event: EventProjectionPayload
    let era: EraProjectionPayload?

    var body: some View {
        Form {
            Section {
                Text(event.title)
                    .font(.headline)
                LabeledContent("When", value: event.displayDate ?? "Date unknown")
                if let category = event.category, !category.isEmpty {
                    LabeledContent("Category", value: category)
                }
                if let era {
                    LabeledContent("Era", value: era.name)
                }
            }

            if let description = event.description, !description.isEmpty {
                Section("Description") {
                    // Trimmed by the publisher, not the archive's full text —
                    // say so rather than letting it read as the whole entry.
                    Text(description)
                        .font(.callout)
                }
            }

            if !event.locations.isEmpty {
                Section("Places") {
                    ForEach(event.locations, id: \.self) { Text($0) }
                }
            }

            if !event.tags.isEmpty {
                Section("Tags") {
                    Text(event.tags.joined(separator: ", "))
                }
            }

            Section {
                Text("Memories are added and edited on Windows. This Mac shows a copy.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}
