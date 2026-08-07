import SwiftUI

/// Standard macOS Settings window. Pairing is the only thing here today, and it
/// runs entirely on the shared `SyncAPIClient` — the same register/revoke calls
/// the iOS companion makes, against the same self-hosted service.
struct SettingsScene: View {
    var body: some View {
        TabView {
            PairingSettingsView()
                .tabItem { Label("Sync", systemImage: "arrow.triangle.2.circlepath") }
        }
        .padding(20)
    }
}

struct PairingSettingsView: View {
    @Environment(MacAppEnvironment.self) private var environment

    @State private var serverURL = ""
    @State private var pairingCode = ""
    @State private var isWorking = false
    @State private var message: Message?

    private struct Message: Identifiable {
        let id = UUID()
        let text: String
        let isError: Bool
    }

    var body: some View {
        Form {
            if environment.isPaired {
                Section("Paired") {
                    LabeledContent("Server", value: environment.serverURL ?? "unknown")
                    LabeledContent("This Mac", value: MacAppEnvironment.deviceDisplayName)
                    Button("Unpair", role: .destructive) { unpair() }
                        .disabled(isWorking)
                }
            } else {
                Section("Pair with your sync server") {
                    TextField("Server URL", text: $serverURL, prompt: Text("https://memoryline.example.com"))
                        .textContentType(.URL)
                    // The pairing code is short-lived and single-use, but it is
                    // still a credential — never render it in the clear.
                    SecureField("Pairing code", text: $pairingCode)
                    Button("Pair") { pair() }
                        .disabled(isWorking || serverURL.isEmpty || pairingCode.isEmpty)
                }
                Section {
                    Text("Generate a pairing code in the Windows app under Settings → Sync.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }

            if let message {
                Text(message.text)
                    .font(.caption)
                    .foregroundStyle(message.isError ? .red : .secondary)
            }
        }
        .formStyle(.grouped)
        .overlay {
            if isWorking { ProgressView().controlSize(.small) }
        }
    }

    private func pair() {
        isWorking = true
        message = nil
        Task {
            defer { isWorking = false }
            do {
                try await environment.pair(serverURL: serverURL, pairingCode: pairingCode)
                // Clear the code as soon as it has been spent; it is single-use
                // and there is no reason to keep it in memory or on screen.
                pairingCode = ""
                message = Message(text: "Paired.", isError: false)
            } catch let error as SyncAPIError {
                message = Message(text: error.message, isError: true)
            } catch {
                message = Message(text: error.localizedDescription, isError: true)
            }
        }
    }

    private func unpair() {
        isWorking = true
        message = nil
        Task {
            defer { isWorking = false }
            await environment.unpair()
            message = Message(text: "Unpaired. Local captures were kept.", isError: false)
        }
    }
}
