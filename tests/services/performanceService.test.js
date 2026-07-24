/**
 * PerformanceService Unit Tests
 *
 * The test database is built from the canonical production DDL
 * (src/database/schemas/schema.sql) via tests/helpers/createTestDb.js.
 * getDatabaseStats in particular reads every production table
 * (recording_queue, pending_events, cross_references, event_embeddings, ...),
 * so a hand-rolled partial schema would make it return null.
 */

const PerformanceService = require('../../src/main/services/performanceService');
const {
    createTestDb,
    insertEvent,
    insertTag,
    linkTag
} = require('../helpers/createTestDb');

/** Timezone-independent ISO date: day `i` counted from 2020-01-01. */
function isoDate(i) {
    return new Date(Date.UTC(2020, 0, i)).toISOString().split('T')[0];
}

describe('PerformanceService', () => {
    let db;
    let performanceService;

    beforeEach(() => {
        db = createTestDb();

        // Insert 100 events (2020-01-01 .. 2020-04-09), every 3rd is 'work'
        for (let i = 1; i <= 100; i++) {
            insertEvent(db, {
                event_id: `e${i}`,
                title: `Event ${i}`,
                start_date: isoDate(i),
                category: i % 3 === 0 ? 'work' : 'milestone'
            });

            // Add some tags
            if (i % 5 === 0) {
                insertTag(db, { tag_id: `t${i}`, tag_name: `tag${i}` });
                linkTag(db, `e${i}`, `t${i}`);
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

            // Pages must not overlap
            const ids1 = page1.events.map(e => e.event_id);
            const ids2 = page2.events.map(e => e.event_id);
            expect(ids1.filter(id => ids2.includes(id))).toEqual([]);
        });

        test('should filter by date range', () => {
            const result = performanceService.getEventsPaginated({
                startDate: isoDate(10),
                endDate: isoDate(20),
                pageSize: 100
            });

            // Days 10..20 inclusive
            expect(result.events.length).toBe(11);
            expect(result.pagination.total).toBe(11);

            result.events.forEach(event => {
                expect(event.start_date >= isoDate(10)).toBe(true);
                expect(event.start_date <= isoDate(20)).toBe(true);
            });
        });

        test('should filter by category', () => {
            const result = performanceService.getEventsPaginated({
                category: 'work',
                pageSize: 100
            });

            expect(result.events.length).toBe(33); // every 3rd of 100
            result.events.forEach(event => {
                expect(event.category).toBe('work');
            });
        });

        test('should filter via FTS searchQuery maintained by the schema triggers', () => {
            const result = performanceService.getEventsPaginated({
                searchQuery: 'Event 5',
                pageSize: 100
            });

            // Tokens 'Event' AND '5' -> only the title "Event 5" ('50' is a
            // different token, so "Event 50" does not match).
            expect(result.events.map(e => e.event_id)).toEqual(['e5']);
            expect(result.pagination.total).toBe(1);
        });

        test('should not throw on FTS special characters in searchQuery', () => {
            const result = performanceService.getEventsPaginated({
                searchQuery: 'Event"(*^ 5',
                pageSize: 100
            });

            expect(result.events.map(e => e.event_id)).toEqual(['e5']);
        });

        test('should include relationships in batch', () => {
            const result = performanceService.getEventsPaginated({
                page: 1,
                pageSize: 100
            });

            result.events.forEach(event => {
                expect(Array.isArray(event.tags)).toBe(true);
                expect(Array.isArray(event.people)).toBe(true);
                expect(Array.isArray(event.locations)).toBe(true);
            });

            const e100 = result.events.find(e => e.event_id === 'e100');
            expect(e100.tags).toEqual(['tag100']);
            const e99 = result.events.find(e => e.event_id === 'e99');
            expect(e99.tags).toEqual([]);
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
        test('should return row counts for every production table', () => {
            const stats = performanceService.getDatabaseStats();

            expect(stats).not.toBeNull();
            expect(stats.events).toBe(100);
            expect(stats.eras).toBe(0);
            expect(stats.tags).toBe(20);
            expect(stats.event_tags).toBe(20);
            expect(stats.people).toBe(0);
            expect(stats.locations).toBe(0);
            expect(stats.recording_queue).toBe(0);
            expect(stats.pending_events).toBe(0);
            expect(stats.cross_references).toBe(0);
            expect(stats.event_embeddings).toBe(0);
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
