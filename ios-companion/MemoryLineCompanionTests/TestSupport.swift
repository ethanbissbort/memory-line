import Foundation
@testable import MemoryLineCompanion

/// Shared helpers for tests that need a throwaway on-disk SQLite database.
enum TestSupport {
    /// Unique database path in the host app's temporary directory.
    static func temporaryDatabaseURL() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("memoryline-tests-\(UUID().uuidString).db", isDirectory: false)
    }

    /// Removes the database plus its WAL sidecar files.
    static func removeDatabaseFiles(at url: URL) {
        for suffix in ["", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: url.path + suffix)
        }
    }

    /// Fully-populated capture builder; every nullable field can be exercised.
    static func makeCapture(
        id: String = UUID().uuidString.lowercased(),
        type: CaptureType = .memory,
        state: CaptureLifecycleState = .savedLocal,
        capturedAt: Date = Date(timeIntervalSince1970: 1_754_500_000.111),
        createdAt: Date = Date(timeIntervalSince1970: 1_754_500_001.222)
    ) -> CaptureRecord {
        CaptureRecord(
            id: id,
            type: type,
            state: state,
            capturedAt: capturedAt,
            timezoneId: "America/New_York",
            localOffsetMinutes: -240,
            titleHint: nil,
            userNote: nil,
            fileName: "\(id).m4a",
            byteLength: 0,
            sha256: "",
            durationSeconds: 0,
            createdAt: createdAt,
            uploadAttempts: 0,
            lastError: nil,
            artifactId: nil,
            uploadedAt: nil
        )
    }
}
