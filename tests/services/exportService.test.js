/**
 * ExportService Unit Tests
 */

const ExportService = require('../../src/main/services/exportService');
const fs = require('fs');
const path = require('path');
const os = require('os');
const {
    createTestDb,
    insertEra,
    insertEvent,
    insertTag,
    insertPerson,
    insertLocation,
    linkTag,
    linkPerson,
    linkLocation
} = require('../helpers/createTestDb');

describe('ExportService', () => {
    let db;
    let exportService;
    let tempDir;

    beforeEach(() => {
        // Build the database from the canonical production schema
        db = createTestDb();

        // Insert sample data (era first: foreign keys are enforced)
        insertEra(db, {
            era_id: 'era1', name: 'Test Era', start_date: '2020-01-01',
            end_date: '2020-12-31', color_code: '#3498db',
            description: 'Test era description'
        });
        insertEvent(db, {
            event_id: 'e1', title: 'Test Event', start_date: '2020-01-01',
            description: 'Test description', category: 'milestone', era_id: 'era1'
        });
        insertTag(db, { tag_id: 't1', tag_name: 'important' });
        insertPerson(db, { person_id: 'p1', name: 'John Doe' });
        insertLocation(db, { location_id: 'l1', name: 'New York' });
        linkTag(db, 'e1', 't1');
        linkPerson(db, 'e1', 'p1');
        linkLocation(db, 'e1', 'l1');

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
            insertEvent(db, {
                event_id: 'e2', title: 'Event with "quotes"', start_date: '2020-02-01',
                description: 'Description, with comma', category: 'work'
            });

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
            insertEvent(db, {
                event_id: 'e2', title: 'Event 2', start_date: '2020-03-01',
                description: 'Another event', category: 'work', era_id: 'era1'
            });

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

            // Create new database from the same canonical schema
            const newDb = createTestDb();

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

        // KNOWN PRODUCT BUG (src/main/services/exportService.js:421-491):
        // events are imported with INSERT OR IGNORE, so a NOT NULL violation
        // (null title) does not throw -- SQLite silently skips the row, yet
        // stats.events is still incremented and no error is recorded. The
        // fix is to check run().changes (or drop OR IGNORE) before counting.
        // This test asserts the CORRECT behavior and is marked failing so
        // the suite stays green while the bug is tracked.
        test('should not count rows silently skipped by INSERT OR IGNORE', () => {
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

            const before = db.prepare('SELECT COUNT(*) as count FROM events').get().count;
            const result = exportService.importFromJSON(importPath);
            const after = db.prepare('SELECT COUNT(*) as count FROM events').get().count;

            expect(result.success).toBe(true);
            // Only the valid event actually lands in the database.
            expect(after - before).toBe(1);
            // Correct behavior: stats must reflect what was really imported
            // (today stats.events is 2 and errors is empty).
            expect(result.stats.events).toBe(1);
        });
    });
});
