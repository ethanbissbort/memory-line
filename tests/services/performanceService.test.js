/**
 * PerformanceService Unit Tests
 */

const Database = require('better-sqlite3');
const PerformanceService = require('../../src/main/services/performanceService');

describe('PerformanceService', () => {
    let db;
    let performanceService;

    beforeEach(() => {
        // Create in-memory database
        db = new Database(':memory:');

        // Create schema
        db.exec(`
            CREATE TABLE events (
                event_id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                start_date TEXT NOT NULL,
                end_date TEXT,
                description TEXT,
                category TEXT,
                era_id TEXT,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE eras (
                era_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                start_date TEXT NOT NULL,
                end_date TEXT,
                color_code TEXT NOT NULL
            );

            CREATE TABLE tags (tag_id TEXT PRIMARY KEY, tag_name TEXT NOT NULL);
            CREATE TABLE people (person_id TEXT PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE locations (location_id TEXT PRIMARY KEY, name TEXT NOT NULL);

            CREATE TABLE event_tags (event_id TEXT, tag_id TEXT);
            CREATE TABLE event_people (event_id TEXT, person_id TEXT);
            CREATE TABLE event_locations (event_id TEXT, location_id TEXT);

            CREATE INDEX idx_events_start_date ON events(start_date);
            CREATE INDEX idx_events_category ON events(category);
        `);

        // Insert test data
        for (let i = 1; i <= 100; i++) {
            const date = new Date(2020, 0, i).toISOString().split('T')[0];
            db.prepare(`
                INSERT INTO events (event_id, title, start_date, category)
                VALUES (?, ?, ?, ?)
            `).run(`e${i}`, `Event ${i}`, date, i % 3 === 0 ? 'work' : 'milestone');

            // Add some tags
            if (i % 5 === 0) {
                db.prepare("INSERT INTO tags VALUES (?, ?)").run(`t${i}`, `tag${i}`);
                db.prepare("INSERT INTO event_tags VALUES (?, ?)").run(`e${i}`, `t${i}`);
            }
        }

        performanceService = new PerformanceService(db);
    });

    afterEach(() => {
        db.close();
    });

    describe('getEventsPaginated', () => {
        test('should return paginated results', () => {
            const result = performanceService.getEventsPaginated({
                page: 1,
                pageSize: 10
            });

            expect(result.events).toHaveLength(10);
            expect(result.pagination.page).toBe(1);
            expect(result.pagination.pageSize).toBe(10);
            expect(result.pagination.total).toBe(100);
            expect(result.pagination.totalPages).toBe(10);
        });

        test('should support pagination', () => {
            const page1 = performanceService.getEventsPaginated({ page: 1, pageSize: 10 });
            const page2 = performanceService.getEventsPaginated({ page: 2, pageSize: 10 });

            expect(page1.events[0].event_id).not.toBe(page2.events[0].event_id);
            expect(page2.pagination.page).toBe(2);
        });

        test('should filter by date range', () => {
            const result = performanceService.getEventsPaginated({
                startDate: '2020-01-10',
                endDate: '2020-01-20',
                pageSize: 100
            });

            expect(result.events.length).toBeGreaterThan(0);
            expect(result.events.length).toBeLessThanOrEqual(11);

            result.events.forEach(event => {
                expect(event.start_date >= '2020-01-10').toBe(true);
                expect(event.start_date <= '2020-01-20').toBe(true);
            });
        });

        test('should filter by category', () => {
            const result = performanceService.getEventsPaginated({
                category: 'work',
                pageSize: 100
            });

            expect(result.events.length).toBeGreaterThan(0);
            result.events.forEach(event => {
                expect(event.category).toBe('work');
            });
        });

        test('should include relationships in batch', () => {
            const result = performanceService.getEventsPaginated({
                page: 1,
                pageSize: 10
            });

            result.events.forEach(event => {
                expect(event.tags).toBeDefined();
                expect(Array.isArray(event.tags)).toBe(true);
                expect(event.people).toBeDefined();
                expect(event.locations).toBeDefined();
            });
        });

        test('should indicate if more pages available', () => {
            const result = performanceService.getEventsPaginated({
                page: 1,
                pageSize: 10
            });

            expect(result.pagination.hasMore).toBe(true);

            const lastPage = performanceService.getEventsPaginated({
                page: 10,
                pageSize: 10
            });

            expect(lastPage.pagination.hasMore).toBe(false);
        });
    });

    describe('caching', () => {
        test('should cache query results', () => {
            const options = { page: 1, pageSize: 10 };

            // First call
            const result1 = performanceService.getEventsPaginated(options);

            // Second call (should be cached)
            const result2 = performanceService.getEventsPaginated(options);

            expect(result1).toEqual(result2);

            // Verify cache is working
            const cacheStats = performanceService.getCacheStats();
            expect(cacheStats.size).toBeGreaterThan(0);
        });

        test('should clear cache', () => {
            performanceService.getEventsPaginated({ page: 1, pageSize: 10 });

            let stats = performanceService.getCacheStats();
            expect(stats.size).toBeGreaterThan(0);

            performanceService.clearCache();

            stats = performanceService.getCacheStats();
            expect(stats.size).toBe(0);
        });
    });

    describe('optimizeIndices', () => {
        test('should optimize database indices', () => {
            const result = performanceService.optimizeIndices();

            expect(result.success).toBe(true);
        });

        test('should not fail if indices exist', () => {
            // Run twice
            performanceService.optimizeIndices();
            const result = performanceService.optimizeIndices();

            expect(result.success).toBe(true);
        });
    });

    describe('getDatabaseStats', () => {
        test('should return database statistics', () => {
            const stats = performanceService.getDatabaseStats();

            expect(stats.events).toBe(100);
            expect(stats.eras).toBe(0);
            expect(stats.tags).toBeGreaterThan(0);
        });
    });

    describe('prefetchEventRange', () => {
        test('should prefetch date ranges', async () => {
            const result = await performanceService.prefetchEventRange('2020-01-01', '2020-01-31');

            expect(result.success).toBe(true);
            expect(result.rangesCached).toBeGreaterThan(0);

            // Verify cache was populated
            const cacheStats = performanceService.getCacheStats();
            expect(cacheStats.size).toBeGreaterThan(0);
        });
    });
});
