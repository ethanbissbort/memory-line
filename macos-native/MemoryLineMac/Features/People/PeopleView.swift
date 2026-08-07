import SwiftUI

/// The contact book, as projected from Windows: who appears in the archive and
/// how often.
///
/// Read-only for the same reason the timeline is — Windows owns the people
/// records and is the only editing surface. What this Mac holds is the identity
/// half of each person (name, nickname, relationship, favourite, event count);
/// email, phone, birthday, notes and avatar paths deliberately never leave
/// Windows (§14.5, and the note on `TimelineProjectionPublisher`'s person
/// payload).
struct PeopleView: View {
    @Environment(MacAppEnvironment.self) private var environment

    @State private var people: [PersonProjectionPayload] = []
    @State private var loadError: String?
    @State private var search = ""
    @State private var sort: SortOption = .events

    enum SortOption: String, CaseIterable, Identifiable {
        case events = "Most events"
        case name = "Name"

        var id: String { rawValue }
    }

    var body: some View {
        Group {
            if let loadError {
                ContentUnavailableView {
                    Label("Could not read the people list", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(loadError)
                } actions: {
                    Button("Try Again") { load() }
                }
            } else if people.isEmpty {
                ContentUnavailableView {
                    Label("No people yet", systemImage: "person.2")
                } description: {
                    Text(environment.isPaired
                         ? "People from your Windows archive appear here once they sync."
                         : "Pair this Mac with your sync server in Settings to see the people in your archive.")
                }
            } else if visible.isEmpty {
                ContentUnavailableView.search(text: search)
            } else {
                List(visible, id: \.personId) { person in
                    PersonRow(person: person)
                }
                .listStyle(.inset)
            }
        }
        .navigationTitle("People")
        .searchable(text: $search, prompt: "Search people")
        .toolbar {
            ToolbarItem {
                Picker("Sort", selection: $sort) {
                    ForEach(SortOption.allCases) { Text($0.rawValue).tag($0) }
                }
                .pickerStyle(.menu)
            }
        }
        .task { load() }
        .onChange(of: environment.sync.lastPulledAt) { _, _ in load() }
    }

    /// Search and sort applied over the loaded rows.
    ///
    /// Favourites float to the top of either sort, matching the Windows hub's
    /// `favoritesFirst` default: a favourite is a user's own statement about
    /// which people matter, so it outranks whatever the secondary key says.
    private var visible: [PersonProjectionPayload] {
        let term = search.trimmingCharacters(in: .whitespaces)
        let matched = term.isEmpty ? people : people.filter { person in
            person.name.localizedCaseInsensitiveContains(term)
                || person.nickname?.localizedCaseInsensitiveContains(term) == true
                || person.relationship?.localizedCaseInsensitiveContains(term) == true
        }

        return matched.sorted { left, right in
            if left.isFavorite != right.isFavorite { return left.isFavorite }
            switch sort {
            case .events:
                if left.eventCount != right.eventCount { return left.eventCount > right.eventCount }
                return left.name.localizedCompare(right.name) == .orderedAscending
            case .name:
                return left.name.localizedCompare(right.name) == .orderedAscending
            }
        }
    }

    private func load() {
        do {
            // `allPeople()` deliberately includes merge tombstones, because the
            // same table also resolves the person ids an event carries and those
            // keep pointing at merged-away ids on purpose. A hub is the other
            // job, and it filters them: showing both halves of a merge is
            // showing a duplicate the user already told Windows to collapse.
            people = try environment.projections.allPeople().filter { $0.mergedIntoId == nil }
            loadError = nil
        } catch {
            loadError = String(describing: error)
        }
    }
}

private struct PersonRow: View {
    let person: PersonProjectionPayload

    var body: some View {
        HStack(spacing: 12) {
            // Initials rather than an avatar: the photo is a file path on
            // Windows, and a path is exactly the kind of thing §14.5 keeps off
            // the wire, so no companion has the image to show.
            Text(initials)
                .font(.caption.weight(.semibold))
                .foregroundStyle(.secondary)
                .frame(width: 32, height: 32)
                .background(Circle().fill(.quaternary))

            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 4) {
                    Text(person.name)
                    if person.isFavorite {
                        Image(systemName: "star.fill")
                            .font(.caption2)
                            .foregroundStyle(.yellow)
                    }
                }
                if let subtitle {
                    Text(subtitle)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }

            Spacer()

            Text(person.eventCount == 1 ? "1 memory" : "\(person.eventCount) memories")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(.vertical, 3)
    }

    private var subtitle: String? {
        let parts = [person.nickname, person.relationship].compactMap { value -> String? in
            guard let value, !value.isEmpty else { return nil }
            return value
        }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }

    private var initials: String {
        let letters = person.name
            .split(separator: " ")
            .prefix(2)
            .compactMap { $0.first.map(String.init) }
        return letters.isEmpty ? "?" : letters.joined().uppercased()
    }
}
