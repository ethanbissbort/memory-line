#!/usr/bin/env node
/**
 * Performance Test Script
 * Tests application performance with various database sizes
 */

const Database = require('better-sqlite3');
const path = require('path');
const PerformanceService = require('../../src/main/services/performanceService');

const DATABASES = [
    { name: 'test-small', file: 'test-small.db' },
    { name: 'test-medium', file: 'test-medium.db' },
    { name: 'test-large', file: 'test-large.db' },
    { name: 'test-stress', file: 'test-stress.db' }
];

/**
 * Format time in milliseconds
 */
function formatTime(ms) {
    if (ms < 1) return `${(ms * 1000).toFixed(2)}μs`;
    if (ms < 1000) return `${ms.toFixed(2)}ms`;
    return `${(ms / 1000).toFixed(2)}s`;
}

/**
 * Run performance test on a database
 */
function testDatabase(dbPath, name) {
    console.log(`\n📊 Testing: ${name}`);
    console.log('─'.repeat(50));

    const db = new Database(dbPath);
    const performanceService = new PerformanceService(db);

    // Get event count
    const eventCount = db.prepare('SELECT COUNT(*) as count FROM events').get().count;
    console.log(`Events: ${eventCount}`);

    const results = {};

    // Test 1: Full table scan
    const fullScanStart = performance.now();
    const allEvents = db.prepare('SELECT * FROM events').all();
    const fullScanTime = performance.now() - fullScanStart;
    results.fullScan = fullScanTime;
    console.log(`Full Scan: ${formatTime(fullScanTime)} (${allEvents.length} rows)`);

    // Test 2: Indexed query
    const indexedStart = performance.now();
    const dateFiltered = db.prepare(
        'SELECT * FROM events WHERE start_date >= ? AND start_date <= ?'
    ).all('2020-01-01', '2020-12-31');
    const indexedTime = performance.now() - indexedStart;
    results.indexedQuery = indexedTime;
    console.log(`Indexed Query: ${formatTime(indexedTime)} (${dateFiltered.length} rows)`);

    // Test 3: Paginated query
    const paginatedStart = performance.now();
    const paginated = performanceService.getEventsPaginated({
        page: 1,
        pageSize: 100
    });
    const paginatedTime = performance.now() - paginatedStart;
    results.paginated = paginatedTime;
    console.log(`Paginated Query: ${formatTime(paginatedTime)} (${paginated.events.length} rows)`);

    // Test 4: Cached query
    const cachedStart = performance.now();
    const cached = performanceService.getEventsPaginated({
        page: 1,
        pageSize: 100
    });
    const cachedTime = performance.now() - cachedStart;
    results.cached = cachedTime;
    console.log(`Cached Query: ${formatTime(cachedTime)} (${cached.events.length} rows)`);
    console.log(`Cache speedup: ${(paginatedTime / cachedTime).toFixed(1)}x`);

    // Test 5: FTS search
    const ftsStart = performance.now();
    const ftsResults = db.prepare(`
        SELECT e.* FROM events_fts
        JOIN events e ON events_fts.rowid = e.rowid
        WHERE events_fts MATCH ?
        LIMIT 50
    `).all('event');
    const ftsTime = performance.now() - ftsStart;
    results.fts = ftsTime;
    console.log(`Full-text Search: ${formatTime(ftsTime)} (${ftsResults.length} rows)`);

    // Test 6: Join query
    const joinStart = performance.now();
    const joinResults = db.prepare(`
        SELECT e.*, GROUP_CONCAT(t.tag_name) as tags
        FROM events e
        LEFT JOIN event_tags et ON e.event_id = et.event_id
        LEFT JOIN tags t ON et.tag_id = t.tag_id
        GROUP BY e.event_id
        LIMIT 100
    `).all();
    const joinTime = performance.now() - joinStart;
    results.join = joinTime;
    console.log(`Join Query: ${formatTime(joinTime)} (${joinResults.length} rows)`);

    // Test 7: Batch relationship loading
    const batchStart = performance.now();
    const eventIds = paginated.events.map(e => e.event_id);
    const batchRelationships = performanceService._batchLoadRelationships(eventIds);
    const batchTime = performance.now() - batchStart;
    results.batchLoad = batchTime;
    console.log(`Batch Load Relationships: ${formatTime(batchTime)} (${eventIds.length} events)`);

    // Test 8: Database stats
    const statsStart = performance.now();
    const stats = performanceService.getDatabaseStats();
    const statsTime = performance.now() - statsStart;
    results.stats = statsTime;
    console.log(`Get Database Stats: ${formatTime(statsTime)}`);

    db.close();

    return { name, eventCount, results };
}

/**
 * Main function
 */
function main() {
    console.log('\n🚀 Memory Timeline Performance Test Suite\n');
    console.log('='.repeat(50));

    const databasesDir = path.join(__dirname, '../fixtures/databases');
    const allResults = [];

    DATABASES.forEach(({ name, file }) => {
        const dbPath = path.join(databasesDir, file);

        try {
            const result = testDatabase(dbPath, name);
            allResults.push(result);
        } catch (error) {
            console.log(`\n⚠️  ${name}: Database not found or error`);
            console.log(`Run 'npm run test:seed' to generate test databases`);
        }
    });

    if (allResults.length > 0) {
        console.log('\n' + '='.repeat(50));
        console.log('\n📈 Performance Summary\n');

        console.log('Database Size vs Query Time:');
        console.log('─'.repeat(50));
        console.log('Database'.padEnd(15) + 'Events'.padEnd(10) + 'Full Scan'.padEnd(15) + 'Paginated'.padEnd(15) + 'Cached');
        console.log('─'.repeat(50));

        allResults.forEach(({ name, eventCount, results }) => {
            console.log(
                name.padEnd(15) +
                eventCount.toString().padEnd(10) +
                formatTime(results.fullScan).padEnd(15) +
                formatTime(results.paginated).padEnd(15) +
                formatTime(results.cached)
            );
        });

        console.log('\n✅ Performance tests completed!\n');

        // Performance guidelines
        console.log('Performance Guidelines:');
        console.log('  • Paginated queries should be < 100ms for 1000 events');
        console.log('  • Cached queries should be < 10ms');
        console.log('  • Full-text search should be < 50ms for 5000 events');
        console.log('  • Cache speedup should be > 5x\n');
    }
}

// Run if executed directly
if (require.main === module) {
    try {
        main();
    } catch (error) {
        console.error('Error running performance tests:', error);
        process.exit(1);
    }
}

module.exports = { testDatabase };
