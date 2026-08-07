import Foundation
import SQLite3
import os

/// `SQLITE_TRANSIENT` is a macro the C header defines as `(sqlite3_destructor_type)-1`,
/// which Swift cannot import; this is the standard reconstruction. It tells SQLite to
/// copy bound text immediately, so Swift's temporary C-string bridging is safe.
private let sqliteTransient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

/// Error thrown by the SQLite layer. Carries the SQLite result code and the
/// database's error message (never bound values — parameters are bound, not
/// interpolated, so no user content can appear here).
struct SQLiteError: Error, LocalizedError, CustomStringConvertible, Sendable {
    /// SQLite result code (e.g. `SQLITE_BUSY`), or `SQLITE_ERROR` for wrapper-level failures.
    var code: Int32
    /// Human-readable message from `sqlite3_errmsg` plus wrapper context.
    var message: String

    var description: String { "SQLite error \(code): \(message)" }
    var errorDescription: String? { description }
}

/// A positional value for `?` placeholders in a prepared statement.
/// Dates are persisted as Unix epoch seconds (REAL), booleans as INTEGER 0/1.
enum SQLiteValue: Sendable {
    case text(String)
    case integer(Int64)
    case real(Double)
    case null

    /// Date as Unix epoch seconds.
    static func date(_ value: Date) -> SQLiteValue { .real(value.timeIntervalSince1970) }
    static func nullableText(_ value: String?) -> SQLiteValue { value.map(SQLiteValue.text) ?? .null }
    static func nullableDate(_ value: Date?) -> SQLiteValue { value.map(SQLiteValue.date) ?? .null }
}

/// Column readers for the current result row. Only valid synchronously inside the
/// transform closure passed to `SQLiteDatabase.query` — never store or escape one.
struct SQLiteRowReader {
    let statement: OpaquePointer

    func isNull(_ index: Int) -> Bool {
        sqlite3_column_type(statement, Int32(index)) == SQLITE_NULL
    }

    /// TEXT column; NULL reads as "".
    func text(_ index: Int) -> String {
        guard let cString = sqlite3_column_text(statement, Int32(index)) else { return "" }
        return String(cString: cString)
    }

    func optionalText(_ index: Int) -> String? {
        isNull(index) ? nil : text(index)
    }

    func int(_ index: Int) -> Int {
        Int(sqlite3_column_int64(statement, Int32(index)))
    }

    func int64(_ index: Int) -> Int64 {
        sqlite3_column_int64(statement, Int32(index))
    }

    func double(_ index: Int) -> Double {
        sqlite3_column_double(statement, Int32(index))
    }

    /// REAL column holding Unix epoch seconds.
    func date(_ index: Int) -> Date {
        Date(timeIntervalSince1970: double(index))
    }

    func optionalDate(_ index: Int) -> Date? {
        isNull(index) ? nil : date(index)
    }
}

/// Minimal safe wrapper over the SQLite3 C API.
///
/// Opened with `READWRITE | CREATE | FULLMUTEX`, WAL journaling, a 5 s busy
/// timeout, and foreign keys on. Statements are prepared per call and finalized
/// on every path (`defer`), so no statement handles outlive a method.
///
/// Thread safety / `@unchecked Sendable`: every access to `handle` happens while
/// `lock` (a plain, non-recursive `NSLock`) is held — public methods take the
/// lock exactly once and only call the private unlocked `_`-prefixed helpers,
/// which never re-enter a public method. `deinit` runs without the lock, which
/// is sound because no other reference can exist at that point.
final class SQLiteDatabase: @unchecked Sendable {
    private let lock = NSLock()
    private var handle: OpaquePointer?
    private let logger = AppLog.persistence

    /// Opens (creating if needed) the database file at `path` and applies the
    /// connection pragmas. The parent directory must already exist.
    init(path: String) throws {
        var db: OpaquePointer?
        let flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX
        let rc = sqlite3_open_v2(path, &db, flags, nil)
        guard rc == SQLITE_OK, let opened = db else {
            let message = db.map { String(cString: sqlite3_errmsg($0)) } ?? "unable to open database"
            sqlite3_close_v2(db)
            throw SQLiteError(code: rc, message: "open failed: \(message)")
        }
        handle = opened
        do {
            try _exec("PRAGMA journal_mode = WAL;")
            try _exec("PRAGMA busy_timeout = 5000;")
            try _exec("PRAGMA foreign_keys = ON;")
        } catch {
            sqlite3_close_v2(handle)
            handle = nil
            throw error
        }
    }

    deinit {
        sqlite3_close_v2(handle)
    }

    // MARK: - Public surface (each method takes the lock exactly once)

    /// Executes one or more semicolon-separated SQL statements with no bindings.
    func exec(_ sql: String) throws {
        lock.lock()
        defer { lock.unlock() }
        try _exec(sql)
    }

