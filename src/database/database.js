/**
 * Database Service Layer
 * Handles SQLite database initialization, migrations, and connections
 */

const Database = require('better-sqlite3');
const path = require('path');
const fs = require('fs');
const { app } = require('electron');

/**
 * Latest schema version. Must equal the highest `version` in MIGRATIONS below.
 */
const LATEST_VERSION = 3;

class DatabaseService {
    constructor() {
        this.db = null;
        this.dbPath = null;
    }

    /**
     * Initialize the database
     * @param {string} userDataPath - Path to store the database
     */
    initialize(userDataPath = null) {
        try {
            // Determine database path
            if (userDataPath) {
                this.dbPath = path.join(userDataPath, 'memory-timeline.db');
            } else {
                const userData = app.getPath('userData');
                this.dbPath = path.join(userData, 'memory-timeline.db');
            }

            // Ensure directory exists
            const dbDir = path.dirname(this.dbPath);
            if (!fs.existsSync(dbDir)) {
                fs.mkdirSync(dbDir, { recursive: true });
            }

            // Create or open database
            this.db = new Database(this.dbPath);

            // Resilience / durability pragmas.
            // WAL for concurrency; NORMAL synchronous is safe and fast with WAL.
            // busy_timeout avoids immediate SQLITE_BUSY errors under contention.
            this.db.pragma('journal_mode = WAL');
            this.db.pragma('synchronous = NORMAL');
            this.db.pragma('foreign_keys = ON');
            this.db.pragma('busy_timeout = 5000');
            // Checkpoint the WAL roughly every 1000 pages to keep the -wal file bounded.
            this.db.pragma('wal_autocheckpoint = 1000');

            console.log(`Database initialized at: ${this.dbPath}`);

            // Run versioned migrations
            this.runMigrations();

            return true;
        } catch (error) {
            console.error('Failed to initialize database:', error);
            throw error;
        }
    }

    /**
     * Ordered list of migration steps. Each step is applied only when the current
     * database `user_version` is lower than the step's `version`, in ascending
     * order, each inside its own transaction. `user_version` is bumped after each
     * successful step so partially-applied upgrades cannot be double-run.
     *
     * NOTE on fresh vs. existing databases:
     *  - Version 1 execs the full base schema.sql (all statements use
     *    IF NOT EXISTS / OR IGNORE, so it is idempotent). A brand-new DB is created
     *    here with the already-corrected trigger/constraint definitions.
     *  - Versions 2+ express incremental changes (trigger fixes, unique index,
     *    updated_at guard) using DROP+CREATE / CREATE ... IF NOT EXISTS so that
     *    databases created by the OLD code (which have the buggy triggers and no
     *    unique index, all at user_version 0) are brought up to date. These steps
     *    are also safe to re-apply on a fresh DB.
     *
     * @returns {Array<{version:number, description:string, up:(db:Database)=>void}>}
     */
    getMigrations() {
        const schemaPath = path.join(__dirname, 'schemas', 'schema.sql');

        return [
            {
                version: 1,
                description: 'Initial schema bootstrap',
                up: (db) => {
                    const schema = fs.readFileSync(schemaPath, 'utf8');
                    // Base schema contains no VACUUM / FTS 'rebuild', so it is safe
                    // to run inside the migration transaction.
                    db.exec(schema);
                }
            },
            {
                version: 2,
                description: 'Fix FTS5 external-content sync triggers (delete/insert command forms)',
                up: (db) => {
                    db.exec(`
                        DROP TRIGGER IF EXISTS events_fts_insert;
                        DROP TRIGGER IF EXISTS events_fts_delete;
                        DROP TRIGGER IF EXISTS events_fts_update;

                        CREATE TRIGGER events_fts_insert AFTER INSERT ON events BEGIN
                            INSERT INTO events_fts(rowid, title, description, raw_transcript)
                            VALUES (new.rowid, new.title, new.description, new.raw_transcript);
                        END;

                        CREATE TRIGGER events_fts_delete AFTER DELETE ON events BEGIN
                            INSERT INTO events_fts(events_fts, rowid, title, description, raw_transcript)
                            VALUES ('delete', old.rowid, old.title, old.description, old.raw_transcript);
                        END;

                        CREATE TRIGGER events_fts_update AFTER UPDATE ON events BEGIN
                            INSERT INTO events_fts(events_fts, rowid, title, description, raw_transcript)
                            VALUES ('delete', old.rowid, old.title, old.description, old.raw_transcript);
                            INSERT INTO events_fts(rowid, title, description, raw_transcript)
                            VALUES (new.rowid, new.title, new.description, new.raw_transcript);
                        END;
                    `);
                }
            },
            {
                version: 3,
                description: 'Add cross_references unique index and recursion-safe events_updated_at trigger',
                up: (db) => {
                    db.exec(`
                        CREATE UNIQUE INDEX IF NOT EXISTS idx_cross_refs_unique
                            ON cross_references(event_id_1, event_id_2, relationship_type);

                        DROP TRIGGER IF EXISTS events_updated_at;
                        CREATE TRIGGER events_updated_at AFTER UPDATE ON events
                        WHEN NEW.updated_at = OLD.updated_at BEGIN
                            UPDATE events SET updated_at = datetime('now') WHERE event_id = NEW.event_id;
                        END;
                    `);
                }
            }
        ];
    }

