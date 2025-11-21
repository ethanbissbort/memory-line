-- Memory Timeline Database Schema
-- Version: 1.0.0

-- Events Table: Core table storing all timeline events
CREATE TABLE IF NOT EXISTS events (
    event_id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    start_date TEXT NOT NULL, -- ISO 8601 format: YYYY-MM-DD
    end_date TEXT, -- NULL for point events
    description TEXT,
    category TEXT CHECK(category IN ('milestone', 'work', 'education', 'relationship', 'travel', 'achievement', 'challenge', 'era', 'other')),
    era_id TEXT,
    audio_file_path TEXT,
    raw_transcript TEXT,
    extraction_metadata TEXT, -- JSON string
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (era_id) REFERENCES eras(era_id) ON DELETE SET NULL
);

-- Index for efficient date range queries
CREATE INDEX IF NOT EXISTS idx_events_start_date ON events(start_date);
CREATE INDEX IF NOT EXISTS idx_events_end_date ON events(end_date);
CREATE INDEX IF NOT EXISTS idx_events_era_id ON events(era_id);
CREATE INDEX IF NOT EXISTS idx_events_category ON events(category);

-- Full-text search virtual table for events
CREATE VIRTUAL TABLE IF NOT EXISTS events_fts USING fts5(
    title,
    description,
    raw_transcript,
    content=events,
    content_rowid=rowid
);

-- Triggers to keep FTS table in sync
CREATE TRIGGER IF NOT EXISTS events_fts_insert AFTER INSERT ON events BEGIN
    INSERT INTO events_fts(rowid, title, description, raw_transcript)
    VALUES (new.rowid, new.title, new.description, new.raw_transcript);
END;

CREATE TRIGGER IF NOT EXISTS events_fts_delete AFTER DELETE ON events BEGIN
    DELETE FROM events_fts WHERE rowid = old.rowid;
END;

CREATE TRIGGER IF NOT EXISTS events_fts_update AFTER UPDATE ON events BEGIN
    UPDATE events_fts
    SET title = new.title,
        description = new.description,
        raw_transcript = new.raw_transcript
    WHERE rowid = new.rowid;
END;

-- Trigger to update updated_at timestamp
CREATE TRIGGER IF NOT EXISTS events_updated_at AFTER UPDATE ON events BEGIN
    UPDATE events SET updated_at = datetime('now') WHERE event_id = NEW.event_id;
END;

