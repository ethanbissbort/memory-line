/**
 * Performance Service
 * Handles performance optimization for large datasets
 */

class PerformanceService {
    constructor(db) {
        this.db = db;
        this.cache = new Map();
        this.cacheTimeout = 5 * 60 * 1000; // 5 minutes
    }

    /**
     * Get events with pagination and efficient querying
     * @param {Object} options - Query options
     * @returns {Object} Paginated results
     */
    getEventsPaginated(options = {}) {
        const {
            startDate,
            endDate,
            page = 1,
            pageSize = 100,
            category = null,
            searchQuery = null
        } = options;

        const offset = (page - 1) * pageSize;
        const cacheKey = JSON.stringify(options);

        // Check cache
        if (this.cache.has(cacheKey)) {
            const cached = this.cache.get(cacheKey);
            if (Date.now() - cached.timestamp < this.cacheTimeout) {
                return cached.data;
            }
            this.cache.delete(cacheKey);
        }

        try {
            let query = `
                SELECT e.*,
                       era.name as era_name,
                       era.color_code as era_color
                FROM events e
                LEFT JOIN eras era ON e.era_id = era.era_id
                WHERE 1=1
            `;
            const params = [];

            // Add filters
            if (startDate && endDate) {
                query += ' AND e.start_date >= ? AND e.start_date <= ?';
                params.push(startDate, endDate);
            }

            if (category) {
                query += ' AND e.category = ?';
                params.push(category);
            }

            if (searchQuery) {
                query += ` AND e.event_id IN (
                    SELECT e2.event_id FROM events_fts
                    JOIN events e2 ON events_fts.rowid = e2.rowid
                    WHERE events_fts MATCH ?
                )`;
                params.push(searchQuery);
            }

            // Get total count
            const countQuery = query.replace('SELECT e.*, era.name as era_name, era.color_code as era_color', 'SELECT COUNT(*) as total');
            const countStmt = this.db.prepare(countQuery);
            const { total } = countStmt.get(...params);

            // Add pagination
            query += ' ORDER BY e.start_date DESC LIMIT ? OFFSET ?';
            params.push(pageSize, offset);

            const stmt = this.db.prepare(query);
            const events = stmt.all(...params);

            // Batch load relationships for all events
            const eventIds = events.map(e => e.event_id);
            const relationships = this._batchLoadRelationships(eventIds);

            // Attach relationships to events
            events.forEach(event => {
                event.tags = relationships.tags[event.event_id] || [];
                event.people = relationships.people[event.event_id] || [];
                event.locations = relationships.locations[event.event_id] || [];
            });

            const result = {
                events: events,
                pagination: {
                    page: page,
                    pageSize: pageSize,
                    total: total,
                    totalPages: Math.ceil(total / pageSize),
                    hasMore: (page * pageSize) < total
                }
            };

            // Cache result
            this.cache.set(cacheKey, {
                data: result,
                timestamp: Date.now()
            });

            return result;
        } catch (error) {
            console.error('Error in getEventsPaginated:', error);
            throw error;
        }
    }

    /**
     * Batch load relationships for multiple events
     * More efficient than loading individually
     * @private
     */
    _batchLoadRelationships(eventIds) {
        if (eventIds.length === 0) {
            return { tags: {}, people: {}, locations: {} };
        }

        const placeholders = eventIds.map(() => '?').join(',');
        const relationships = {
            tags: {},
            people: {},
            locations: {}
        };

        // Load all tags in one query
        const tagsStmt = this.db.prepare(`
            SELECT et.event_id, t.tag_name
            FROM event_tags et
            JOIN tags t ON et.tag_id = t.tag_id
            WHERE et.event_id IN (${placeholders})
        `);
        const tagRows = tagsStmt.all(...eventIds);
        tagRows.forEach(row => {
            if (!relationships.tags[row.event_id]) {
                relationships.tags[row.event_id] = [];
            }
            relationships.tags[row.event_id].push(row.tag_name);
        });

        // Load all people in one query
        const peopleStmt = this.db.prepare(`
            SELECT ep.event_id, p.name
            FROM event_people ep
            JOIN people p ON ep.person_id = p.person_id
            WHERE ep.event_id IN (${placeholders})
        `);
        const peopleRows = peopleStmt.all(...eventIds);
        peopleRows.forEach(row => {
            if (!relationships.people[row.event_id]) {
                relationships.people[row.event_id] = [];
            }
            relationships.people[row.event_id].push(row.name);
        });

        // Load all locations in one query
        const locationsStmt = this.db.prepare(`
            SELECT el.event_id, l.name
            FROM event_locations el
            JOIN locations l ON el.location_id = l.location_id
            WHERE el.event_id IN (${placeholders})
        `);
        const locationRows = locationsStmt.all(...eventIds);
        locationRows.forEach(row => {
            if (!relationships.locations[row.event_id]) {
                relationships.locations[row.event_id] = [];
            }
            relationships.locations[row.event_id].push(row.name);
        });

        return relationships;
    }