    /**
     * Run database migrations using PRAGMA user_version as the source of truth.
     * Applies only steps newer than the current version, each in its own
     * transaction, updating user_version after each.
     */
    runMigrations() {
        const migrations = this.getMigrations();
        let currentVersion = this.db.pragma('user_version', { simple: true });

        if (currentVersion >= LATEST_VERSION) {
            console.log(`Database schema up to date (version ${currentVersion})`);
            return;
        }

        for (const migration of migrations) {
            if (migration.version <= currentVersion) {
                continue;
            }

            // Each migration runs atomically. Setting user_version and recording the
            // audit row inside the same transaction guarantees they roll back together
            // if the migration body throws. (No VACUUM / FTS 'rebuild' here — those
            // must run outside a transaction and are handled separately in vacuum().)
            const applyStep = this.db.transaction(() => {
                migration.up(this.db);
                this.db.pragma(`user_version = ${migration.version}`);
                this.db.prepare(
                    'INSERT OR IGNORE INTO schema_version (version, description) VALUES (?, ?)'
                ).run(migration.version, migration.description);
            });

            applyStep();
            currentVersion = migration.version;
            console.log(`Applied migration ${migration.version}: ${migration.description}`);
        }

        console.log(`Database migrations completed (now at version ${currentVersion})`);
    }

    /**
     * Get the database instance
     * @returns {Database} SQLite database instance
     */
    getDatabase() {
        if (!this.db) {
            throw new Error('Database not initialized. Call initialize() first.');
        }
        return this.db;
    }

    /**
     * Close the database connection.
     * Checkpoints and truncates the WAL first so the -wal file does not grow
     * unbounded across restarts. Safe to call when the db is already null.
     */
    close() {
        if (!this.db) {
            return;
        }

        try {
            // TRUNCATE checkpoint flushes committed pages into the main db and
            // shrinks the -wal file back to zero.
            this.db.pragma('wal_checkpoint(TRUNCATE)');
        } catch (error) {
            console.error('WAL checkpoint on close failed (continuing to close):', error);
        }

        this.db.close();
        this.db = null;
        console.log('Database connection closed');
    }

    /**
     * Create a backup of the database.
     * better-sqlite3 v9 exposes a Promise-based db.backup(path); there is no
     * backup.step()/finish() (that was the node-sqlite3 API).
     * @param {string} backupPath - Path for the backup file
     * @returns {Promise<{success:boolean, path:string}>}
     */
    async backup(backupPath) {
        if (!this.db) {
            throw new Error('Database not initialized');
        }

        try {
            await this.db.backup(backupPath);
            console.log(`Database backed up to: ${backupPath}`);
            return { success: true, path: backupPath };
        } catch (error) {
            console.error('Failed to backup database:', error);
            throw error;
        }
    }

    /**
     * Get database statistics
     * @returns {Object} Database statistics
     */
    getStats() {
        if (!this.db) {
            throw new Error('Database not initialized');
        }

        const stats = {
            events: this.db.prepare('SELECT COUNT(*) as count FROM events').get().count,
            eras: this.db.prepare('SELECT COUNT(*) as count FROM eras').get().count,
            tags: this.db.prepare('SELECT COUNT(*) as count FROM tags').get().count,
            pendingEvents: this.db.prepare('SELECT COUNT(*) as count FROM pending_events WHERE status = ?').get('pending_review').count,
            queuedRecordings: this.db.prepare('SELECT COUNT(*) as count FROM recording_queue WHERE status = ?').get('pending').count,
            crossReferences: this.db.prepare('SELECT COUNT(*) as count FROM cross_references').get().count,
            dbSizeBytes: this.getDatabaseSizeBytes()
        };

        return stats;
    }

    /**
     * Total on-disk size of the database, including the -wal and -shm sidecar
     * files (which the plain main-file stat ignores while WAL mode is active).
     * @returns {number} Size in bytes
     */
    getDatabaseSizeBytes() {
        const files = [this.dbPath, `${this.dbPath}-wal`, `${this.dbPath}-shm`];
        let total = 0;

        for (const file of files) {
            try {
                total += fs.statSync(file).size;
            } catch (error) {
                // Sidecar files may not exist (e.g., after a checkpoint/close) — ignore.
                if (error.code !== 'ENOENT') {
                    throw error;
                }
            }
        }

        return total;
    }

    /**
     * Rebuild the FTS5 index from the external content table.
     * Must run OUTSIDE a transaction. Use after a VACUUM (which renumbers the
     * implicit rowids that events_fts is keyed on) or to repair a desynced index.
     */
    rebuildFtsIndex() {
        if (!this.db) {
            throw new Error('Database not initialized');
        }

        this.db.exec("INSERT INTO events_fts(events_fts) VALUES('rebuild');");
        console.log('FTS index rebuilt successfully');
    }

    /**
     * Vacuum the database to reclaim space.
     * VACUUM renumbers the implicit rowids of the `events` table (its primary key
     * is a TEXT column, so it uses an unaliased rowid), which breaks the
     * external-content FTS5 join. We therefore rebuild the FTS index immediately
     * afterwards. Both VACUUM and the 'rebuild' command must run outside any
     * transaction.
     */
    vacuum() {
        if (!this.db) {
            throw new Error('Database not initialized');
        }

        try {
            this.db.exec('VACUUM');
            this.rebuildFtsIndex();
            console.log('Database vacuumed successfully');
            return true;
        } catch (error) {
            console.error('Failed to vacuum database:', error);
            throw error;
        }
    }
}

// Export singleton instance
const databaseService = new DatabaseService();
module.exports = databaseService;
