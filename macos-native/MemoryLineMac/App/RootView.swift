import SwiftUI

/// Top-level navigation. The sidebar sections mirror the Windows app's
/// navigation so the two heads stay conceptually aligned; only Library is
/// implemented today, and the rest render an honest "not built yet" state
/// rather than an empty page that looks broken.
///
/// See `docs/design/MACOS-PORT-PLAN.md` §5 for the order these are being filled
/// in and which ones need a decision first.
struct RootView: View {
    @Environment(MacAppEnvironment.self) private var environment
    @State private var selection: Section? = .library

    enum Section: String, CaseIterable, Identifiable, Hashable {
        case library = "Library"
        case timeline = "Timeline"
        case review = "Review"
        case people = "People"
        case ask = "Ask"

        var id: String { rawValue }

        var symbol: String {
            switch self {
            case .library: return "waveform"
            case .timeline: return "calendar.day.timeline.left"
            case .review: return "checkmark.circle"
            case .people: return "person.2"
            case .ask: return "bubble.left.and.text.bubble.right"
            }
        }

        /// Whether the section has a real implementation yet.
        var isImplemented: Bool { self == .library }
    }

    var body: some View {
        NavigationSplitView {
            List(Section.allCases, selection: $selection) { section in
                NavigationLink(value: section) {
                    Label(section.rawValue, systemImage: section.symbol)
                        .foregroundStyle(section.isImplemented ? .primary : .secondary)
                }
            }
            .navigationSplitViewColumnWidth(min: 180, ideal: 200, max: 260)
        } detail: {
            switch selection {
            case .library, nil:
                LibraryView()
            case .some(let section):
                NotBuiltYetView(section: section)
            }
        }
    }
}

/// Placeholder for a sidebar section that has not been ported yet. Names the
/// section and points at the plan instead of showing a blank pane.
struct NotBuiltYetView: View {
    let section: RootView.Section

    var body: some View {
        ContentUnavailableView {
            Label(section.rawValue, systemImage: section.symbol)
        } description: {
            Text("Not ported yet. This section exists on Windows; see MACOS-PORT-PLAN.md for where it sits in the order of work.")
        }
    }
}
