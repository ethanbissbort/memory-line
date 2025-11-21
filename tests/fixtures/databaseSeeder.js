/**
 * Database Seeder
 * Seeds test databases with sample data
 */

const Database = require('better-sqlite3');
const path = require('path');
const fs = require('fs');
const { v4: uuidv4 } = require('uuid');
const { generateSampleData } = require('./sampleDataGenerator');

/**
 * Create database with schema
 */
function createDatabase(dbPath) {
    // Ensure directory exists
    const dir = path.dirname(dbPath);
    if (!fs.existsSync(dir)) {
        fs.mkdirSync(dir, { recursive: true });
    }

    // Delete if exists
    if (fs.existsSync(dbPath)) {
        fs.unlinkSync(dbPath);
    }

    const db = new Database(dbPath);

    // Read and execute schema
    const schemaPath = path.join(__dirname, '../../src/database/schemas/schema.sql');
    const schema = fs.readFileSync(schemaPath, 'utf8');

    db.exec(schema);

    return db;
}

/**
 * Seed database with sample data
 */
function seedDatabase(db, data) {
    const transaction = db.transaction(() => {
        // Insert eras
        const insertEra = db.prepare(`
            INSERT INTO eras (era_id, name, start_date, end_date, color_code, description)
            VALUES (?, ?, ?, ?, ?, ?)
        `);

        data.eras.forEach(era => {
            insertEra.run(
                era.era_id,
                era.name,
                era.start_date,
                era.end_date,
                era.color_code,
                era.description
            );
        });

        // Insert tags
        const insertTag = db.prepare(`
            INSERT INTO tags (tag_id, tag_name) VALUES (?, ?)
        `);

        data.tags.forEach(tag => {
            insertTag.run(tag.tag_id, tag.tag_name);
        });

        // Insert people
        const insertPerson = db.prepare(`
            INSERT INTO people (person_id, name) VALUES (?, ?)
        `);

        data.people.forEach(person => {
            insertPerson.run(person.person_id, person.name);
        });

        // Insert locations
        const insertLocation = db.prepare(`
            INSERT INTO locations (location_id, name) VALUES (?, ?)
        `);

        data.locations.forEach(location => {
            insertLocation.run(location.location_id, location.name);
        });

        // Insert events
        const insertEvent = db.prepare(`
            INSERT INTO events (
                event_id, title, start_date, end_date, description, category, era_id
            ) VALUES (?, ?, ?, ?, ?, ?, ?)
        `);

        const linkTag = db.prepare(`
            INSERT INTO event_tags (event_id, tag_id, is_manual) VALUES (?, ?, 1)
        `);

        const linkPerson = db.prepare(`
            INSERT INTO event_people (event_id, person_id) VALUES (?, ?)
        `);

        const linkLocation = db.prepare(`
            INSERT INTO event_locations (event_id, location_id) VALUES (?, ?)
        `);

        data.events.forEach(event => {
            // Insert event
            insertEvent.run(
                event.event_id,
                event.title,
                event.start_date,
                event.end_date,
                event.description,
                event.category,
                event.era_id
            );

            // Link tags
            event.tags.forEach(tagName => {
                const tag = data.tags.find(t => t.tag_name === tagName);
                if (tag) {
                    linkTag.run(event.event_id, tag.tag_id);
                }
            });

            // Link people
            event.people.forEach(personName => {
                const person = data.people.find(p => p.name === personName);
                if (person) {
                    linkPerson.run(event.event_id, person.person_id);
                }
            });

            // Link locations
            event.locations.forEach(locationName => {
                const location = data.locations.find(l => l.name === locationName);
                if (location) {
                    linkLocation.run(event.event_id, location.location_id);
                }
            });
        });
    });

    transaction();
}

/**
 * Create test database with specified size
 */
function createTestDatabase(name, eventCount, outputDir = './tests/fixtures/databases') {
    console.log(`Creating ${name} database with ${eventCount} events...`);

    const dbPath = path.join(outputDir, `${name}.db`);
    const db = createDatabase(dbPath);

    // Generate sample data
    const eraCount = Math.max(3, Math.floor(eventCount / 100));
    const data = generateSampleData(eventCount, eraCount);

    // Seed database
    seedDatabase(db, data);

    // Get statistics
    const stats = {
        events: db.prepare('SELECT COUNT(*) as count FROM events').get().count,
        eras: db.prepare('SELECT COUNT(*) as count FROM eras').get().count,
        tags: db.prepare('SELECT COUNT(*) as count FROM tags').get().count,
        people: db.prepare('SELECT COUNT(*) as count FROM people').get().count,
        locations: db.prepare('SELECT COUNT(*) as count FROM locations').get().count,
        file_size_mb: (fs.statSync(dbPath).size / (1024 * 1024)).toFixed(2)
    };

    // Optimize database
    db.prepare('ANALYZE').run();
    db.prepare('VACUUM').run();

    db.close();

    console.log(`✓ Created ${name}.db (${stats.events} events, ${stats.file_size_mb} MB)`);

    // Save metadata
    const metadataPath = path.join(outputDir, `${name}.json`);
    fs.writeFileSync(metadataPath, JSON.stringify({
        name: name,
        created_at: new Date().toISOString(),
        statistics: stats,
        sample_data: data.statistics
    }, null, 2));

    return { dbPath, stats };
}

/**
 * Create all test databases
 */
function createAllTestDatabases() {
    const outputDir = path.join(__dirname, 'databases');

    console.log('\n🗄️  Creating Test Databases\n');
    console.log('='.repeat(50));

    const databases = [
        { name: 'test-tiny', events: 10, description: 'Tiny dataset for quick tests' },
        { name: 'test-small', events: 100, description: 'Small dataset for unit tests' },
        { name: 'test-medium', events: 500, description: 'Medium dataset for integration tests' },
        { name: 'test-large', events: 1000, description: 'Large dataset for performance tests' },
        { name: 'test-stress', events: 5000, description: 'Stress test dataset (5000+ events)' }
    ];

    const results = [];

    databases.forEach(({ name, events, description }) => {
        console.log(`\n${description}:`);
        const result = createTestDatabase(name, events, outputDir);
        results.push({ name, ...result });
    });

    console.log('\n' + '='.repeat(50));
    console.log('✅ All test databases created successfully!\n');

    // Create summary
    const summaryPath = path.join(outputDir, 'README.md');
    const summary = `# Test Databases

Generated on: ${new Date().toISOString()}

## Available Databases

${databases.map(({ name, events, description }, i) => `
### ${name}.db
- **Description:** ${description}
- **Events:** ${results[i].stats.events}
- **Size:** ${results[i].stats.file_size_mb} MB
- **Eras:** ${results[i].stats.eras}
- **Tags:** ${results[i].stats.tags}
- **People:** ${results[i].stats.people}
- **Locations:** ${results[i].stats.locations}
`).join('\n')}

## Usage

\`\`\`javascript
const Database = require('better-sqlite3');
const db = new Database('./tests/fixtures/databases/test-small.db');

// Query events
const events = db.prepare('SELECT * FROM events LIMIT 10').all();
console.log(events);
\`\`\`

## Regenerating Databases

\`\`\`bash
npm run test:seed
\`\`\`
`;

    fs.writeFileSync(summaryPath, summary);
    console.log(`📄 Summary written to ${summaryPath}\n`);

    return results;
}

// Run if executed directly
if (require.main === module) {
    try {
        createAllTestDatabases();
    } catch (error) {
        console.error('Error creating test databases:', error);
        process.exit(1);
    }
}

module.exports = {
    createDatabase,
    seedDatabase,
    createTestDatabase,
    createAllTestDatabases
};
