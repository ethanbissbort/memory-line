import Foundation

/// Locations for capture audio on disk. Directories are created on demand.
///
/// - Recordings live in Application Support (backed up — they are the user's
///   data and may not have reached the sync service yet).
/// - Upload part slices live in Caches (safe for the system to purge; the
///   upload pipeline re-slices from the original recording as needed).
///
/// `CaptureRecord.fileName` stores only the file name, never a path — the app
/// container path changes between installs, so paths are always rebuilt here.
enum AudioStorage {
    /// Application Support/MemoryLine/Recordings, created if missing.
    static func recordingsDirectory() throws -> URL {
        let support = try FileManager.default.url(
            for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
        let directory = support
            .appendingPathComponent("MemoryLine", isDirectory: true)
            .appendingPathComponent("Recordings", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory
    }

    /// Absolute URL for a recording file name from `CaptureRecord.fileName`.
    static func url(forFileName fileName: String) throws -> URL {
        try recordingsDirectory().appendingPathComponent(fileName, isDirectory: false)
    }

    /// Caches/MemoryLine/UploadParts, created if missing.
    static func partsDirectory() throws -> URL {
        let caches = try FileManager.default.url(
            for: .cachesDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
        let directory = caches
            .appendingPathComponent("MemoryLine", isDirectory: true)
            .appendingPathComponent("UploadParts", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory
    }
}
