import Foundation

/// The Windows-side processing status of one capture, as last received over
/// `capture_status` sync (design §19 Phase 3). This is a read-only projection:
/// the phone never authors it, and editing/approval stay on Windows.
///
/// It is deliberately separate from `CaptureRecord`. The two have different
/// owners and different lifetimes — a capture can exist with no status at all
/// (never uploaded), and the status carries detail the local pipeline has no
/// concept of (transcript preview, review counts).
struct CaptureStatusRecord: Equatable, Sendable {
    /// The capture this status belongs to; primary key of `capture_status`.
    let captureId: String
    /// A `SyncCaptureStatus` vocabulary value, stored exactly as received so an
    /// unrecognized future value is preserved rather than lost.
    var status: String
    /// Finer-grained Windows queue stage (`QueueProcessingStage`) when known.
    var processingStage: String?
    var transcriptAvailable: Bool
    /// Leading excerpt only (≤ 600 chars by contract) — the full transcript
    /// stays on Windows. Never logged (design §14.5).
    var transcriptPreview: String?
    /// Character count of the complete transcript, so the UI can say how much
    /// of it the preview covers without holding the whole text.
    var transcriptCharCount: Int?
    var pendingEventCount: Int?
    var approvedEventCount: Int?
    /// User-facing failure text. Carries no transcript content, file paths, or
    /// credentials by contract (design §14.5).
    var failureReason: String?
    var failureRetryable: Bool?
    /// When Windows produced this status (payload `updatedAtUtc`).
    var updatedAt: Date
}

/// Durable storage for `CaptureStatusRecord`. Mirrors `CaptureStore`'s shape:
/// implementations are thread-safe and calls are synchronous and cheap.
protocol CaptureStatusStore: AnyObject, Sendable {
    /// Inserts or replaces the status for `record.captureId`.
    func upsert(_ record: CaptureStatusRecord) throws
    func status(captureId: String) throws -> CaptureStatusRecord?
    /// Every stored status, newest update first.
    func allStatuses() throws -> [CaptureStatusRecord]
    func delete(captureId: String) throws
}

/// Schema for the Phase 3 `capture_status` table.
///
/// The batch is written to be re-runnable (`CREATE TABLE IF NOT EXISTS`) and is
/// applied by `SQLiteCaptureStatusStore.init` rather than appended to
/// `AppDatabase.migrations`. That array is the seam `SQLiteCaptureStore.swift`
/// owns, and `PRAGMA user_version` counts its entries: bumping the version from
/// a second array would make `AppDatabase.open` reject the file as "newer than
/// this app supports" on the next launch. Running idempotent DDL keeps the
/// two schemas independent and the version bookkeeping intact.
enum CaptureStatusSchema {
    /// One capture's remote status; no foreign key to `captures`, because a
    /// status may legitimately arrive for a capture the phone has deleted (it
    /// is dropped on apply) and the row must not block that deletion either.
    static let sql = """
        CREATE TABLE IF NOT EXISTS capture_status (
            capture_id            TEXT PRIMARY KEY NOT NULL,
            status                TEXT NOT NULL,
            processing_stage      TEXT,
            transcript_available  INTEGER NOT NULL DEFAULT 0,
            transcript_preview    TEXT,
            transcript_char_count INTEGER,
            pending_event_count   INTEGER,
            approved_event_count  INTEGER,
            failure_reason        TEXT,
            failure_retryable     INTEGER,
            updated_at            REAL NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_capture_status_updated_at ON capture_status(updated_at);
        """

    /// Applies the schema. Safe to call on every launch and from every store
    /// instance sharing the connection.
    static func migrate(_ database: SQLiteDatabase) throws {
        try database.exec(sql)
    }
}

/// `CaptureStatusStore` over the shared `SQLiteDatabase`. The `capture_status`
/// table mirrors `CaptureStatusRecord` one-to-one (snake_case columns, the date
/// as Unix epoch REAL, booleans as INTEGER 0/1). Thread-safe: all statements
/// run under the database's internal lock.
final class SQLiteCaptureStatusStore: CaptureStatusStore {
    private let database: SQLiteDatabase

    /// Column order shared by every SELECT so row decoding stays positional and consistent.
    private static let columns = """
        capture_id, status, processing_stage, transcript_available, transcript_preview, \
        transcript_char_count, pending_event_count, approved_event_count, failure_reason, \
        failure_retryable, updated_at
        """

    /// Opens the store, applying `CaptureStatusSchema` first so the table
    /// exists before any statement runs.
    init(database: SQLiteDatabase) throws {
        self.database = database
        try CaptureStatusSchema.migrate(database)
    }

    func upsert(_ record: CaptureStatusRecord) throws {
        let sql = """
            INSERT INTO capture_status (\(Self.columns))
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(capture_id) DO UPDATE SET
                status = excluded.status,
                processing_stage = excluded.processing_stage,
                transcript_available = excluded.transcript_available,
                transcript_preview = excluded.transcript_preview,
                transcript_char_count = excluded.transcript_char_count,
                pending_event_count = excluded.pending_event_count,
                approved_event_count = excluded.approved_event_count,
                failure_reason = excluded.failure_reason,
                failure_retryable = excluded.failure_retryable,
                updated_at = excluded.updated_at;
            """
        try database.run(sql, [
            .text(record.captureId),
            .text(record.status),
            .nullableText(record.processingStage),
            .integer(record.transcriptAvailable ? 1 : 0),
            .nullableText(record.transcriptPreview),
            Self.nullableInteger(record.transcriptCharCount),
            Self.nullableInteger(record.pendingEventCount),
            Self.nullableInteger(record.approvedEventCount),
            .nullableText(record.failureReason),
            Self.nullableBoolean(record.failureRetryable),
            .date(record.updatedAt)
        ])
    }

    func status(captureId: String) throws -> CaptureStatusRecord? {
        try database.query(
            "SELECT \(Self.columns) FROM capture_status WHERE capture_id = ?;",
            [.text(captureId)],
            Self.record(from:)
        ).first
    }

    func allStatuses() throws -> [CaptureStatusRecord] {
        try database.query(
            "SELECT \(Self.columns) FROM capture_status ORDER BY updated_at DESC, capture_id DESC;",
            [],
            Self.record(from:))
    }

    func delete(captureId: String) throws {
        try database.run("DELETE FROM capture_status WHERE capture_id = ?;", [.text(captureId)])
    }

    // MARK: - Row mapping

    private static func nullableInteger(_ value: Int?) -> SQLiteValue {
        value.map { SQLiteValue.integer(Int64($0)) } ?? .null
    }

    private static func nullableBoolean(_ value: Bool?) -> SQLiteValue {
        value.map { SQLiteValue.integer($0 ? 1 : 0) } ?? .null
    }

    /// Decodes a row selected with `columns`.
    private static func record(from row: SQLiteRowReader) -> CaptureStatusRecord {
        CaptureStatusRecord(
            captureId: row.text(0),
            status: row.text(1),
            processingStage: row.optionalText(2),
            transcriptAvailable: row.int(3) != 0,
            transcriptPreview: row.optionalText(4),
            transcriptCharCount: row.isNull(5) ? nil : row.int(5),
            pendingEventCount: row.isNull(6) ? nil : row.int(6),
            approvedEventCount: row.isNull(7) ? nil : row.int(7),
            failureReason: row.optionalText(8),
            failureRetryable: row.isNull(9) ? nil : row.int(9) != 0,
            updatedAt: row.date(10)
        )
    }
}
