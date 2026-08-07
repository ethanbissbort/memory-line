import SwiftUI
import WidgetKit

// MARK: - Entry / Provider

/// The quick-record button is stateless; the entry carries only the date.
struct QuickRecordEntry: TimelineEntry {
    let date: Date
}

struct QuickRecordProvider: TimelineProvider {
    func placeholder(in context: Context) -> QuickRecordEntry {
        QuickRecordEntry(date: Date())
    }

    func getSnapshot(in context: Context, completion: @escaping (QuickRecordEntry) -> Void) {
        completion(QuickRecordEntry(date: Date()))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<QuickRecordEntry>) -> Void) {
        completion(Timeline(entries: [QuickRecordEntry(date: Date())], policy: .never))
    }
}

// MARK: - View

/// Pure button affordance: tapping anywhere opens `memoryline://record`, which
/// the app treats as toggle-recording (one-touch record from the Lock Screen
/// or Home Screen, design §8.6).
struct QuickRecordWidgetView: View {
    @Environment(\.widgetFamily) private var family

    var body: some View {
        Group {
            if family == .accessoryCircular {
                ZStack {
                    AccessoryWidgetBackground()
                    Image(systemName: "record.circle")
                        .font(.title)
                        .widgetAccentable()
                }
            } else {
                VStack(spacing: 10) {
                    ZStack {
                        Circle()
                            .fill(Color.red)
                        Image(systemName: "mic.fill")
                            .font(.title)
                            .foregroundStyle(Color.white)
                    }
                    .frame(width: 64, height: 64)
                    Text("Record")
                        .font(.headline)
                }
            }
        }
        .containerBackground(.fill.tertiary, for: .widget)
        .widgetURL(WidgetDeepLink.record)
    }
}

// MARK: - Widget

/// One-touch record button for the Lock Screen and Home Screen.
struct QuickRecordWidget: Widget {
    let kind: String = "MemoryLineQuickRecord"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: QuickRecordProvider()) { _ in
            QuickRecordWidgetView()
        }
        .configurationDisplayName("Quick Record")
        .description("One tap starts a Memory Line voice capture.")
        .supportedFamilies([.accessoryCircular, .systemSmall])
    }
}
