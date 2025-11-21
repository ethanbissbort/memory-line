/**
 * Export Service
 * Handles exporting timeline data to various formats (JSON, CSV, PDF)
 */

const fs = require('fs');
const path = require('path');

class ExportService {
    constructor(db) {
        this.db = db;
    }

    /**
     * Export all events to JSON
     * @param {string} filePath - Output file path
     * @returns {Object} Result with success status
     */
    exportToJSON(filePath) {
        try {
            // Get all events with related data
            const events = this._getAllEventsWithRelations();
            const eras = this._getAllEras();
            const tags = this._getAllTags();
            const people = this._getAllPeople();
            const locations = this._getAllLocations();

            const exportData = {
                exported_at: new Date().toISOString(),
                version: '1.0.0',
                events: events,
                eras: eras,
                tags: tags,
                people: people,
                locations: locations,
                statistics: {
                    total_events: events.length,
                    total_eras: eras.length,
                    total_tags: tags.length,
                    total_people: people.length,
                    total_locations: locations.length
                }
            };

            fs.writeFileSync(filePath, JSON.stringify(exportData, null, 2), 'utf8');

            return {
                success: true,
                filePath: filePath,
                recordCount: events.length
            };
        } catch (error) {
            console.error('Error exporting to JSON:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }

    /**
     * Export events to CSV
     * @param {string} filePath - Output file path
     * @returns {Object} Result with success status
     */
    exportToCSV(filePath) {
        try {
            const events = this._getAllEventsWithRelations();

            // CSV headers
            const headers = [
                'Event ID',
                'Title',
                'Start Date',
                'End Date',
                'Description',
                'Category',
                'Era',
                'Tags',
                'People',
                'Locations',
                'Created At',
                'Updated At'
            ];

            // Build CSV content
            let csvContent = headers.map(h => this._escapeCSV(h)).join(',') + '\n';

            events.forEach(event => {
                const row = [
                    event.event_id,
                    event.title,
                    event.start_date,
                    event.end_date || '',
                    event.description || '',
                    event.category || '',
                    event.era_name || '',
                    event.tags.join('; '),
                    event.people.join('; '),
                    event.locations.join('; '),
                    event.created_at,
                    event.updated_at
                ];

                csvContent += row.map(r => this._escapeCSV(String(r))).join(',') + '\n';
            });

            fs.writeFileSync(filePath, csvContent, 'utf8');

            return {
                success: true,
                filePath: filePath,
                recordCount: events.length
            };
        } catch (error) {
            console.error('Error exporting to CSV:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }

    /**
     * Export events to Markdown format
     * @param {string} filePath - Output file path
     * @returns {Object} Result with success status
     */
    exportToMarkdown(filePath) {
        try {
            const events = this._getAllEventsWithRelations();
            const eras = this._getAllEras();

            let markdown = '# Memory Timeline Export\n\n';
            markdown += `**Exported:** ${new Date().toISOString()}\n\n`;
            markdown += `**Total Events:** ${events.length}\n\n`;
            markdown += '---\n\n';

            // Group events by era
            const eventsByEra = {};
            const noEraEvents = [];

            events.forEach(event => {
                if (event.era_id) {
                    if (!eventsByEra[event.era_id]) {
                        eventsByEra[event.era_id] = [];
                    }
                    eventsByEra[event.era_id].push(event);
                } else {
                    noEraEvents.push(event);
                }
            });

            // Write events by era
            eras.forEach(era => {
                const eraEvents = eventsByEra[era.era_id] || [];
                if (eraEvents.length > 0) {
                    markdown += `## ${era.name}\n\n`;
                    markdown += `**Period:** ${era.start_date}${era.end_date ? ` - ${era.end_date}` : ' - Present'}\n\n`;
                    if (era.description) {
                        markdown += `${era.description}\n\n`;
                    }

                    eraEvents.forEach(event => {
                        markdown += this._formatEventMarkdown(event);
                    });

                    markdown += '---\n\n';
                }
            });

            // Write events without era
            if (noEraEvents.length > 0) {
                markdown += '## Uncategorized Events\n\n';
                noEraEvents.forEach(event => {
                    markdown += this._formatEventMarkdown(event);
                });
            }

            fs.writeFileSync(filePath, markdown, 'utf8');

            return {
                success: true,
                filePath: filePath,
                recordCount: events.length
            };
        } catch (error) {
            console.error('Error exporting to Markdown:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }

    /**
     * Format an event as Markdown
     * @private
     */
    _formatEventMarkdown(event) {
        let md = `### ${event.title}\n\n`;
        md += `**Date:** ${event.start_date}${event.end_date ? ` - ${event.end_date}` : ''}\n\n`;

        if (event.category) {
            md += `**Category:** ${event.category}\n\n`;
        }

        if (event.description) {
            md += `${event.description}\n\n`;
        }

        if (event.tags.length > 0) {
            md += `**Tags:** ${event.tags.map(t => `\`${t}\``).join(', ')}\n\n`;
        }

        if (event.people.length > 0) {
            md += `**People:** ${event.people.join(', ')}\n\n`;
        }

        if (event.locations.length > 0) {
            md += `**Locations:** ${event.locations.join(', ')}\n\n`;
        }

        md += '\n';
        return md;
    }

    /**
     * Get all events with relations
     * @private
     */
    _getAllEventsWithRelations() {
        const stmt = this.db.prepare(`
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
            GROUP BY e.event_id
            ORDER BY e.start_date ASC
        `);

        const events = stmt.all();

        // Parse arrays
        events.forEach(event => {
            event.tags = event.tags ? event.tags.split(',') : [];
            event.people = event.people ? event.people.split(',') : [];
            event.locations = event.locations ? event.locations.split(',') : [];
            if (event.extraction_metadata) {
                try {
                    event.extraction_metadata = JSON.parse(event.extraction_metadata);
                } catch (e) {
                    event.extraction_metadata = null;
                }
            }
        });

        return events;
    }

    /**
     * Get all eras
     * @private
     */
    _getAllEras() {
        const stmt = this.db.prepare('SELECT * FROM eras ORDER BY start_date ASC');
        return stmt.all();
    }

    /**
     * Get all tags
     * @private
     */
    _getAllTags() {
        const stmt = this.db.prepare('SELECT * FROM tags ORDER BY tag_name ASC');
        return stmt.all();
    }

    /**
     * Get all people
     * @private
     */
    _getAllPeople() {
        const stmt = this.db.prepare('SELECT * FROM people ORDER BY name ASC');
        return stmt.all();
    }

    /**
     * Get all locations
     * @private
     */
    _getAllLocations() {
        const stmt = this.db.prepare('SELECT * FROM locations ORDER BY name ASC');
        return stmt.all();
    }

    /**
     * Escape CSV field
     * @private
     */
    _escapeCSV(field) {
        if (field == null) return '';
        const str = String(field);
        // If field contains comma, quote, or newline, wrap in quotes and escape quotes
        if (str.includes(',') || str.includes('"') || str.includes('\n')) {
            return '"' + str.replace(/"/g, '""') + '"';
        }
        return str;
    }

    /**
     * Import events from JSON file
     * @param {string} filePath - JSON file path
     * @returns {Object} Result with import statistics
     */
    importFromJSON(filePath) {
        try {
            const content = fs.readFileSync(filePath, 'utf8');
            const data = JSON.parse(content);

            if (!data.events || !Array.isArray(data.events)) {
                throw new Error('Invalid JSON format: missing events array');
            }

            const stats = {
                events: 0,
                eras: 0,
                tags: 0,
                people: 0,
                locations: 0,
                errors: []
            };

            // Import in a transaction for performance
            const transaction = this.db.transaction(() => {
                const { v4: uuidv4 } = require('uuid');

                // Import eras first
                if (data.eras && Array.isArray(data.eras)) {
                    const insertEra = this.db.prepare(`
                        INSERT OR IGNORE INTO eras (era_id, name, start_date, end_date, color_code, description)
                        VALUES (?, ?, ?, ?, ?, ?)
                    `);

                    data.eras.forEach(era => {
                        try {
                            insertEra.run(
                                era.era_id || uuidv4(),
                                era.name,
                                era.start_date,
                                era.end_date,
                                era.color_code,
                                era.description
                            );
                            stats.eras++;
                        } catch (error) {
                            stats.errors.push({ type: 'era', name: era.name, error: error.message });
                        }
                    });
                }

                // Import tags
                if (data.tags && Array.isArray(data.tags)) {
                    const insertTag = this.db.prepare(`
                        INSERT OR IGNORE INTO tags (tag_id, tag_name) VALUES (?, ?)
                    `);

                    data.tags.forEach(tag => {
                        try {
                            insertTag.run(tag.tag_id || uuidv4(), tag.tag_name);
                            stats.tags++;
                        } catch (error) {
                            stats.errors.push({ type: 'tag', name: tag.tag_name, error: error.message });
                        }
                    });
                }

                // Import people
                if (data.people && Array.isArray(data.people)) {
                    const insertPerson = this.db.prepare(`
                        INSERT OR IGNORE INTO people (person_id, name) VALUES (?, ?)
                    `);

                    data.people.forEach(person => {
                        try {
                            insertPerson.run(person.person_id || uuidv4(), person.name);
                            stats.people++;
                        } catch (error) {
                            stats.errors.push({ type: 'person', name: person.name, error: error.message });
                        }
                    });
                }

                // Import locations
                if (data.locations && Array.isArray(data.locations)) {
                    const insertLocation = this.db.prepare(`
                        INSERT OR IGNORE INTO locations (location_id, name) VALUES (?, ?)
                    `);

                    data.locations.forEach(location => {
                        try {
                            insertLocation.run(location.location_id || uuidv4(), location.name);
                            stats.locations++;
                        } catch (error) {
                            stats.errors.push({ type: 'location', name: location.name, error: error.message });
                        }
                    });
                }

                // Import events
                const insertEvent = this.db.prepare(`
                    INSERT OR IGNORE INTO events (
                        event_id, title, start_date, end_date, description,
                        category, era_id, audio_file_path, raw_transcript, extraction_metadata
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                `);

                const linkTag = this.db.prepare(`
                    INSERT OR IGNORE INTO event_tags (event_id, tag_id, is_manual) VALUES (?, ?, 1)
                `);

                const linkPerson = this.db.prepare(`
                    INSERT OR IGNORE INTO event_people (event_id, person_id) VALUES (?, ?)
                `);

                const linkLocation = this.db.prepare(`
                    INSERT OR IGNORE INTO event_locations (event_id, location_id) VALUES (?, ?)
                `);

                data.events.forEach(event => {
                    try {
                        const eventId = event.event_id || uuidv4();

                        insertEvent.run(
                            eventId,
                            event.title,
                            event.start_date,
                            event.end_date,
                            event.description,
                            event.category,
                            event.era_id,
                            event.audio_file_path,
                            event.raw_transcript,
                            event.extraction_metadata ? JSON.stringify(event.extraction_metadata) : null
                        );

                        // Link tags
                        if (event.tags && Array.isArray(event.tags)) {
                            event.tags.forEach(tagName => {
                                const tag = this.db.prepare('SELECT tag_id FROM tags WHERE tag_name = ?').get(tagName);
                                if (tag) {
                                    linkTag.run(eventId, tag.tag_id);
                                }
                            });
                        }

                        // Link people
                        if (event.people && Array.isArray(event.people)) {
                            event.people.forEach(personName => {
                                const person = this.db.prepare('SELECT person_id FROM people WHERE name = ?').get(personName);
                                if (person) {
                                    linkPerson.run(eventId, person.person_id);
                                }
                            });
                        }

                        // Link locations
                        if (event.locations && Array.isArray(event.locations)) {
                            event.locations.forEach(locationName => {
                                const location = this.db.prepare('SELECT location_id FROM locations WHERE name = ?').get(locationName);
                                if (location) {
                                    linkLocation.run(eventId, location.location_id);
                                }
                            });
                        }

                        stats.events++;
                    } catch (error) {
                        stats.errors.push({ type: 'event', title: event.title, error: error.message });
                    }
                });
            });

            transaction();

            return {
                success: true,
                stats: stats
            };
        } catch (error) {
            console.error('Error importing from JSON:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }
}

module.exports = ExportService;
