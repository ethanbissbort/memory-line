import Foundation

/// Schema for the review-decision outbox.
///
/// Applied from `SQLiteReviewDecisionStore.init` rather than appended to
/// `AppDatabase.migrations`, for the reason `CaptureStatusSchema` and
/// `TimelineProjectionSchema` both document: that array is the seam
/// `SQLiteCaptureStore.swift` owns, `PRAGMA user_version` counts its entries, and
/// a second array bumping the same counter would make the next `AppDatabase.open`
/// reject the file as newer than the app supports. Sharper here still, because
/// this table is macOS-only and the iOS companion opens the same database layout
/// with a shorter array.
enum ReviewDecisionSchema {
    /// `pending_event_id` is the primary key, not a surrogate: a reviewer has one
    /// verdict per item, and letting two rows exist for the same pending event
    /// would be a way to send Windows two contradictory answers with no rule for
    /// which wins.
    ///
    /// `client_sequence` is UNIQUE as a cheap assertion that the counter in
    /// settings is behaving. If it ever collides, an insert fails loudly here
    /// rather than the server silently discarding the second decision as a
    /// duplicate receipt — a failure that would otherwise look like a decision
    /// that simply never happened.
    private static let sql = """
        CREATE TABLE IF NOT EXISTS review_decision_outbox (
            pending_event_id TEXT PRIMARY KEY NOT NULL,
            verdict TEXT NOT NULL,
            decided_at REAL NOT NULL,
            client_sequence INTEGER NOT NULL UNIQUE,
            state TEXT NOT NULL DEFAULT 'pending',
            attempts INTEGER NOT NULL DEFAULT 0,
            last_error TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_review_decision_state
            ON review_decision_outbox (state, decided_at);
        """

    static func migrate(_ database: SQLiteDatabase) throws {
        try database.exec(sql)
    }
}

/// `ReviewDecisionStore` over the shared `SQLiteDatabase`.
///
/// Thread-safe: every statement runs under the database's internal lock.
final class SQLiteReviewDecisionStore: ReviewDecisionStore {
    private let database: SQLiteDatabase
    private let settings: any SettingsStore

    private static let columns =
        "pending_event_id, verdict, decided_at, client_sequence, state, attempts, last_error"

    /// Where the monotonic client-sequence counter lives. Settings rather than a
    /// table of its own: it is a single scalar, and the settings store is already
    /// on the same connection and the same transaction guarantees.
    private static let sequenceKey = "review_decision_sequence"

    init(database: SQLiteDatabase, settings: any SettingsStore) throws {
        self.database = database
        self.settings = settings
        try ReviewDecisionSchema.migrate(database)
    }

    func upsert(_ decision: ReviewDecisionRecord) throws {
        try database.run(
            """
            INSERT INTO review_decision_outbox (\(Self.columns))
            VALUES (?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(pending_event_id) DO UPDATE SET
                verdict = excluded.verdict,
                decided_at = excluded.decided_at,
                client_sequence = excluded.client_sequence,
                state = excluded.state,
                attempts = excluded.attempts,
                last_error = excluded.last_error;
            """,
            [
                .text(decision.pendingEventId),
                .text(decision.verdict.rawValue),
                .date(decision.decidedAt),
                .integer(decision.clientSequence),
                .text(decision.state.rawValue),
                .integer(Int64(decision.attempts)),
                .nullableText(decision.lastError)
            ])
    }

    func decision(for pendingEventId: String) throws -> ReviewDecisionRecord? {
        try database.query(
            "SELECT \(Self.columns) FROM review_decision_outbox WHERE pending_event_id = ?;",
            [.text(pendingEventId)],
            Self.decode(from:)
        ).first
    }

    func all() throws -> [ReviewDecisionRecord] {
        try database.query(
            "SELECT \(Self.columns) FROM review_decision_outbox ORDER BY decided_at ASC;",
            [],
            Self.decode(from:))
    }

    func pending() throws -> [ReviewDecisionRecord] {
        try database.query(
            """
            SELECT \(Self.columns) FROM review_decision_outbox
            WHERE state = ? ORDER BY decided_at ASC;
            """,
            [.text(ReviewDecisionState.pending.rawValue)],
            Self.decode(from:))
    }

    func delete(pendingEventId: String) throws {
        try database.run(
            "DELETE FROM review_decision_outbox WHERE pending_event_id = ?;",
            [.text(pendingEventId)])
    }

    func deleteAll() throws {
        try database.run("DELETE FROM review_decision_outbox;", [])
    }

    /// Reads the counter, advances it, persists it, and returns the new value.
    ///
    /// Persisted *before* returning, so a crash between allocating and inserting
    /// the outbox row burns a sequence number rather than handing the same one
    /// out twice. A gap in the sequence costs nothing — the server treats it as
    /// an entry it never received — whereas a repeat would be recognised as a
    /// duplicate receipt and the decision would be dropped without a trace.
    func nextClientSequence() throws -> Int64 {
        let current = settings.string(Self.sequenceKey).flatMap(Int64.init) ?? 0
        let next = current + 1
        settings.set(String(next), for: Self.sequenceKey)
        return next
    }

    private static func decode(from row: SQLiteRowReader) -> ReviewDecisionRecord {
        ReviewDecisionRecord(
            pendingEventId: row.text(0),
            // An unrecognised verdict or state can only come from a newer build
            // that wrote this file. Falling back keeps the row readable rather
            // than making one bad value throw the whole queue: an unknown verdict
            // is treated as a rejection (the answer that does not write to the
            // archive) and an unknown state as failed (the one that does not
            // send). Both are the conservative direction.
            verdict: ReviewVerdict(rawValue: row.text(1)) ?? .reject,
            decidedAt: row.date(2),
            clientSequence: row.int64(3),
            state: ReviewDecisionState(rawValue: row.text(4)) ?? .failed,
            attempts: row.int(5),
            lastError: row.optionalText(6))
    }
}
