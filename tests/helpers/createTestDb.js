/**
 * Shared test-database helper.
 *
 * Builds an in-memory better-sqlite3 database from the CANONICAL production
 * DDL (src/database/schemas/schema.sql) so test schemas can never drift from
 * the real one again. All service test suites should use this instead of
 * hand-rolling CREATE TABLE statements.
 *
 * The schema file also installs the events_fts external-content FTS5 table
 * and the triggers that keep it in sync, so plain INSERT/UPDATE/DELETE on
 * `events` maintains full-text search automatically.
 */

const fs = require('fs');
const path = require('path');
const Database = require('better-sqlite3');

const SCHEMA_PATH = path.join(
    __dirname, '..', '..', 'src', 'database', 'schemas', 'schema.sql'
);

let schemaSql = null;

/**
 * Create a fresh in-memory database with the full production schema applied.
 * @returns {Database} better-sqlite3 database handle
 */
function createTestDb() {
    if (schemaSql === null) {
        schemaSql = fs.readFileSync(SCHEMA_PATH, 'utf8');
    }
    const db = new Database(':memory:');
    // Mirror production pragmas that affect behavior
    // (src/database/database.js enables foreign key enforcement).
    db.pragma('foreign_keys = ON');
    db.exec(schemaSql);
    return db;
}

// ---------------------------------------------------------------------------
// Seed helpers. All use the canonical snake_case column names and explicit
// column lists so they keep working if columns are ever added to the schema.
// ---------------------------------------------------------------------------

function insertEra(db, {
    era_id, name, start_date, end_date = null,
    color_code = '#3498db', description = null
}) {
    db.prepare(`
        INSERT INTO eras (era_id, name, start_date, end_date, color_code, description)
        VALUES (?, ?, ?, ?, ?, ?)
    `).run(era_id, name, start_date, end_date, color_code, description);
    return era_id;
}

function insertEvent(db, {
    event_id, title, start_date, end_date = null, description = null,
    category = null, era_id = null, audio_file_path = null,
    raw_transcript = null, extraction_metadata = null
}) {
    db.prepare(`
        INSERT INTO events (
            event_id, title, start_date, end_date, description,
            category, era_id, audio_file_path, raw_transcript, extraction_metadata
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `).run(
        event_id, title, start_date, end_date, description,
        category, era_id, audio_file_path, raw_transcript, extraction_metadata
    );
    return event_id;
}

function insertTag(db, { tag_id, tag_name }) {
    db.prepare('INSERT INTO tags (tag_id, tag_name) VALUES (?, ?)').run(tag_id, tag_name);
    return tag_id;
}

function insertPerson(db, { person_id, name }) {
    db.prepare('INSERT INTO people (person_id, name) VALUES (?, ?)').run(person_id, name);
    return person_id;
}

function insertLocation(db, { location_id, name }) {
    db.prepare('INSERT INTO locations (location_id, name) VALUES (?, ?)').run(location_id, name);
    return location_id;
}

function linkTag(db, event_id, tag_id, { confidence_score = 1.0, is_manual = 1 } = {}) {
    db.prepare(`
        INSERT INTO event_tags (event_id, tag_id, confidence_score, is_manual)
        VALUES (?, ?, ?, ?)
    `).run(event_id, tag_id, confidence_score, is_manual);
}

function linkPerson(db, event_id, person_id) {
    db.prepare('INSERT INTO event_people (event_id, person_id) VALUES (?, ?)')
        .run(event_id, person_id);
}

function linkLocation(db, event_id, location_id) {
    db.prepare('INSERT INTO event_locations (event_id, location_id) VALUES (?, ?)')
        .run(event_id, location_id);
}

/**
 * Insert a cross-reference. Note the schema enforces event_id_1 < event_id_2
 * and a CHECK on relationship_type
 * ('causal','thematic','temporal','person','location','other').
 */
function insertCrossReference(db, {
    reference_id, event_id_1, event_id_2, relationship_type,
    confidence_score = null, analysis_details = null
}) {
    db.prepare(`
        INSERT INTO cross_references (
            reference_id, event_id_1, event_id_2,
            relationship_type, confidence_score, analysis_details
        ) VALUES (?, ?, ?, ?, ?, ?)
    `).run(
        reference_id, event_id_1, event_id_2,
        relationship_type, confidence_score, analysis_details
    );
    return reference_id;
}

module.exports = {
    createTestDb,
    insertEra,
    insertEvent,
    insertTag,
    insertPerson,
    insertLocation,
    linkTag,
    linkPerson,
    linkLocation,
    insertCrossReference
};
