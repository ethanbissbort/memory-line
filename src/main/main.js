/**
 * Electron Main Process
 * Handles window creation, IPC communication, and system integration
 */

const { app, BrowserWindow, ipcMain, dialog } = require('electron');
const path = require('path');
const fs = require('fs');
const databaseService = require('../database/database');
const anthropicService = require('./services/anthropicService');
const sttService = require('./services/sttService');
const EmbeddingService = require('./services/embeddingService');
const RAGService = require('./services/ragService');
const ExportService = require('./services/exportService');
const PerformanceService = require('./services/performanceService');

let mainWindow = null;
let embeddingService = null;
let ragService = null;
let exportService = null;
let performanceService = null;

/**
 * Create the main application window
 */
function createMainWindow() {
    mainWindow = new BrowserWindow({
        width: 1400,
        height: 900,
        minWidth: 1000,
        minHeight: 600,
        backgroundColor: '#f5f5f5',
        webPreferences: {
            nodeIntegration: false,
            contextIsolation: true,
            preload: path.join(__dirname, 'preload.js')
        },
        show: false // Don't show until ready
    });

    // Load the app
    if (process.env.NODE_ENV === 'development') {
        mainWindow.loadURL('http://localhost:8080');
        mainWindow.webContents.openDevTools();
    } else {
        mainWindow.loadFile(path.join(__dirname, '../../dist/index.html'));
    }

    // Show window when ready
    mainWindow.once('ready-to-show', () => {
        mainWindow.show();
    });

    // Handle window close
    mainWindow.on('closed', () => {
        mainWindow = null;
    });
}

/**
 * Initialize the application
 */
function initializeApp() {
    try {
        // Initialize database
        databaseService.initialize();

        // Initialize services
        const db = databaseService.getDatabase();
        embeddingService = new EmbeddingService(db);
        ragService = new RAGService(db, embeddingService);
        exportService = new ExportService(db);
        performanceService = new PerformanceService(db);

        // Optimize database indices on startup
        performanceService.optimizeIndices();

        // Create assets directory if it doesn't exist
        const assetsPath = path.join(app.getPath('userData'), 'assets');
        const audioPath = path.join(assetsPath, 'audio');

        if (!fs.existsSync(audioPath)) {
            fs.mkdirSync(audioPath, { recursive: true });
        }

        console.log('Application initialized successfully');
    } catch (error) {
        console.error('Failed to initialize application:', error);
        dialog.showErrorBox('Initialization Error',
            'Failed to initialize the application. Please check the logs for details.');
        app.quit();
    }
}

// App event handlers
app.on('ready', () => {
    initializeApp();
    createMainWindow();
});

app.on('window-all-closed', () => {
    // On macOS, keep the app running even when all windows are closed
    if (process.platform !== 'darwin') {
        app.quit();
    }
});

app.on('activate', () => {
    // On macOS, recreate window when dock icon is clicked
    if (mainWindow === null) {
        createMainWindow();
    }
});

app.on('before-quit', () => {
    // Clean up before quitting
    databaseService.close();
});

// ===========================================
// IPC Handlers - Database Operations
// ===========================================

/**
 * Get database statistics
 */
