import XCTest
@testable import MemoryLineCompanion

final class DTOCodingTests: XCTestCase {

    // MARK: - Round trips

    func testDeviceRegisterResponseRoundTrip() throws {
        let original = DeviceRegisterResponse(
            deviceId: "device-1",
            ownerId: "owner-1",
            accessToken: "token-access",
            accessTokenExpiresAtUtc: Date(timeIntervalSince1970: 1_754_500_000.5),
            refreshToken: "token-refresh")

        let data = try SyncJSON.encoder.encode(original)
        let decoded = try SyncJSON.decoder.decode(DeviceRegisterResponse.self, from: data)

        XCTAssertEqual(decoded.deviceId, original.deviceId)
        XCTAssertEqual(decoded.ownerId, original.ownerId)
        XCTAssertEqual(decoded.accessToken, original.accessToken)
        XCTAssertEqual(decoded.refreshToken, original.refreshToken)
        XCTAssertEqual(
            decoded.accessTokenExpiresAtUtc.timeIntervalSince1970,
            original.accessTokenExpiresAtUtc.timeIntervalSince1970,
            accuracy: 0.002)
    }

    func testCaptureCreateRequestRoundTrip() throws {
        let original = CaptureCreateRequest(
            captureId: "3f0b7f2a-aaaa-bbbb-cccc-1234567890ab",
            sourceDeviceId: "device-1",
            captureType: "trip_log",
            capturedAtUtc: Date(timeIntervalSince1970: 1_754_500_000.25),
            timezoneId: "America/Chicago",
            localOffsetMinutes: -300,
            tripId: nil,
            titleHint: "Cadillac Ranch",
            userNote: nil,
            locationContext: LocationContext(
                latitude: 35.1872,
                longitude: -101.9871,
                horizontalAccuracyM: 5.0,
                headingDegrees: nil,
                speedMps: 31.3),
            clientSchemaVersion: 1)

        let data = try SyncJSON.encoder.encode(original)
        let decoded = try SyncJSON.decoder.decode(CaptureCreateRequest.self, from: data)

        XCTAssertEqual(decoded.captureId, original.captureId)
        XCTAssertEqual(decoded.sourceDeviceId, original.sourceDeviceId)
        XCTAssertEqual(decoded.captureType, original.captureType)
        XCTAssertEqual(
            decoded.capturedAtUtc.timeIntervalSince1970,
            original.capturedAtUtc.timeIntervalSince1970,
            accuracy: 0.002)
        XCTAssertEqual(decoded.timezoneId, original.timezoneId)
        XCTAssertEqual(decoded.localOffsetMinutes, original.localOffsetMinutes)
        XCTAssertNil(decoded.tripId)
        XCTAssertEqual(decoded.titleHint, original.titleHint)
        XCTAssertNil(decoded.userNote)
        XCTAssertEqual(decoded.clientSchemaVersion, 1)

        let location = try XCTUnwrap(decoded.locationContext)
        XCTAssertEqual(location.latitude, 35.1872, accuracy: 0.000001)
        XCTAssertEqual(location.longitude, -101.9871, accuracy: 0.000001)
        XCTAssertEqual(try XCTUnwrap(location.horizontalAccuracyM), 5.0, accuracy: 0.000001)
        XCTAssertNil(location.headingDegrees)
        XCTAssertEqual(try XCTUnwrap(location.speedMps), 31.3, accuracy: 0.000001)
    }

    // MARK: - Date parsing tolerance

    /// The service (System.Text.Json) emits both "…T12:00:00Z" and
    /// "…T12:00:00.125Z"; the decoder must accept both.
    func testDecodesDatesWithAndWithoutFractionalSeconds() throws {
        func payload(expiry: String) -> Data {
            Data("""
            {"deviceId":"d","ownerId":"o","accessToken":"a",\
            "accessTokenExpiresAtUtc":"\(expiry)","refreshToken":"r"}
            """.utf8)
        }

        let whole = try SyncJSON.decoder.decode(
            DeviceRegisterResponse.self, from: payload(expiry: "2026-08-07T12:00:00Z"))
        let fractional = try SyncJSON.decoder.decode(
            DeviceRegisterResponse.self, from: payload(expiry: "2026-08-07T12:00:00.125Z"))

        let reference = try XCTUnwrap(ISO8601DateFormatter().date(from: "2026-08-07T12:00:00Z"))
        XCTAssertEqual(whole.accessTokenExpiresAtUtc, reference)
        XCTAssertEqual(
            fractional.accessTokenExpiresAtUtc.timeIntervalSince(reference),
            0.125, accuracy: 0.0005)
    }

    func testUnrecognizedDateFormatThrows() {
        let data = Data("""
        {"deviceId":"d","ownerId":"o","accessToken":"a",\
        "accessTokenExpiresAtUtc":"07/08/2026 12:00","refreshToken":"r"}
        """.utf8)
        XCTAssertThrowsError(try SyncJSON.decoder.decode(DeviceRegisterResponse.self, from: data))
    }

    // MARK: - Wire field names

    /// The wire contract is camelCase with no key-conversion strategy — property
    /// names ARE the wire names.
    func testEncodesCamelCaseFieldNames() throws {
        let request = CaptureCreateRequest(
            captureId: "cap-1",
            sourceDeviceId: "dev-1",
            captureType: "memory",
            capturedAtUtc: Date(timeIntervalSince1970: 1_754_500_000))

        let data = try SyncJSON.encoder.encode(request)
        let json = try XCTUnwrap(String(data: data, encoding: .utf8))

        XCTAssertTrue(json.contains("\"captureId\""), "raw JSON: \(json)")
        XCTAssertTrue(json.contains("\"sourceDeviceId\""), "raw JSON: \(json)")
        XCTAssertTrue(json.contains("\"capturedAtUtc\""), "raw JSON: \(json)")
        XCTAssertTrue(json.contains("\"clientSchemaVersion\""), "raw JSON: \(json)")
        XCTAssertFalse(json.contains("capture_id"), "raw JSON: \(json)")
    }

    /// Encoded dates must be ISO 8601 UTC ("Z"), not epoch numbers.
    func testEncodesDatesAsIso8601Utc() throws {
        let request = CaptureCreateRequest(
            captureId: "cap-1",
            sourceDeviceId: "dev-1",
            captureType: "memory",
            capturedAtUtc: Date(timeIntervalSince1970: 1_754_500_000))

        let data = try SyncJSON.encoder.encode(request)
        let json = try XCTUnwrap(String(data: data, encoding: .utf8))
        XCTAssertTrue(json.contains("\"capturedAtUtc\":\"20"), "raw JSON: \(json)")
        XCTAssertTrue(json.contains("Z\""), "raw JSON: \(json)")
    }
}