    /**
     * Optimize database indices for performance
     */
    optimizeIndices() {
        try {
            // Analyze tables to update query planner statistics
            this.db.prepare('ANALYZE').run();

            // Additional performance indexes
            const additionalIndices = [
                'CREATE INDEX IF NOT EXISTS idx_events_created_at ON events(created_at DESC)',
                'CREATE INDEX IF NOT EXISTS idx_events_category_date ON events(category, start_date)',
                'CREATE INDEX IF NOT EXISTS idx_event_tags_composite ON event_tags(event_id, tag_id)',
                'CREATE INDEX IF NOT EXISTS idx_event_people_composite ON event_people(event_id, person_id)',
                'CREATE INDEX IF NOT EXISTS idx_event_locations_composite ON event_locations(event_id, location_id)'
            ];

            additionalIndices.forEach(indexSQL => {
                try {
                    this.db.prepare(indexSQL).run();
                } catch (error) {
                    // Index might already exist, that's okay
                    console.log('Index creation skipped:', error.message);
                }
            });

            console.log('Database indices optimized');
            return { success: true };
        } catch (error) {
            console.error('Error optimizing indices:', error);
            return { success: false, error: error.message };
        }
    }

    /**
     * Clear performance cache
     */
    clearCache() {
        this.cache.clear();
    }

    /**
     * Get cache statistics
     */
    getCacheStats() {
        return {
            size: this.cache.size,
            entries: Array.from(this.cache.entries()).map(([key, value]) => ({
                key: key,
                timestamp: value.timestamp,
                age: Date.now() - value.timestamp
            }))
        };
    }

    /**
     * Prefetch events for a date range (warming cache)
     */
    async prefetchEventRange(startDate, endDate) {
        const ranges = this._splitDateRange(startDate, endDate, 7); // 7-day chunks

        for (const range of ranges) {
            this.getEventsPaginated({
                startDate: range.start,
                endDate: range.end,
                pageSize: 100
            });

            // Small delay to avoid blocking
            await new Promise(resolve => setTimeout(resolve, 10));
        }

        return { success: true, rangesCached: ranges.length };
    }

    /**
     * Split date range into smaller chunks
     * @private
     */
    _splitDateRange(startDate, endDate, chunkDays) {
        const ranges = [];
        const start = new Date(startDate);
        const end = new Date(endDate);
        let current = new Date(start);

        while (current < end) {
            const chunkEnd = new Date(current);
            chunkEnd.setDate(chunkEnd.getDate() + chunkDays);

            ranges.push({
                start: current.toISOString().split('T')[0],
                end: (chunkEnd > end ? end : chunkEnd).toISOString().split('T')[0]
            });

            current = new Date(chunkEnd);
        }

        return ranges;
    }

    /**
     * Get database statistics for monitoring
     */
    getDatabaseStats() {
        try {
            const stats = {};

            // Table row counts
            const tables = ['events', 'eras', 'tags', 'people', 'locations',
                          'event_tags', 'event_people', 'event_locations',
                          'recording_queue', 'pending_events', 'cross_references',
                          'event_embeddings'];

            tables.forEach(table => {
                const result = this.db.prepare(`SELECT COUNT(*) as count FROM ${table}`).get();
                stats[table] = result.count;
            });

            // Database file size
            const dbPath = this.db.name;
            const fs = require('fs');
            if (fs.existsSync(dbPath)) {
                const size = fs.statSync(dbPath).size;
                stats.file_size_mb = (size / (1024 * 1024)).toFixed(2);
            }

            return stats;
        } catch (error) {
            console.error('Error getting database stats:', error);
            return null;
        }
    }
}

module.exports = PerformanceService;
