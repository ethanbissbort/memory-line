import XCTest
@testable import MemoryLineCompanion

final class SettingsStoreTests: XCTestCase {
    private var databaseURL: URL!
    private var database: SQLiteDatabase!
    private var settings: SQLiteSettingsStore!

    override func setUpWithError() throws {
        databaseURL = TestSupport.temporaryDatabaseURL()
        database = try AppDatabase.open(at: databaseURL)
        settings = SQLiteSettingsStore(database: database)
    }

    override func tearDownWithError() throws {
        settings = nil
        database = nil
        TestSupport.removeDatabaseFiles(at: databaseURL)
    }

    func testUnsetKeyReturnsNil() {
        XCTAssertNil(settings.string(AppSettingsKey.serverURL))
    }

    func testStringRoundTrip() {
        settings.set("http://mac-mini.tailnet:8080", for: AppSettingsKey.serverURL)
        XCTAssertEqual(settings.string(AppSettingsKey.serverURL), "http://mac-mini.tailnet:8080")
    }

    func testSetOverwritesExistingValue() {
        settings.set("first", for: "k")
        settings.set("second", for: "k")
        XCTAssertEqual(settings.string("k"), "second")
    }

    func testSetNilDeletesValue() {
        settings.set("value", for: "k")
        settings.set(nil, for: "k")
        XCTAssertNil(settings.string("k"))
    }

    func testKeysAreIndependent() {
        settings.set("a", for: "key-a")
        settings.set("b", for: "key-b")
        XCTAssertEqual(settings.string("key-a"), "a")
        XCTAssertEqual(settings.string("key-b"), "b")
    }

    func testBoolDefaultAppliesWhenUnset() {
        XCTAssertTrue(settings.bool(AppSettingsKey.autoUpload, default: true))
        XCTAssertFalse(settings.bool(AppSettingsKey.autoUpload, default: false))
    }

    func testBoolRoundTrip() {
        settings.set(true, for: AppSettingsKey.spokenConfirmation)
        XCTAssertTrue(settings.bool(AppSettingsKey.spokenConfirmation, default: false))

        settings.set(false, for: AppSettingsKey.spokenConfirmation)
        XCTAssertFalse(settings.bool(AppSettingsKey.spokenConfirmation, default: true))
    }

    func testBoolAcceptsTrueFalseSpellings() {
        settings.set("true", for: "k")
        XCTAssertTrue(settings.bool("k", default: false))
        settings.set("false", for: "k")
        XCTAssertFalse(settings.bool("k", default: true))
    }

    func testBoolUnparsableValueFallsBackToDefault() {
        settings.set("maybe", for: "k")
        XCTAssertTrue(settings.bool("k", default: true))
        XCTAssertFalse(settings.bool("k", default: false))
    }
}
