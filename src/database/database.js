/**
 * Database Service Layer
 * Handles SQLite database initialization, migrations, and connections
 */

const Database = require('better-sqlite3');
const path = require('path');
const fs = require('fs');
const { app } = require('electron');

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

            // Enable foreign keys
            this.db.pragma('foreign_keys = ON');

            // Enable WAL mode for better concurrency
            this.db.pragma('journal_mode = WAL');

            console.log(`Database initialized at: ${this.dbPath}`);

            // Run migrations
            this.runMigrations();

            return true;
        } catch (error) {
            console.error('Failed to initialize database:', error);
            throw error;
        }
    }

    /**
     * Run database migrations
     */
    runMigrations() {
        try {
            // Read schema file
            const schemaPath = path.join(__dirname, 'schemas', 'schema.sql');
            const schema = fs.readFileSync(schemaPath, 'utf8');

            // Execute schema
            this.db.exec(schema);

            console.log('Database migrations completed successfully');
        } catch (error) {
            console.error('Failed to run migrations:', error);
            throw error;
        }
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
     * Close the database connection
     */
    close() {
        if (this.db) {
            this.db.close();
            this.db = null;
            console.log('Database connection closed');
        }
    }

    /**
     * Create a backup of the database
     * @param {string} backupPath - Path for the backup file
     */
    backup(backupPath) {
        if (!this.db) {
            throw new Error('Database not initialized');
        }

        try {
            const backup = this.db.backup(backupPath);
            backup.step(-1); // Copy entire database
            backup.finish();
            console.log(`Database backed up to: ${backupPath}`);
            return true;
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
            dbSizeBytes: fs.statSync(this.dbPath).size
        };

        return stats;
    }

    /**
     * Vacuum the database to reclaim space
     */
    vacuum() {
        if (!this.db) {
            throw new Error('Database not initialized');
        }

        try {
            this.db.exec('VACUUM');
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
