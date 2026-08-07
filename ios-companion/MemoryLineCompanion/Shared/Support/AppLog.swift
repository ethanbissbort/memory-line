import os

/// Logger factory for the app, one `Logger` per category under the single
/// subsystem "com.memoryline.companion".
///
/// Policy (design §14.5): NEVER log audio content, transcripts, notes, tokens,
/// or the pairing code. Capture IDs, states, byte counts, and status codes are
/// fine. Interpolate anything dynamic with an explicit privacy annotation.
enum AppLog {
    static let subsystem = "com.memoryline.companion"

    /// Makes a logger for an ad-hoc category; prefer the shared ones below.
    static func logger(category: String) -> Logger {
        Logger(subsystem: subsystem, category: category)
    }

    static let persistence = logger(category: "persistence")
    static let security = logger(category: "security")
    static let audio = logger(category: "audio")
    static let sync = logger(category: "sync")
    static let background = logger(category: "background")
    static let widget = logger(category: "widget")
    static let ui = logger(category: "ui")
}
