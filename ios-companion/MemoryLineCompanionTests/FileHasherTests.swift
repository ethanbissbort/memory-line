import CryptoKit
import XCTest
@testable import MemoryLineCompanion

final class FileHasherTests: XCTestCase {
    private var createdFiles: [URL] = []

    override func tearDownWithError() throws {
        for url in createdFiles {
            try? FileManager.default.removeItem(at: url)
        }
        createdFiles = []
    }

    func testKnownThreeByteVector() throws {
        // FIPS 180-2 test vector: SHA-256("abc").
        let url = try writeTemporaryFile(Data("abc".utf8))
        XCTAssertEqual(
            try FileHasher.sha256Hex(of: url),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
    }

    func testEmptyFile() throws {
        let url = try writeTemporaryFile(Data())
        XCTAssertEqual(
            try FileHasher.sha256Hex(of: url),
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
    }

    func testMultiChunkFileMatchesOneShotDigest() throws {
        // 300 KiB crosses the 256 KiB streaming chunk boundary.
        let data = Data((0..<(300 * 1024)).map { UInt8($0 % 251) })
        let expected = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()

        let url = try writeTemporaryFile(data)
        XCTAssertEqual(try FileHasher.sha256Hex(of: url), expected)
    }

    func testMissingFileThrows() {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("filehasher-missing-\(UUID().uuidString)")
        XCTAssertThrowsError(try FileHasher.sha256Hex(of: url))
    }

    private func writeTemporaryFile(_ data: Data) throws -> URL {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("filehasher-\(UUID().uuidString).bin", isDirectory: false)
        try data.write(to: url)
        createdFiles.append(url)
        return url
    }
}