ipcMain.handle('db:getStats', async () => {
    try {
        return {
            success: true,
            data: databaseService.getStats()
        };
    } catch (error) {
        console.error('Error getting database stats:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Create database backup
 */
ipcMain.handle('db:backup', async () => {
    try {
        const { filePath, canceled } = await dialog.showSaveDialog(mainWindow, {
            title: 'Save Database Backup',
            defaultPath: `memory-timeline-backup-${new Date().toISOString().split('T')[0]}.db`,
            filters: [
                { name: 'Database Files', extensions: ['db'] },
                { name: 'All Files', extensions: ['*'] }
            ]
        });

        if (canceled || !filePath) {
            return { success: false, canceled: true };
        }

        databaseService.backup(filePath);
        return { success: true, filePath };
    } catch (error) {
        console.error('Error backing up database:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Vacuum database
 */
ipcMain.handle('db:vacuum', async () => {
    try {
        databaseService.vacuum();
        return { success: true };
    } catch (error) {
        console.error('Error vacuuming database:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

// ===========================================
// IPC Handlers - Events
// ===========================================

/**
 * Get events within a date range
 */
ipcMain.handle('events:getRange', async (event, { startDate, endDate, limit = 1000 }) => {
    try {
        const db = databaseService.getDatabase();
        const stmt = db.prepare(`
            SELECT e.*,
                   GROUP_CONCAT(DISTINCT t.tag_name) as tags,
                   GROUP_CONCAT(DISTINCT p.name) as people,
                   GROUP_CONCAT(DISTINCT l.name) as locations,
                   era.name as era_name,
                   era.color_code as era_color
            FROM events e
            LEFT JOIN eras era ON e.era_id = era.era_id
            LEFT JOIN event_tags et ON e.event_id = et.event_id
            LEFT JOIN tags t ON et.tag_id = t.tag_id
            LEFT JOIN event_people ep ON e.event_id = ep.event_id
            LEFT JOIN people p ON ep.person_id = p.person_id
            LEFT JOIN event_locations el ON e.event_id = el.event_id
            LEFT JOIN locations l ON el.location_id = l.location_id
            WHERE e.start_date >= ? AND e.start_date <= ?
            GROUP BY e.event_id
            ORDER BY e.start_date ASC
            LIMIT ?
        `);

        const events = stmt.all(startDate, endDate, limit);

        // Parse JSON fields and split comma-separated values
        events.forEach(event => {
            if (event.extraction_metadata) {
                event.extraction_metadata = JSON.parse(event.extraction_metadata);
            }
            event.tags = event.tags ? event.tags.split(',') : [];
            event.people = event.people ? event.people.split(',') : [];
            event.locations = event.locations ? event.locations.split(',') : [];
        });

        return {
            success: true,
            data: events
        };
    } catch (error) {
        console.error('Error getting events:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Get a single event by ID
 */
ipcMain.handle('events:getById', async (event, eventId) => {
    try {
        const db = databaseService.getDatabase();
        const stmt = db.prepare(`
            SELECT e.*,
                   GROUP_CONCAT(DISTINCT t.tag_name) as tags,
                   GROUP_CONCAT(DISTINCT p.name) as people,
                   GROUP_CONCAT(DISTINCT l.name) as locations,
                   era.name as era_name,
                   era.color_code as era_color
            FROM events e
            LEFT JOIN eras era ON e.era_id = era.era_id
            LEFT JOIN event_tags et ON e.event_id = et.event_id
            LEFT JOIN tags t ON et.tag_id = t.tag_id
            LEFT JOIN event_people ep ON e.event_id = ep.event_id
            LEFT JOIN people p ON ep.person_id = p.person_id
            LEFT JOIN event_locations el ON e.event_id = el.event_id
            LEFT JOIN locations l ON el.location_id = l.location_id
            WHERE e.event_id = ?
            GROUP BY e.event_id
        `);

        const eventData = stmt.get(eventId);

        if (!eventData) {
            return {
                success: false,
                error: 'Event not found'
            };
        }

        // Parse JSON fields and split comma-separated values
        if (eventData.extraction_metadata) {
            eventData.extraction_metadata = JSON.parse(eventData.extraction_metadata);
        }
        eventData.tags = eventData.tags ? eventData.tags.split(',') : [];
        eventData.people = eventData.people ? eventData.people.split(',') : [];
        eventData.locations = eventData.locations ? eventData.locations.split(',') : [];

        return {
            success: true,
            data: eventData
        };
    } catch (error) {
        console.error('Error getting event:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Create a new event
 */
ipcMain.handle('events:create', async (event, eventData) => {
    try {
        const db = databaseService.getDatabase();
        const { v4: uuidv4 } = require('uuid');

        const eventId = uuidv4();
        const {
            title,
            start_date,
            end_date,
            description,
            category,
            era_id,
            audio_file_path,
            raw_transcript,
            extraction_metadata,
            tags = [],
            people = [],
            locations = []
        } = eventData;

        // Begin transaction
        const transaction = db.transaction(() => {
            // Insert event
            const insertEvent = db.prepare(`
                INSERT INTO events (
                    event_id, title, start_date, end_date, description,
                    category, era_id, audio_file_path, raw_transcript, extraction_metadata
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            `);

            insertEvent.run(
                eventId,
                title,
                start_date,
                end_date || null,
                description || null,
                category || 'other',
                era_id || null,
                audio_file_path || null,
                raw_transcript || null,
                extraction_metadata ? JSON.stringify(extraction_metadata) : null
            );

            // Insert tags
            if (tags.length > 0) {
                const insertTag = db.prepare(`
                    INSERT OR IGNORE INTO tags (tag_id, tag_name) VALUES (?, ?)
                `);
                const linkTag = db.prepare(`
                    INSERT INTO event_tags (event_id, tag_id, is_manual) VALUES (?, ?, 1)
                `);

                tags.forEach(tagName => {
                    const tagId = uuidv4();
                    insertTag.run(tagId, tagName);
                    const existingTag = db.prepare('SELECT tag_id FROM tags WHERE tag_name = ?').get(tagName);
                    linkTag.run(eventId, existingTag.tag_id);
                });
            }

            // Insert people
            if (people.length > 0) {
                const insertPerson = db.prepare(`
                    INSERT OR IGNORE INTO people (person_id, name) VALUES (?, ?)
                `);
                const linkPerson = db.prepare(`
                    INSERT INTO event_people (event_id, person_id) VALUES (?, ?)
                `);

                people.forEach(personName => {
                    const personId = uuidv4();
                    insertPerson.run(personId, personName);
                    const existingPerson = db.prepare('SELECT person_id FROM people WHERE name = ?').get(personName);
                    linkPerson.run(eventId, existingPerson.person_id);
                });
            }

            // Insert locations
            if (locations.length > 0) {
                const insertLocation = db.prepare(`
                    INSERT OR IGNORE INTO locations (location_id, name) VALUES (?, ?)
                `);
                const linkLocation = db.prepare(`
                    INSERT INTO event_locations (event_id, location_id) VALUES (?, ?)
                `);

                locations.forEach(locationName => {
                    const locationId = uuidv4();
                    insertLocation.run(locationId, locationName);
                    const existingLocation = db.prepare('SELECT location_id FROM locations WHERE name = ?').get(locationName);
                    linkLocation.run(eventId, existingLocation.location_id);
                });
            }
        });

        // Execute transaction
        transaction();

        return {
            success: true,
            data: { event_id: eventId }
        };
    } catch (error) {
        console.error('Error creating event:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Update an existing event
 */
ipcMain.handle('events:update', async (event, { eventId, updates }) => {
    try {
        const db = databaseService.getDatabase();

        // Build UPDATE query dynamically based on provided fields
        const allowedFields = [
            'title', 'start_date', 'end_date', 'description',
            'category', 'era_id', 'audio_file_path', 'raw_transcript'
        ];

        const updateFields = [];
        const values = [];

        Object.keys(updates).forEach(key => {
            if (allowedFields.includes(key)) {
                updateFields.push(`${key} = ?`);
                values.push(updates[key]);
            }
        });

        if (updateFields.length === 0) {
            return {
                success: false,
                error: 'No valid fields to update'
            };
        }

        values.push(eventId);

        const stmt = db.prepare(`
            UPDATE events
            SET ${updateFields.join(', ')}
            WHERE event_id = ?
        `);

        stmt.run(...values);

        return { success: true };
    } catch (error) {
        console.error('Error updating event:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Delete an event
 */
ipcMain.handle('events:delete', async (event, eventId) => {
    try {
        const db = databaseService.getDatabase();
        const stmt = db.prepare('DELETE FROM events WHERE event_id = ?');
        stmt.run(eventId);

        return { success: true };
    } catch (error) {
        console.error('Error deleting event:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Search events using full-text search
 */
ipcMain.handle('events:search', async (event, { query, limit = 50 }) => {
    try {
        const db = databaseService.getDatabase();
        const stmt = db.prepare(`
            SELECT e.*,
                   era.name as era_name,
                   era.color_code as era_color
            FROM events_fts fts
            JOIN events e ON fts.rowid = e.rowid
            LEFT JOIN eras era ON e.era_id = era.era_id
            WHERE events_fts MATCH ?
            ORDER BY rank
            LIMIT ?
        `);

        const events = stmt.all(query, limit);

        return {
            success: true,
            data: events
        };
    } catch (error) {
        console.error('Error searching events:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

// ===========================================
// IPC Handlers - Eras
// ===========================================

/**
 * Get all eras
 */
ipcMain.handle('eras:getAll', async () => {
    try {
        const db = databaseService.getDatabase();
        const stmt = db.prepare('SELECT * FROM eras ORDER BY start_date ASC');
        const eras = stmt.all();

        return {
            success: true,
            data: eras
        };
    } catch (error) {
        console.error('Error getting eras:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Create a new era
 */
ipcMain.handle('eras:create', async (event, eraData) => {
    try {
        const db = databaseService.getDatabase();
        const { v4: uuidv4 } = require('uuid');

        const eraId = uuidv4();
        const { name, start_date, end_date, color_code, description } = eraData;

        const stmt = db.prepare(`
            INSERT INTO eras (era_id, name, start_date, end_date, color_code, description)
            VALUES (?, ?, ?, ?, ?, ?)
        `);

        stmt.run(eraId, name, start_date, end_date || null, color_code, description || null);

        return {
            success: true,
            data: { era_id: eraId }
        };
    } catch (error) {
        console.error('Error creating era:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Update an era
 */
ipcMain.handle('eras:update', async (event, { eraId, updates }) => {
    try {
        const db = databaseService.getDatabase();

        const allowedFields = ['name', 'start_date', 'end_date', 'color_code', 'description'];
        const updateFields = [];
        const values = [];

        Object.keys(updates).forEach(key => {
            if (allowedFields.includes(key)) {
                updateFields.push(`${key} = ?`);
                values.push(updates[key]);
            }
        });

        if (updateFields.length === 0) {
            return {
                success: false,
                error: 'No valid fields to update'
            };
        }

        values.push(eraId);

        const stmt = db.prepare(`
            UPDATE eras
            SET ${updateFields.join(', ')}
            WHERE era_id = ?
        `);

        stmt.run(...values);

        return { success: true };
    } catch (error) {
        console.error('Error updating era:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Delete an era
 */
ipcMain.handle('eras:delete', async (event, eraId) => {
    try {
        const db = databaseService.getDatabase();
        const stmt = db.prepare('DELETE FROM eras WHERE era_id = ?');
        stmt.run(eraId);

        return { success: true };
    } catch (error) {
        console.error('Error deleting era:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

// ===========================================
// IPC Handlers - Settings
// ===========================================

/**
 * Get all settings
 */
ipcMain.handle('settings:getAll', async () => {
    try {
        const db = databaseService.getDatabase();
        const stmt = db.prepare('SELECT setting_key, setting_value FROM app_settings');
        const rows = stmt.all();

        const settings = {};
        rows.forEach(row => {
            settings[row.setting_key] = row.setting_value;
        });

        return {
            success: true,
            data: settings
        };
    } catch (error) {
        console.error('Error getting settings:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Update a setting
 */
ipcMain.handle('settings:update', async (event, { key, value }) => {
    try {
        const db = databaseService.getDatabase();
        const stmt = db.prepare(`
            INSERT INTO app_settings (setting_key, setting_value)
            VALUES (?, ?)
            ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value
        `);

        stmt.run(key, value);

        return { success: true };
    } catch (error) {
        console.error('Error updating setting:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

// ===========================================
// IPC Handlers - Audio & Recording Queue
// ===========================================

/**
 * Save audio blob to file system
 */
ipcMain.handle('audio:save', async (event, { audioData, duration }) => {
    try {
        const { v4: uuidv4 } = require('uuid');

        // Generate unique filename
        const audioId = uuidv4();
        const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
        const filename = `recording-${timestamp}-${audioId}.wav`;

        // Get audio directory path
        const audioPath = path.join(app.getPath('userData'), 'assets', 'audio');

        // Ensure directory exists
        if (!fs.existsSync(audioPath)) {
            fs.mkdirSync(audioPath, { recursive: true });
        }

        const filePath = path.join(audioPath, filename);

        // Convert base64 to buffer and save
        const buffer = Buffer.from(audioData.split(',')[1], 'base64');
        fs.writeFileSync(filePath, buffer);

        const stats = fs.statSync(filePath);

        return {
            success: true,
            data: {
                filePath: filePath,
                filename: filename,
                fileSize: stats.size,
                duration: duration
            }
        };
    } catch (error) {
        console.error('Error saving audio file:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Add audio file to recording queue
 */
ipcMain.handle('queue:add', async (event, { filePath, duration, fileSize }) => {
    try {
        const db = databaseService.getDatabase();
        const { v4: uuidv4 } = require('uuid');

        const queueId = uuidv4();

        const stmt = db.prepare(`
            INSERT INTO recording_queue (
                queue_id, audio_file_path, duration_seconds, file_size_bytes, status
            ) VALUES (?, ?, ?, ?, 'pending')
        `);

        stmt.run(queueId, filePath, duration, fileSize);

        return {
            success: true,
            data: { queue_id: queueId }
        };
    } catch (error) {
        console.error('Error adding to queue:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Get recording queue items
 */
ipcMain.handle('queue:getAll', async (event, { status = null } = {}) => {
    try {
        const db = databaseService.getDatabase();

        let query = 'SELECT * FROM recording_queue';
        const params = [];

        if (status) {
            query += ' WHERE status = ?';
            params.push(status);
        }

        query += ' ORDER BY created_at DESC';

        const stmt = db.prepare(query);
        const items = stmt.all(...params);

        return {
            success: true,
            data: items
        };
    } catch (error) {
        console.error('Error getting queue items:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Update queue item status
 */
ipcMain.handle('queue:updateStatus', async (event, { queueId, status, errorMessage = null }) => {
    try {
        const db = databaseService.getDatabase();

        const stmt = db.prepare(`
            UPDATE recording_queue
            SET status = ?,
                processed_at = CASE WHEN ? IN ('completed', 'failed') THEN datetime('now') ELSE processed_at END,
                error_message = ?
            WHERE queue_id = ?
        `);

        stmt.run(status, status, errorMessage, queueId);

        return { success: true };
    } catch (error) {
        console.error('Error updating queue status:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Remove item from queue
 */
ipcMain.handle('queue:remove', async (event, queueId) => {
    try {
        const db = databaseService.getDatabase();

        // Get file path before deleting from database
        const getStmt = db.prepare('SELECT audio_file_path FROM recording_queue WHERE queue_id = ?');
        const item = getStmt.get(queueId);

        if (item && item.audio_file_path) {
            // Delete audio file if it exists
            if (fs.existsSync(item.audio_file_path)) {
                fs.unlinkSync(item.audio_file_path);
            }
        }

        // Delete from database
        const deleteStmt = db.prepare('DELETE FROM recording_queue WHERE queue_id = ?');
        deleteStmt.run(queueId);

        return { success: true };
    } catch (error) {
        console.error('Error removing from queue:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Get pending events for review
 */
ipcMain.handle('pending:getAll', async (event, { status = 'pending_review' } = {}) => {
    try {
        const db = databaseService.getDatabase();

        const stmt = db.prepare(`
            SELECT * FROM pending_events
            WHERE status = ?
            ORDER BY created_at DESC
        `);

        const items = stmt.all(status);

        // Parse extracted_data JSON
        items.forEach(item => {
            if (item.extracted_data) {
                item.extracted_data = JSON.parse(item.extracted_data);
            }
        });

        return {
            success: true,
            data: items
        };
    } catch (error) {
        console.error('Error getting pending events:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Approve pending event and create actual event
 */
ipcMain.handle('pending:approve', async (event, { pendingId, editedData = null }) => {
    try {
        const db = databaseService.getDatabase();
        const { v4: uuidv4 } = require('uuid');

        // Get pending event
        const getPending = db.prepare('SELECT * FROM pending_events WHERE pending_id = ?');
        const pendingEvent = getPending.get(pendingId);

        if (!pendingEvent) {
            return {
                success: false,
                error: 'Pending event not found'
            };
        }

        // Use edited data if provided, otherwise use extracted data
        const eventData = editedData || JSON.parse(pendingEvent.extracted_data);

        // Create the event
        const eventId = uuidv4();

        const transaction = db.transaction(() => {
            // Insert event
            const insertEvent = db.prepare(`
                INSERT INTO events (
                    event_id, title, start_date, end_date, description,
                    category, audio_file_path, raw_transcript, extraction_metadata
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            `);

            insertEvent.run(
                eventId,
                eventData.title,
                eventData.start_date,
                eventData.end_date || null,
                eventData.description || null,
                eventData.category || 'other',
                pendingEvent.audio_file_path,
                pendingEvent.transcript,
                JSON.stringify({ confidence: eventData.confidence || 1.0 })
            );

            // Insert tags if provided
            if (eventData.suggested_tags && eventData.suggested_tags.length > 0) {
                const insertTag = db.prepare(`
                    INSERT OR IGNORE INTO tags (tag_id, tag_name) VALUES (?, ?)
                `);
                const linkTag = db.prepare(`
                    INSERT INTO event_tags (event_id, tag_id, is_manual, confidence_score)
                    VALUES (?, ?, 0, ?)
                `);

                eventData.suggested_tags.forEach(tagName => {
                    const tagId = uuidv4();
                    insertTag.run(tagId, tagName);
                    const existingTag = db.prepare('SELECT tag_id FROM tags WHERE tag_name = ?').get(tagName);
                    linkTag.run(eventId, existingTag.tag_id, eventData.confidence || 0.8);
                });
            }

            // Insert people if provided
            if (eventData.key_people && eventData.key_people.length > 0) {
                const insertPerson = db.prepare(`
                    INSERT OR IGNORE INTO people (person_id, name) VALUES (?, ?)
                `);
                const linkPerson = db.prepare(`
                    INSERT INTO event_people (event_id, person_id) VALUES (?, ?)
                `);

                eventData.key_people.forEach(personName => {
                    const personId = uuidv4();
                    insertPerson.run(personId, personName);
                    const existingPerson = db.prepare('SELECT person_id FROM people WHERE name = ?').get(personName);
                    linkPerson.run(eventId, existingPerson.person_id);
                });
            }

            // Insert locations if provided
            if (eventData.locations && eventData.locations.length > 0) {
                const insertLocation = db.prepare(`
                    INSERT OR IGNORE INTO locations (location_id, name) VALUES (?, ?)
                `);
                const linkLocation = db.prepare(`
                    INSERT INTO event_locations (event_id, location_id) VALUES (?, ?)
                `);

                eventData.locations.forEach(locationName => {
                    const locationId = uuidv4();
                    insertLocation.run(locationId, locationName);
                    const existingLocation = db.prepare('SELECT location_id FROM locations WHERE name = ?').get(locationName);
                    linkLocation.run(eventId, existingLocation.location_id);
                });
            }

            // Update pending event status
            const updatePending = db.prepare(`
                UPDATE pending_events
                SET status = 'approved', reviewed_at = datetime('now')
                WHERE pending_id = ?
            `);
            updatePending.run(pendingId);

            // Update queue status
            if (pendingEvent.queue_id) {
                const updateQueue = db.prepare(`
                    UPDATE recording_queue
                    SET status = 'completed'
                    WHERE queue_id = ?
                `);
                updateQueue.run(pendingEvent.queue_id);
            }
        });

        transaction();

        return {
            success: true,
            data: { event_id: eventId }
        };
    } catch (error) {
        console.error('Error approving pending event:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Reject pending event
 */
ipcMain.handle('pending:reject', async (event, pendingId) => {
    try {
        const db = databaseService.getDatabase();

        // Get pending event
        const getPending = db.prepare('SELECT queue_id FROM pending_events WHERE pending_id = ?');
        const pendingEvent = getPending.get(pendingId);

        const transaction = db.transaction(() => {
            // Update pending event status
            const updatePending = db.prepare(`
                UPDATE pending_events
                SET status = 'rejected', reviewed_at = datetime('now')
                WHERE pending_id = ?
            `);
            updatePending.run(pendingId);

            // Update queue status to failed
            if (pendingEvent && pendingEvent.queue_id) {
                const updateQueue = db.prepare(`
                    UPDATE recording_queue
                    SET status = 'failed', error_message = 'Rejected by user'
                    WHERE queue_id = ?
                `);
                updateQueue.run(pendingEvent.queue_id);
            }
        });

        transaction();

        return { success: true };
    } catch (error) {
        console.error('Error rejecting pending event:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Get audio file as data URL for playback
 */
ipcMain.handle('audio:getFile', async (event, filePath) => {
    try {
        if (!fs.existsSync(filePath)) {
            return {
                success: false,
                error: 'Audio file not found'
            };
        }

        const buffer = fs.readFileSync(filePath);
        const base64 = buffer.toString('base64');
        const dataUrl = `data:audio/wav;base64,${base64}`;

        return {
            success: true,
            data: dataUrl
        };
    } catch (error) {
        console.error('Error reading audio file:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

// ===========================================
// IPC Handlers - LLM & Processing
// ===========================================

/**
 * Set Anthropic API key
 */
ipcMain.handle('llm:setApiKey', async (event, apiKey) => {
    try {
        // Store API key in database (encrypted in production)
        const db = databaseService.getDatabase();
        const stmt = db.prepare(`
            INSERT INTO app_settings (setting_key, setting_value)
            VALUES ('anthropic_api_key', ?)
            ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value
        `);

        stmt.run(apiKey);

        // Initialize Anthropic service
        anthropicService.initialize(apiKey);

        return { success: true };
    } catch (error) {
        console.error('Error setting API key:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Check if API key is set
 */
ipcMain.handle('llm:hasApiKey', async () => {
    try {
        const db = databaseService.getDatabase();
        const stmt = db.prepare('SELECT setting_value FROM app_settings WHERE setting_key = ?');
        const result = stmt.get('anthropic_api_key');

        return {
            success: true,
            hasKey: !!result && !!result.setting_value
        };
    } catch (error) {
        return {
            success: false,
            hasKey: false,
            error: error.message
        };
    }
});

/**
 * Initialize Anthropic service with stored API key
 */
async function initializeAnthropicService() {
    try {
        const db = databaseService.getDatabase();
        const stmt = db.prepare('SELECT setting_value FROM app_settings WHERE setting_key = ?');
        const result = stmt.get('anthropic_api_key');

        if (result && result.setting_value) {
            anthropicService.initialize(result.setting_value);
            console.log('Anthropic service initialized from stored API key');
        }
    } catch (error) {
        console.error('Failed to initialize Anthropic service:', error);
    }
}

/**
 * Process a single queue item
 */
ipcMain.handle('llm:processQueueItem', async (event, queueId) => {
    try {
        if (!anthropicService.isInitialized()) {
            return {
                success: false,
                error: 'API key not set. Please configure Anthropic API key in Settings.'
            };
        }

        const db = databaseService.getDatabase();
        const { v4: uuidv4 } = require('uuid');

        // Get queue item
        const getQueue = db.prepare('SELECT * FROM recording_queue WHERE queue_id = ?');
        const queueItem = getQueue.get(queueId);

        if (!queueItem) {
            return {
                success: false,
                error: 'Queue item not found'
            };
        }

        if (queueItem.status !== 'pending') {
            return {
                success: false,
                error: `Queue item status is ${queueItem.status}, expected pending`
            };
        }

        // Update status to processing
        const updateStatus = db.prepare(`
            UPDATE recording_queue
            SET status = 'processing'
            WHERE queue_id = ?
        `);
        updateStatus.run(queueId);

        // Process the audio file
        const result = await anthropicService.processAudioFile(queueItem.audio_file_path, queueId);

        if (!result.success) {
            // Update queue status to failed
            const updateFailed = db.prepare(`
                UPDATE recording_queue
                SET status = 'failed', error_message = ?, processed_at = datetime('now')
                WHERE queue_id = ?
            `);
            updateFailed.run(result.error, queueId);

            return {
                success: false,
                error: result.error
            };
        }

        // Create pending event
        const pendingId = uuidv4();
        const insertPending = db.prepare(`
            INSERT INTO pending_events (
                pending_id, extracted_data, audio_file_path, transcript, queue_id, status
            ) VALUES (?, ?, ?, ?, ?, 'pending_review')
        `);

        insertPending.run(
            pendingId,
            JSON.stringify(result.extractedData),
            queueItem.audio_file_path,
            result.transcript,
            queueId
        );

        // Update queue status to completed (will be marked completed when user approves)
        const updateCompleted = db.prepare(`
            UPDATE recording_queue
            SET status = 'completed', processed_at = datetime('now')
            WHERE queue_id = ?
        `);
        updateCompleted.run(queueId);

        return {
            success: true,
            data: {
                pending_id: pendingId,
                extracted_data: result.extractedData
            }
        };
    } catch (error) {
        console.error('Error processing queue item:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Process all pending queue items
 */
ipcMain.handle('llm:processAllPending', async () => {
    try {
        if (!anthropicService.isInitialized()) {
            return {
                success: false,
                error: 'API key not set. Please configure Anthropic API key in Settings.'
            };
        }

        const db = databaseService.getDatabase();

        // Get all pending items
        const getPending = db.prepare('SELECT queue_id FROM recording_queue WHERE status = ?');
        const pendingItems = getPending.all('pending');

        if (pendingItems.length === 0) {
            return {
                success: true,
                message: 'No pending items to process',
                processed: 0
            };
        }

        const results = {
            total: pendingItems.length,
            succeeded: 0,
            failed: 0,
            errors: []
        };

        // Process each item
        for (const item of pendingItems) {
            try {
                const result = await anthropicService.processAudioFile(item.audio_file_path, item.queue_id);

                if (result.success) {
                    // Create pending event
                    const { v4: uuidv4 } = require('uuid');
                    const pendingId = uuidv4();

                    const insertPending = db.prepare(`
                        INSERT INTO pending_events (
                            pending_id, extracted_data, audio_file_path, transcript, queue_id, status
                        ) VALUES (?, ?, ?, ?, ?, 'pending_review')
                    `);

                    const queueItem = db.prepare('SELECT * FROM recording_queue WHERE queue_id = ?').get(item.queue_id);

                    insertPending.run(
                        pendingId,
                        JSON.stringify(result.extractedData),
                        queueItem.audio_file_path,
                        result.transcript,
                        item.queue_id
                    );

                    // Update queue status
                    const updateCompleted = db.prepare(`
                        UPDATE recording_queue
                        SET status = 'completed', processed_at = datetime('now')
                        WHERE queue_id = ?
                    `);
                    updateCompleted.run(item.queue_id);

                    results.succeeded++;
                } else {
                    // Update queue status to failed
                    const updateFailed = db.prepare(`
                        UPDATE recording_queue
                        SET status = 'failed', error_message = ?, processed_at = datetime('now')
                        WHERE queue_id = ?
                    `);
                    updateFailed.run(result.error, item.queue_id);

                    results.failed++;
                    results.errors.push({
                        queueId: item.queue_id,
                        error: result.error
                    });
                }
            } catch (error) {
                console.error(`Error processing queue item ${item.queue_id}:`, error);

                // Update queue status to failed with error message
                try {
                    const updateFailed = db.prepare(`
                        UPDATE recording_queue
                        SET status = 'failed', error_message = ?, processed_at = datetime('now')
                        WHERE queue_id = ?
                    `);
                    updateFailed.run(error.message || 'Unknown error', item.queue_id);
                } catch (dbError) {
                    console.error('Failed to update queue item status:', dbError);
                }

                results.failed++;
                results.errors.push({
                    queueId: item.queue_id,
                    error: error.message || 'Unknown error',
                    timestamp: new Date().toISOString()
                });
            }
        }

        return {
            success: true,
            results: results
        };
    } catch (error) {
        console.error('Error processing all pending items:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

// ===========================================
// IPC Handlers - STT (Speech-to-Text)
// ===========================================

/**
 * Get list of available STT engines
 */
ipcMain.handle('stt:getAvailableEngines', async () => {
    try {
        const engines = sttService.constructor.getAvailableEngines();
        return {
            success: true,
            engines: engines
        };
    } catch (error) {
        console.error('Error getting STT engines:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Initialize STT engine with config
 */
ipcMain.handle('stt:initializeEngine', async (event, { engine, config }) => {
    try {
        const db = databaseService.getDatabase();

        // Save STT settings to database
        const updateEngine = db.prepare(`
            INSERT INTO app_settings (setting_key, setting_value)
            VALUES ('stt_engine', ?)
            ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value
        `);
        updateEngine.run(engine);

        const updateConfig = db.prepare(`
            INSERT INTO app_settings (setting_key, setting_value)
            VALUES ('stt_config', ?)
            ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value
        `);
        updateConfig.run(JSON.stringify(config));

        // Initialize STT service
        await anthropicService.initializeSTT(engine, config);

        return { success: true };
    } catch (error) {
        console.error('Error initializing STT engine:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Initialize STT service with stored settings
 */
async function initializeSTTService() {
    try {
        const db = databaseService.getDatabase();

        // Get STT settings
        const getEngine = db.prepare('SELECT setting_value FROM app_settings WHERE setting_key = ?');
        const engineResult = getEngine.get('stt_engine');
        const configResult = getEngine.get('stt_config');

        if (engineResult && engineResult.setting_value) {
            const engine = engineResult.setting_value;
            const config = configResult ? JSON.parse(configResult.setting_value) : {};

            await anthropicService.initializeSTT(engine, config);
            console.log(`STT service initialized with engine: ${engine}`);
        }
    } catch (error) {
        console.error('Failed to initialize STT service:', error);
    }
}

// ===========================================
// IPC Handlers - Embeddings
// ===========================================

/**
 * Initialize embedding service with provider config
 */
ipcMain.handle('embedding:initialize', async (event, { provider, model, apiKey }) => {
    try {
        await embeddingService.initialize(provider, model, apiKey);

        // Save settings to database
        const db = databaseService.getDatabase();
        const updateSetting = db.prepare(`
            INSERT INTO app_settings (setting_key, setting_value)
            VALUES (?, ?)
            ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value
        `);

        updateSetting.run('embedding_provider', provider);
        updateSetting.run('embedding_model', model);
        if (apiKey) {
            updateSetting.run('embedding_api_key', apiKey);
        }

        return { success: true };
    } catch (error) {
        console.error('Error initializing embedding service:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Generate embedding for a specific event
 */
ipcMain.handle('embedding:generateForEvent', async (event, eventId) => {
    try {
        const db = databaseService.getDatabase();
        const getEvent = db.prepare('SELECT * FROM events WHERE event_id = ?');
        const eventData = getEvent.get(eventId);

        if (!eventData) {
            return {
                success: false,
                error: 'Event not found'
            };
        }

        const result = await embeddingService.generateEventEmbedding(eventId, eventData);
        return result;
    } catch (error) {
        console.error('Error generating event embedding:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Generate embeddings for all events without embeddings
 */
ipcMain.handle('embedding:generateAll', async () => {
    try {
        const result = await embeddingService.generateAllMissingEmbeddings();
        return result;
    } catch (error) {
        console.error('Error generating all embeddings:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Find similar events to a given event
 */
ipcMain.handle('embedding:findSimilar', async (event, { eventId, threshold = 0.75, limit = 10 }) => {
    try {
        const result = await embeddingService.findSimilarEvents(eventId, threshold, limit);
        return result;
    } catch (error) {
        console.error('Error finding similar events:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Clear all embeddings
 */
ipcMain.handle('embedding:clearAll', async () => {
    try {
        embeddingService.clearAllEmbeddings();
        return { success: true };
    } catch (error) {
        console.error('Error clearing embeddings:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

// ===========================================
// IPC Handlers - RAG Cross-Referencing
// ===========================================

/**
 * Analyze cross-references for a specific event
 */
ipcMain.handle('rag:analyzeEvent', async (event, { eventId, threshold = 0.75 }) => {
    try {
        const result = await ragService.analyzeEventCrossReferences(eventId, threshold);
        return result;
    } catch (error) {
        console.error('Error analyzing event:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Analyze entire timeline for cross-references
 */
ipcMain.handle('rag:analyzeTimeline', async (event, { threshold = 0.75 }) => {
    try {
        const result = await ragService.analyzeFullTimeline(threshold);
        return result;
    } catch (error) {
        console.error('Error analyzing timeline:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Get cross-references for an event
 */
ipcMain.handle('rag:getCrossReferences', async (event, eventId) => {
    try {
        const crossRefs = ragService.getCrossReferences(eventId);
        return {
            success: true,
            data: crossRefs
        };
    } catch (error) {
        console.error('Error getting cross-references:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Detect patterns in timeline
 */
ipcMain.handle('rag:detectPatterns', async () => {
    try {
        const result = await ragService.detectPatterns();
        return result;
    } catch (error) {
        console.error('Error detecting patterns:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Suggest tags for an event
 */
ipcMain.handle('rag:suggestTags', async (event, { eventId, limit = 5 }) => {
    try {
        const result = await ragService.suggestTags(eventId, limit);
        return result;
    } catch (error) {
        console.error('Error suggesting tags:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Initialize embedding service with stored settings
 */
async function initializeEmbeddingService() {
    try {
        const db = databaseService.getDatabase();
        const getSetting = db.prepare('SELECT setting_value FROM app_settings WHERE setting_key = ?');

        const providerResult = getSetting.get('embedding_provider');
        const modelResult = getSetting.get('embedding_model');
        const apiKeyResult = getSetting.get('embedding_api_key');

        if (providerResult && providerResult.setting_value && modelResult && modelResult.setting_value) {
            const provider = providerResult.setting_value;
            const model = modelResult.setting_value;
            const apiKey = apiKeyResult ? apiKeyResult.setting_value : null;

            await embeddingService.initialize(provider, model, apiKey);
            console.log(`Embedding service initialized with ${provider}/${model}`);
        }
    } catch (error) {
        console.error('Failed to initialize embedding service:', error);
    }
}

// ===========================================
// IPC Handlers - Export & Performance
// ===========================================

/**
 * Export timeline to JSON
 */
ipcMain.handle('export:toJSON', async () => {
    try {
        const { filePath, canceled } = await dialog.showSaveDialog(mainWindow, {
            title: 'Export Timeline to JSON',
            defaultPath: `memory-timeline-${new Date().toISOString().split('T')[0]}.json`,
            filters: [
                { name: 'JSON Files', extensions: ['json'] },
                { name: 'All Files', extensions: ['*'] }
            ]
        });

        if (canceled || !filePath) {
            return { success: false, canceled: true };
        }

        const result = exportService.exportToJSON(filePath);
        return result;
    } catch (error) {
        console.error('Error exporting to JSON:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Export timeline to CSV
 */
ipcMain.handle('export:toCSV', async () => {
    try {
        const { filePath, canceled } = await dialog.showSaveDialog(mainWindow, {
            title: 'Export Timeline to CSV',
            defaultPath: `memory-timeline-${new Date().toISOString().split('T')[0]}.csv`,
            filters: [
                { name: 'CSV Files', extensions: ['csv'] },
                { name: 'All Files', extensions: ['*'] }
            ]
        });

        if (canceled || !filePath) {
            return { success: false, canceled: true };
        }

        const result = exportService.exportToCSV(filePath);
        return result;
    } catch (error) {
        console.error('Error exporting to CSV:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Export timeline to Markdown
 */
ipcMain.handle('export:toMarkdown', async () => {
    try {
        const { filePath, canceled } = await dialog.showSaveDialog(mainWindow, {
            title: 'Export Timeline to Markdown',
            defaultPath: `memory-timeline-${new Date().toISOString().split('T')[0]}.md`,
            filters: [
                { name: 'Markdown Files', extensions: ['md'] },
                { name: 'All Files', extensions: ['*'] }
            ]
        });

        if (canceled || !filePath) {
            return { success: false, canceled: true };
        }

        const result = exportService.exportToMarkdown(filePath);
        return result;
    } catch (error) {
        console.error('Error exporting to Markdown:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Import timeline from JSON
 */
ipcMain.handle('import:fromJSON', async () => {
    try {
        const { filePaths, canceled } = await dialog.showOpenDialog(mainWindow, {
            title: 'Import Timeline from JSON',
            filters: [
                { name: 'JSON Files', extensions: ['json'] },
                { name: 'All Files', extensions: ['*'] }
            ],
            properties: ['openFile']
        });

        if (canceled || !filePaths || filePaths.length === 0) {
            return { success: false, canceled: true };
        }

        const result = exportService.importFromJSON(filePaths[0]);
        return result;
    } catch (error) {
        console.error('Error importing from JSON:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Get events with pagination (performance optimized)
 */
ipcMain.handle('performance:getEventsPaginated', async (event, options) => {
    try {
        const result = performanceService.getEventsPaginated(options);
        return {
            success: true,
            data: result
        };
    } catch (error) {
        console.error('Error getting paginated events:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Clear performance cache
 */
ipcMain.handle('performance:clearCache', async () => {
    try {
        performanceService.clearCache();
        return { success: true };
    } catch (error) {
        console.error('Error clearing cache:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Get database statistics
 */
ipcMain.handle('performance:getStats', async () => {
    try {
        const stats = performanceService.getDatabaseStats();
        return {
            success: true,
            data: stats
        };
    } catch (error) {
        console.error('Error getting performance stats:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

/**
 * Optimize database
 */
ipcMain.handle('performance:optimize', async () => {
    try {
        performanceService.optimizeIndices();
        databaseService.vacuum();
        performanceService.clearCache();
        return { success: true };
    } catch (error) {
        console.error('Error optimizing database:', error);
        return {
            success: false,
            error: error.message
        };
    }
});

console.log('Main process initialized');

// Initialize services on startup
app.whenReady().then(() => {
    initializeAnthropicService();
    initializeSTTService();
    initializeEmbeddingService();
});
