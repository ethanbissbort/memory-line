import SwiftUI

/// Search across everything the projection carries: events, eras and people.
///
/// **This is keyword matching, not the Windows search.** Windows runs hybrid
/// retrieval — embeddings plus full text — over the whole archive, including
/// transcripts. None of that is here, and none of it can be: a projection
/// carries what a user reads on a timeline, and transcripts and embeddings
/// deliberately stay on Windows (§14.5). What this searches is titles,
/// descriptions, categories, tags, locations, era names and people names — the
/// fields that actually crossed. That is a genuinely smaller thing, so the view
/// says so rather than letting an empty result read as "you have no such
/// memory".
///
/// Matching is substring and diacritic-insensitive rather than token-based:
/// against a few thousand denormalised rows the difference is not measurable,
/// and "hali" finding "Halifax" is what someone typing into a box expects.
struct SearchView: View {
    @Environment(MacAppEnvironment.self) private var environment

    @State private var query = ""
    @State private var events: [EventProjectionPayload] = []
    @State private var eras: [EraProjectionPayload] = []
    @State private var people: [PersonProjectionPayload] = []
    @State private var loadError: String?

    var body: some View {
        Group {
            if let loadError {
                ContentUnavailableView {
                    Label("Could not read the archive copy", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(loadError)
                } actions: {
                    Button("Try Again") { load() }
                }
            } else if trimmedQuery.isEmpty {
                ContentUnavailableView {
                    Label("Search your timeline", systemImage: "magnifyingglass")
                } description: {
                    Text("Matches titles, descriptions, tags, places, eras and people. Recordings and transcripts stay on Windows, so they are not searched here.")
                }
            } else if matchedEvents.isEmpty && matchedEras.isEmpty && matchedPeople.isEmpty {
                ContentUnavailableView {
                    Label("No matches", systemImage: "magnifyingglass")
                } description: {
                    Text("Nothing in the synced copy matches “\(trimmedQuery)”. Windows searches transcripts too — try there if you are looking for something that was said rather than written.")
                }
            } else {
                List {
                    if !matchedEvents.isEmpty {
                        Section("Memories") {
                            ForEach(matchedEvents, id: \.eventId) { event in
                                SearchEventRow(event: event)
                            }
                        }
                    }
                    if !matchedPeople.isEmpty {
                        Section("People") {
                            ForEach(matchedPeople, id: \.personId) { person in
                                LabeledContent(person.name) {
                                    Text(person.eventCount == 1 ? "1 memory" : "\(person.eventCount) memories")
                                        .foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                    if !matchedEras.isEmpty {
                        Section("Eras") {
                            ForEach(matchedEras, id: \.eraId) { era in
                                LabeledContent(era.name) {
                                    Text(era.subtitle ?? "")
                                        .foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                }
                .listStyle(.inset)
            }
        }
        .navigationTitle("Search")
        .searchable(text: $query, prompt: "Search memories, people and eras")
        .task { load() }
        .onChange(of: environment.sync.lastPulledAt) { _, _ in load() }
    }

    private var trimmedQuery: String {
        query.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    // MARK: - Matching

    /// Filtering happens in memory over the loaded rows rather than in SQL.
    ///
    /// Three reasons, in order of weight: tags, people and locations live in
    /// JSON text columns (see the storage note on `TimelineProjectionStore`), so
    /// SQL could only `LIKE` inside them and would match a tag against a
    /// location without knowing it did; the projection is one person's archive,
    /// bounded by a life rather than by a query, so it is already fully loaded;
    /// and `localizedStandardContains` does the diacritic and case folding a
    /// user expects, which SQLite's `LIKE` does not do for non-ASCII.
    private var matchedEvents: [EventProjectionPayload] {
        let term = trimmedQuery
        guard !term.isEmpty else { return [] }
        return events.filter { event in
            event.title.localizedStandardContains(term)
                || event.description?.localizedStandardContains(term) == true
                || event.category?.localizedStandardContains(term) == true
                || event.displayDate?.localizedStandardContains(term) == true
                || event.tags.contains { $0.localizedStandardContains(term) }
                || event.locations.contains { $0.localizedStandardContains(term) }
        }
    }

    private var matchedPeople: [PersonProjectionPayload] {
        let term = trimmedQuery
        guard !term.isEmpty else { return [] }
        // Merge tombstones are excluded here but kept in the store, for the
        // reason `allPeople()` documents: they still have to resolve the person
        // ids old events carry. A search hit on one would offer the user a
        // duplicate they already told Windows to collapse.
        return people.filter { person in
            person.mergedIntoId == nil
                && (person.name.localizedStandardContains(term)
                    || person.nickname?.localizedStandardContains(term) == true
                    || person.relationship?.localizedStandardContains(term) == true)
        }
    }

    private var matchedEras: [EraProjectionPayload] {
        let term = trimmedQuery
        guard !term.isEmpty else { return [] }
        return eras.filter { era in
            era.name.localizedStandardContains(term)
                || era.subtitle?.localizedStandardContains(term) == true
                || era.categoryName?.localizedStandardContains(term) == true
        }
    }

    private func load() {
        do {
            events = try environment.projections.events(from: .distantPast, to: .distantFuture)
            eras = try environment.projections.allEras()
            people = try environment.projections.allPeople()
            loadError = nil
        } catch {
            loadError = String(describing: error)
        }
    }
}

private struct SearchEventRow: View {
    let event: EventProjectionPayload

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(event.title)
            HStack(spacing: 6) {
                // Windows' precision-honest string, as everywhere else.
                Text(event.displayDate ?? "Date unknown")
                if let category = event.category, !category.isEmpty {
                    Text(category)
                }
                if !event.locations.isEmpty {
                    Text(event.locations.joined(separator: ", "))
                }
            }
            .font(.caption)
            .foregroundStyle(.secondary)
        }
        .padding(.vertical, 2)
    }
}
