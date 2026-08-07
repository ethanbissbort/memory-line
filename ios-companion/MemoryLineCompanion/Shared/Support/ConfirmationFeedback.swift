import AVFAudio
import UIKit

/// Capture-saved confirmation (design §15.3): a short success haptic, plus an
/// optional single spoken word — terse by design, no status narration, so it
/// works while driving.
@MainActor
enum ConfirmationFeedback {
    /// Held statically so the synthesizer is not deallocated mid-utterance
    /// (a released `AVSpeechSynthesizer` cuts speech off).
    private static let synthesizer = AVSpeechSynthesizer()

    /// Confirms a capture was durably saved. `spoken` follows the
    /// `AppSettingsKey.spokenConfirmation` setting.
    static func captureSaved(spoken: Bool) {
        let generator = UINotificationFeedbackGenerator()
        generator.notificationOccurred(.success)
        guard spoken else { return }
        // A rapid double-capture should not queue narration; latest wins.
        if synthesizer.isSpeaking {
            _ = synthesizer.stopSpeaking(at: .immediate)
        }
        let utterance = AVSpeechUtterance(string: String(localized: "Saved"))
        synthesizer.speak(utterance)
    }
}
