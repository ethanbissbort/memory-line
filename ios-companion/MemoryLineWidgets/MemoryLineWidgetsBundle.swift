import SwiftUI
import WidgetKit

/// Widget extension entry point. Both widgets are pure launchers/status views:
/// they read app-group state written by the app's `WidgetStatusPublisher` and
/// deep-link back into the app for every action — no recording or networking
/// happens in this process (design §9.1).
@main
struct MemoryLineWidgetsBundle: WidgetBundle {
    var body: some Widget {
        CaptureStatusWidget()
        QuickRecordWidget()
    }
}