    /// Runs a single statement with positional bindings, discarding any rows.
    func run(_ sql: String, _ values: [SQLiteValue] = []) throws {
        lock.lock()
        defer { lock.unlock() }
        try _run(sql, values)
    }

    /// Runs a query and maps each result row through `transform`.
    func query<Row>(
        _ sql: String,
        _ values: [SQLiteValue] = [],
        _ transform: (SQLiteRowReader) throws -> Row
    ) throws -> [Row] {
        lock.lock()
        defer { lock.unlock() }
        return try _query(sql, values, transform)
    }

    /// First column of the first row as Int64, or nil when the query returns no rows.
    func scalarInt64(_ sql: String, _ values: [SQLiteValue] = []) throws -> Int64? {
        lock.lock()
        defer { lock.unlock() }
        return try _query(sql, values) { $0.int64(0) }.first
    }

    /// `PRAGMA user_version` migration runner. `migrations[n]` is the SQL batch
    /// that moves the schema from version n to n+1; batches already recorded in
    /// `user_version` are skipped, so running the same array twice is a no-op.
    /// Each pending batch is applied inside its own transaction together with the
    /// version bump (user_version is stored in the database header and is
    /// transactional), so a crash mid-migration rolls back cleanly.
    func migrate(_ migrations: [String]) throws {
        lock.lock()
        defer { lock.unlock() }
        let current = Int(try _query("PRAGMA user_version;", []) { $0.int64(0) }.first ?? 0)
        guard current <= migrations.count else {
            throw SQLiteError(
                code: SQLITE_ERROR,
                message: "database schema version \(current) is newer than this app supports (\(migrations.count))")
        }
        guard current < migrations.count else { return }
        for versionIndex in current..<migrations.count {
            try _exec("BEGIN IMMEDIATE TRANSACTION;")
            do {
                try _exec(migrations[versionIndex])
                try _exec("PRAGMA user_version = \(versionIndex + 1);")
                try _exec("COMMIT;")
            } catch {
                try? _exec("ROLLBACK;")
                throw error
            }
            logger.info("Applied database migration to schema version \(versionIndex + 1)")
        }
    }

    // MARK: - Unlocked internals (caller must hold `lock`)

    private func _exec(_ sql: String) throws {
        guard let handle else { throw SQLiteError(code: SQLITE_MISUSE, message: "database is closed") }
        guard sqlite3_exec(handle, sql, nil, nil, nil) == SQLITE_OK else {
            throw _lastError("exec failed")
        }
    }

    private func _run(_ sql: String, _ values: [SQLiteValue]) throws {
        try _withStatement(sql, values) { statement in
            let rc = sqlite3_step(statement)
            // SQLITE_ROW allowed so single-row PRAGMAs can go through run().
            guard rc == SQLITE_DONE || rc == SQLITE_ROW else {
                throw _lastError("step failed")
            }
        }
    }

    private func _query<Row>(
        _ sql: String,
        _ values: [SQLiteValue],
        _ transform: (SQLiteRowReader) throws -> Row
    ) throws -> [Row] {
        try _withStatement(sql, values) { statement in
            var rows: [Row] = []
            while true {
                let rc = sqlite3_step(statement)
                if rc == SQLITE_ROW {
                    rows.append(try transform(SQLiteRowReader(statement: statement)))
                } else if rc == SQLITE_DONE {
                    break
                } else {
                    throw _lastError("step failed")
                }
            }
            return rows
        }
    }

    /// Prepares `sql`, binds `values` positionally (1-based), runs `body`, and
    /// finalizes the statement on all paths.
    private func _withStatement<T>(
        _ sql: String,
        _ values: [SQLiteValue],
        _ body: (OpaquePointer) throws -> T
    ) throws -> T {
        guard let handle else { throw SQLiteError(code: SQLITE_MISUSE, message: "database is closed") }
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK, let statement else {
            throw _lastError("prepare failed")
        }
        defer { sqlite3_finalize(statement) }
        for (offset, value) in values.enumerated() {
            let index = Int32(offset + 1)
            let rc: Int32
            switch value {
            case .text(let string):
                rc = sqlite3_bind_text(statement, index, string, -1, sqliteTransient)
            case .integer(let number):
                rc = sqlite3_bind_int64(statement, index, number)
            case .real(let number):
                rc = sqlite3_bind_double(statement, index, number)
            case .null:
                rc = sqlite3_bind_null(statement, index)
            }
            guard rc == SQLITE_OK else { throw _lastError("bind failed at index \(index)") }
        }
        return try body(statement)
    }

    private func _lastError(_ context: String) -> SQLiteError {
        guard let handle else { return SQLiteError(code: SQLITE_MISUSE, message: context) }
        let code = sqlite3_errcode(handle)
        let message = String(cString: sqlite3_errmsg(handle))
        return SQLiteError(code: code, message: "\(context): \(message)")
    }
}
