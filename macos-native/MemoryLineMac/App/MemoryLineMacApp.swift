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
                // Deliberately here rather than in the environment's init: the
                // composition root builds the object graph, and starting disk
                // repair and network loops is a separate act that belongs where
                // the UI can already show what is happening.
                .task { await environment.start() }
        }
        .commands {
            // Replacing .newItem rather than adding: "New" in a capture app
            // means starting a recording, and leaving the stock File ▸ New
            // alongside it would offer two different meanings of the word.
            CommandGroup(replacing: .newItem) {
                Button("Sync Now") {
                    Task { await environment.sync.pullNow() }
                }
                .keyboardShortcut("r", modifiers: [.command])
                .disabled(!environment.sync.canSync)
            }
        }

        // Quick capture without bringing the main window forward — the reason
        // a Mac companion is useful mid-thought. The label carries the
        // recording state, so the menu bar itself shows whether audio is being
        // captured even when every window is closed.
        MenuBarExtra {
            MenuBarCaptureView(recorder: environment.recorder, settings: environment.settings)
        } label: {
            MenuBarCaptureLabel(recorder: environment.recorder)
        }

        Settings {
            SettingsScene()
                .environment(environment)
                .frame(width: 520)
        }
    }
}
