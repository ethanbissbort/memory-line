import XCTest
@testable import MemoryLineCompanion

final class MigrationTests: XCTestCase {
    private var databaseURL: URL!

    override func setUpWithError() throws {
        databaseURL = TestSupport.temporaryDatabaseURL()
    }

    override func tearDownWithError() throws {
        TestSupport.removeDatabaseFiles(at: databaseURL)
    }

    func testFreshDatabaseGetsFullSchema() throws {
        let database = try SQLiteDatabase(path: databaseURL.path)
        try database.migrate(AppDatabase.migrations)

        let version = try database.scalarInt64("PRAGMA user_version;")
        XCTAssertEqual(version, Int64(AppDatabase.migrations.count))

        let tables = try database.query(
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;"
        ) { $0.text(0) }
        XCTAssertTrue(tables.contains("captures"), "tables: \(tables)")
        XCTAssertTrue(tables.contains("settings"), "tables: \(tables)")
    }

    func testMigratingTwiceIsANoOp() throws {
        let database = try SQLiteDatabase(path: databaseURL.path)
        try database.migrate(AppDatabase.migrations)

        // Data written between runs must survive the second (no-op) run.
        let store = SQLiteCaptureStore(database: database)
        try store.insert(TestSupport.makeCapture(id: "survivor"))

        try database.migrate(AppDatabase.migrations)

        XCTAssertEqual(try database.scalarInt64("PRAGMA user_version;"),
                       Int64(AppDatabase.migrations.count))
        XCTAssertNotNil(try store.capture(id: "survivor"))
    }

    func testSchemaSurvivesReopen() throws {
        do {
            let database = try AppDatabase.open(at: databaseURL)
            let store = SQLiteCaptureStore(database: database)
            try store.insert(TestSupport.makeCapture(id: "persisted"))
        } // both references die here, closing the connection

        let reopened = try AppDatabase.open(at: databaseURL)
        let reopenedStore = SQLiteCaptureStore(database: reopened)
        XCTAssertNotNil(try reopenedStore.capture(id: "persisted"))
    }

    func testNewerDatabaseVersionIsRejected() throws {
        let database = try SQLiteDatabase(path: databaseURL.path)
        try database.exec("PRAGMA user_version = \(AppDatabase.migrations.count + 1);")
        XCTAssertThrowsError(try database.migrate(AppDatabase.migrations)) { error in
            XCTAssertTrue(error is SQLiteError)
        }
    }
}
