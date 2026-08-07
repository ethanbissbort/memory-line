import SwiftUI

/// Entry point for the macOS app.
///
/// Window shape is the first real divergence from the iOS companion: the phone
/// is a single-purpose capture device, whereas the Mac is the browsing and
/// review surface, so this uses a resizable main window with a sidebar plus a
/// standard Settings scene rather than a tab bar.
@main
struct MemoryLineMacApp: App {
    @State private var environment = MacAppEnvironment.bootstrap()

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(environment)
                .frame(minWidth: 900, minHeight: 560)
        }
        .commands {
            // Placeholder for the menu-bar commands the port plan calls for
            // (New Capture, Sync Now, Go to Today). Added with the features
            // they invoke rather than as dead menu items.
            CommandGroup(replacing: .newItem) {}
        }

        Settings {
            SettingsScene()
                .environment(environment)
                .frame(width: 520)
        }
    }
}
