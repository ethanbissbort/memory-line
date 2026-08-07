import CryptoKit
import Foundation

/// Streaming SHA-256 for artifact integrity (design §8.5, §16.1). Reads in
/// 256 KiB chunks so hashing hour-long drive recordings never loads the whole
/// file into memory.
enum FileHasher {
    private static let chunkSize = 256 * 1024

    /// Lowercase hex SHA-256 of the file's contents. Matches the wire format
    /// expected by `ArtifactInitiateRequest.expectedSha256` and
    /// `ArtifactCompleteRequest.sha256`.
    static func sha256Hex(of url: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var hasher = SHA256()
        while let chunk = try handle.read(upToCount: chunkSize), !chunk.isEmpty {
            hasher.update(data: chunk)
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }
}
