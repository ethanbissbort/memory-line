/**
 * ExportService Unit Tests
 */

const Database = require('better-sqlite3');
const ExportService = require('../../src/main/services/exportService');
const fs = require('fs');
const path = require('path');
const os = require('os');

describe('ExportService', () => {
    let db;
    let exportService;
    let tempDir;

    beforeEach(() => {
        // Create in-memory database
        db = new Database(':memory:');

        // Create basic schema
        db.exec(`
            CREATE TABLE events (
                event_id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                start_date TEXT NOT NULL,
                end_date TEXT,
                description TEXT,
                category TEXT,
                era_id TEXT
            );

            CREATE TABLE eras (
                era_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                start_date TEXT NOT NULL,
                end_date TEXT,
                color_code TEXT NOT NULL,
                description TEXT
            );

            CREATE TABLE tags (tag_id TEXT PRIMARY KEY, tag_name TEXT NOT NULL);
            CREATE TABLE people (person_id TEXT PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE locations (location_id TEXT PRIMARY KEY, name TEXT NOT NULL);

            CREATE TABLE event_tags (event_id TEXT, tag_id TEXT, is_manual INTEGER DEFAULT 1);
            CREATE TABLE event_people (event_id TEXT, person_id TEXT);
            CREATE TABLE event_locations (event_id TEXT, location_id TEXT);
        `);

        // Insert sample data
        db.prepare("INSERT INTO events VALUES ('e1', 'Test Event', '2020-01-01', NULL, 'Test description', 'milestone', 'era1')").run();
        db.prepare("INSERT INTO eras VALUES ('era1', 'Test Era', '2020-01-01', '2020-12-31', '#3498db', 'Test era description')").run();
        db.prepare("INSERT INTO tags VALUES ('t1', 'important')").run();
        db.prepare("INSERT INTO people VALUES ('p1', 'John Doe')").run();
        db.prepare("INSERT INTO locations VALUES ('l1', 'New York')").run();
        db.prepare("INSERT INTO event_tags VALUES ('e1', 't1', 1)").run();
        db.prepare("INSERT INTO event_people VALUES ('e1', 'p1')").run();
        db.prepare("INSERT INTO event_locations VALUES ('e1', 'l1')").run();

        exportService = new ExportService(db);

        // Create temp directory for test files
        tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'export-test-'));
    });

    afterEach(() => {
        db.close();
        // Clean up temp files
        if (fs.existsSync(tempDir)) {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });

    describe('exportToJSON', () => {
        test('should export events to JSON file', () => {
            const filePath = path.join(tempDir, 'export.json');
            const result = exportService.exportToJSON(filePath);

            expect(result.success).toBe(true);
            expect(result.filePath).toBe(filePath);
            expect(result.recordCount).toBe(1);

            // Verify file exists and contains valid JSON
            expect(fs.existsSync(filePath)).toBe(true);
            const content = JSON.parse(fs.readFileSync(filePath, 'utf8'));

            expect(content.events).toHaveLength(1);
            expect(content.events[0].title).toBe('Test Event');
            expect(content.eras).toHaveLength(1);
            expect(content.tags).toHaveLength(1);
        });

        test('should include statistics in export', () => {
            const filePath = path.join(tempDir, 'export.json');
            exportService.exportToJSON(filePath);

            const content = JSON.parse(fs.readFileSync(filePath, 'utf8'));

            expect(content.statistics).toBeDefined();
            expect(content.statistics.total_events).toBe(1);
            expect(content.statistics.total_eras).toBe(1);
        });

        test('should handle export errors gracefully', () => {
            const invalidPath = '/invalid/path/export.json';
            const result = exportService.exportToJSON(invalidPath);

            expect(result.success).toBe(false);
            expect(result.error).toBeDefined();
        });
    });

    describe('exportToCSV', () => {
        test('should export events to CSV file', () => {
            const filePath = path.join(tempDir, 'export.csv');
            const result = exportService.exportToCSV(filePath);

            expect(result.success).toBe(true);
            expect(fs.existsSync(filePath)).toBe(true);

            const content = fs.readFileSync(filePath, 'utf8');
            expect(content).toContain('Event ID');
            expect(content).toContain('Test Event');
        });

        test('should properly escape CSV fields', () => {
            // Add event with special characters
            db.prepare(`INSERT INTO events VALUES ('e2', 'Event with "quotes"', '2020-02-01', NULL, 'Description, with comma', 'work', NULL)`).run();

            const filePath = path.join(tempDir, 'export.csv');
            exportService.exportToCSV(filePath);

            const content = fs.readFileSync(filePath, 'utf8');
            expect(content).toContain('"Event with ""quotes"""');
            expect(content).toContain('"Description, with comma"');
        });
    });

    describe('exportToMarkdown', () => {
        test('should export events to Markdown file', () => {
            const filePath = path.join(tempDir, 'export.md');
            const result = exportService.exportToMarkdown(filePath);

            expect(result.success).toBe(true);
            expect(fs.existsSync(filePath)).toBe(true);

            const content = fs.readFileSync(filePath, 'utf8');
            expect(content).toContain('# Memory Timeline Export');
            expect(content).toContain('## Test Era');
            expect(content).toContain('### Test Event');
        });

        test('should group events by era', () => {
            db.prepare("INSERT INTO events VALUES ('e2', 'Event 2', '2020-03-01', NULL, 'Another event', 'work', 'era1')").run();

            const filePath = path.join(tempDir, 'export.md');
            exportService.exportToMarkdown(filePath);

            const content = fs.readFileSync(filePath, 'utf8');

            // Should have one era header
            const eraMatches = content.match(/## Test Era/g);
            expect(eraMatches).toHaveLength(1);

            // Should have two events
            expect(content).toContain('### Test Event');
            expect(content).toContain('### Event 2');
        });
    });

    describe('importFromJSON', () => {
        test('should import events from JSON file', () => {
            // First export
            const exportPath = path.join(tempDir, 'export.json');
            exportService.exportToJSON(exportPath);

            // Create new database
            const newDb = new Database(':memory:');
            newDb.exec(db.prepare('SELECT sql FROM sqlite_master WHERE type="table"').all().map(r => r.sql).join(';'));

            const newExportService = new ExportService(newDb);

            // Import
            const result = newExportService.importFromJSON(exportPath);

            expect(result.success).toBe(true);
            expect(result.stats.events).toBe(1);
            expect(result.stats.eras).toBe(1);

            // Verify data
            const events = newDb.prepare('SELECT * FROM events').all();
            expect(events).toHaveLength(1);
            expect(events[0].title).toBe('Test Event');

            newDb.close();
        });

        test('should handle invalid JSON file', () => {
            const invalidPath = path.join(tempDir, 'invalid.json');
            fs.writeFileSync(invalidPath, 'not valid json');

            const result = exportService.importFromJSON(invalidPath);

            expect(result.success).toBe(false);
            expect(result.error).toBeDefined();
        });

        test('should track errors during import', () => {
            // Create JSON with invalid data
            const importData = {
                events: [
                    { title: 'Valid Event', start_date: '2020-01-01' },
                    { title: null, start_date: '2020-02-01' } // Invalid: null title
                ],
                eras: [],
                tags: [],
                people: [],
                locations: []
            };

            const importPath = path.join(tempDir, 'import.json');
            fs.writeFileSync(importPath, JSON.stringify(importData));

            const result = exportService.importFromJSON(importPath);

            expect(result.success).toBe(true);
            expect(result.stats.events).toBeLessThan(2);
            expect(result.stats.errors.length).toBeGreaterThan(0);
        });
    });
});