-- Eras Table: Life phases/periods with color coding
CREATE TABLE IF NOT EXISTS eras (
    era_id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    start_date TEXT NOT NULL,
    end_date TEXT, -- NULL for ongoing eras
    color_code TEXT NOT NULL, -- Hex color code
    description TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_eras_dates ON eras(start_date, end_date);

CREATE TRIGGER IF NOT EXISTS eras_updated_at AFTER UPDATE ON eras BEGIN
    UPDATE eras SET updated_at = datetime('now') WHERE era_id = NEW.era_id;
END;

-- Recording Queue Table: Tracks audio files awaiting processing
CREATE TABLE IF NOT EXISTS recording_queue (
    queue_id TEXT PRIMARY KEY,
    audio_file_path TEXT NOT NULL,
    status TEXT NOT NULL CHECK(status IN ('pending', 'processing', 'completed', 'failed')) DEFAULT 'pending',
    duration_seconds REAL,
    file_size_bytes INTEGER,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    processed_at TEXT,
    error_message TEXT
);

CREATE INDEX IF NOT EXISTS idx_queue_status ON recording_queue(status);
CREATE INDEX IF NOT EXISTS idx_queue_created_at ON recording_queue(created_at);

-- Pending Events Table: Extracted events awaiting user review
CREATE TABLE IF NOT EXISTS pending_events (
    pending_id TEXT PRIMARY KEY,
    extracted_data TEXT NOT NULL, -- JSON string with structured event details
    audio_file_path TEXT,
    transcript TEXT,
    queue_id TEXT,
    status TEXT NOT NULL CHECK(status IN ('pending_review', 'approved', 'rejected')) DEFAULT 'pending_review',
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    reviewed_at TEXT,
    FOREIGN KEY (queue_id) REFERENCES recording_queue(queue_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_pending_status ON pending_events(status);
CREATE INDEX IF NOT EXISTS idx_pending_created_at ON pending_events(created_at);

-- Tags Table: Categorization tags for events
CREATE TABLE IF NOT EXISTS tags (
    tag_id TEXT PRIMARY KEY,
    tag_name TEXT NOT NULL UNIQUE COLLATE NOCASE,
    created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_tags_name ON tags(tag_name COLLATE NOCASE);

-- Event_Tags Junction Table: Many-to-many relationship between events and tags
CREATE TABLE IF NOT EXISTS event_tags (
    event_id TEXT NOT NULL,
    tag_id TEXT NOT NULL,
    confidence_score REAL DEFAULT 1.0 CHECK(confidence_score >= 0 AND confidence_score <= 1),
    is_manual INTEGER NOT NULL DEFAULT 1, -- 1 for manual, 0 for LLM-suggested
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (event_id, tag_id),
    FOREIGN KEY (event_id) REFERENCES events(event_id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tags(tag_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_event_tags_event ON event_tags(event_id);
CREATE INDEX IF NOT EXISTS idx_event_tags_tag ON event_tags(tag_id);

-- Cross_References Table: Relationships between events identified by RAG analysis
CREATE TABLE IF NOT EXISTS cross_references (
    reference_id TEXT PRIMARY KEY,
    event_id_1 TEXT NOT NULL,
    event_id_2 TEXT NOT NULL,
    relationship_type TEXT NOT NULL CHECK(relationship_type IN ('causal', 'thematic', 'temporal', 'person', 'location', 'other')),
    confidence_score REAL CHECK(confidence_score >= 0 AND confidence_score <= 1),
    analysis_details TEXT, -- JSON string with detailed explanation
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (event_id_1) REFERENCES events(event_id) ON DELETE CASCADE,
    FOREIGN KEY (event_id_2) REFERENCES events(event_id) ON DELETE CASCADE,
    CHECK (event_id_1 < event_id_2) -- Ensure consistent ordering to prevent duplicates
);

CREATE INDEX IF NOT EXISTS idx_cross_refs_event1 ON cross_references(event_id_1);
CREATE INDEX IF NOT EXISTS idx_cross_refs_event2 ON cross_references(event_id_2);
CREATE INDEX IF NOT EXISTS idx_cross_refs_type ON cross_references(relationship_type);

-- People Table: Track important people mentioned in events
CREATE TABLE IF NOT EXISTS people (
    person_id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE COLLATE NOCASE,
    created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_people_name ON people(name COLLATE NOCASE);

-- Event_People Junction Table: Link people to events
CREATE TABLE IF NOT EXISTS event_people (
    event_id TEXT NOT NULL,
    person_id TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (event_id, person_id),
    FOREIGN KEY (event_id) REFERENCES events(event_id) ON DELETE CASCADE,
    FOREIGN KEY (person_id) REFERENCES people(person_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_event_people_event ON event_people(event_id);
CREATE INDEX IF NOT EXISTS idx_event_people_person ON event_people(person_id);

-- Locations Table: Track significant places mentioned in events
CREATE TABLE IF NOT EXISTS locations (
    location_id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE COLLATE NOCASE,
    created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_locations_name ON locations(name COLLATE NOCASE);

-- Event_Locations Junction Table: Link locations to events
CREATE TABLE IF NOT EXISTS event_locations (
    event_id TEXT NOT NULL,
    location_id TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (event_id, location_id),
    FOREIGN KEY (event_id) REFERENCES events(event_id) ON DELETE CASCADE,
    FOREIGN KEY (location_id) REFERENCES locations(location_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_event_locations_event ON event_locations(event_id);
CREATE INDEX IF NOT EXISTS idx_event_locations_location ON event_locations(location_id);

-- App Settings Table: Store application configuration
CREATE TABLE IF NOT EXISTS app_settings (
    setting_key TEXT PRIMARY KEY,
    setting_value TEXT NOT NULL,
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TRIGGER IF NOT EXISTS settings_updated_at AFTER UPDATE ON app_settings BEGIN
    UPDATE app_settings SET updated_at = datetime('now') WHERE setting_key = NEW.setting_key;
END;

-- Schema Version Table: Track database migrations
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TEXT NOT NULL DEFAULT (datetime('now')),
    description TEXT
);

-- Insert initial schema version
INSERT OR IGNORE INTO schema_version (version, description)
VALUES (1, 'Initial schema creation');

-- Insert default settings
INSERT OR IGNORE INTO app_settings (setting_key, setting_value) VALUES
    ('theme', 'light'),
    ('default_zoom_level', 'month'),
    ('audio_quality', 'high'),
    ('llm_provider', 'anthropic'),
    ('llm_model', 'claude-sonnet-4-20250514'),
    ('llm_max_tokens', '4000'),
    ('llm_temperature', '0.3'),
    ('stt_engine', 'mock'),
    ('stt_config', '{}'),
    ('rag_auto_run_enabled', 'false'),
    ('rag_schedule', 'weekly'),
    ('rag_similarity_threshold', '0.75'),
    ('send_transcripts_only', 'true'),
    ('require_confirmation', 'true');
