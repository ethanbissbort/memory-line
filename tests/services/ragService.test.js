/**
 * RAGService Unit Tests
 */

const Database = require('better-sqlite3');
const RAGService = require('../../src/main/services/ragService');
const EmbeddingService = require('../../src/main/services/embeddingService');

describe('RAGService', () => {
    let db;
    let embeddingService;
    let ragService;

    beforeEach(async () => {
        // Create in-memory database
        db = new Database(':memory:');

        // Create schema
        db.exec(`
            CREATE TABLE events (
                event_id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                description TEXT,
                category TEXT,
                start_date TEXT NOT NULL
            );

            CREATE TABLE event_embeddings (
                embedding_id TEXT PRIMARY KEY,
                event_id TEXT NOT NULL UNIQUE,
                embedding_vector TEXT NOT NULL,
                embedding_provider TEXT NOT NULL,
                embedding_model TEXT NOT NULL,
                embedding_dimension INTEGER NOT NULL
            );

            CREATE TABLE cross_references (
                reference_id TEXT PRIMARY KEY,
                source_event_id TEXT NOT NULL,
                target_event_id TEXT NOT NULL,
                relationship_type TEXT NOT NULL,
                confidence_score REAL,
                llm_reasoning TEXT
            );

            CREATE TABLE tags (tag_id TEXT PRIMARY KEY, tag_name TEXT NOT NULL);
            CREATE TABLE event_tags (event_id TEXT, tag_id TEXT);
        `);

        // Initialize services
        embeddingService = new EmbeddingService(db);
        await embeddingService.initialize('local', 'test-model');

        ragService = new RAGService(db, embeddingService);
    });

    afterEach(() => {
        db.close();
    });

    describe('getCrossReferences', () => {
        test('should return cross references for event', () => {
            // Insert test data
            db.prepare("INSERT INTO events VALUES ('e1', 'Event 1', 'Desc 1', 'work', '2020-01-01')").run();
            db.prepare("INSERT INTO events VALUES ('e2', 'Event 2', 'Desc 2', 'milestone', '2020-02-01')").run();

            db.prepare(`
                INSERT INTO cross_references (reference_id, source_event_id, target_event_id, relationship_type, confidence_score)
                VALUES ('r1', 'e1', 'e2', 'causal', 0.9)
            `).run();

            const refs = ragService.getCrossReferences('e1');

            expect(refs).toHaveLength(1);
            expect(refs[0].target_event_id).toBe('e2');
            expect(refs[0].relationship_type).toBe('causal');
        });

        test('should return empty array if no references', () => {
            db.prepare("INSERT INTO events VALUES ('e1', 'Event 1', 'Desc 1', 'work', '2020-01-01')").run();

            const refs = ragService.getCrossReferences('e1');

            expect(refs).toEqual([]);
        });
    });

    describe('detectPatterns', () => {
        beforeEach(() => {
            // Insert events with patterns
            for (let i = 1; i <= 10; i++) {
                const category = i % 3 === 0 ? 'work' : 'milestone';
                db.prepare(`
                    INSERT INTO events (event_id, title, description, category, start_date)
                    VALUES (?, ?, ?, ?, ?)
                `).run(`e${i}`, `Event ${i}`, `Description ${i}`, category, `2020-0${Math.ceil(i/3)}-01`);
            }
        });

        test('should detect recurring categories', async () => {
            const result = await ragService.detectPatterns();

            expect(result.success).toBe(true);
            expect(result.patterns).toBeDefined();

            const categoryPattern = result.patterns.find(p => p.type === 'recurring_categories');
            expect(categoryPattern).toBeDefined();
            expect(categoryPattern.patterns.length).toBeGreaterThan(0);
        });

        test('should detect temporal clusters', async () => {
            const result = await ragService.detectPatterns();

            expect(result.success).toBe(true);

            const temporalPattern = result.patterns.find(p => p.type === 'temporal_clusters');
            expect(temporalPattern).toBeDefined();
        });
    });

    describe('suggestTags', () => {
        beforeEach(() => {
            // Insert events and tags
            db.prepare("INSERT INTO events VALUES ('e1', 'Work Project', 'Project desc', 'work', '2020-01-01')").run();
            db.prepare("INSERT INTO events VALUES ('e2', 'Work Meeting', 'Meeting desc', 'work', '2020-02-01')").run();

            db.prepare("INSERT INTO tags VALUES ('t1', 'important')").run();
            db.prepare("INSERT INTO tags VALUES ('t2', 'career')").run();

            db.prepare("INSERT INTO event_tags VALUES ('e1', 't1')").run();
            db.prepare("INSERT INTO event_tags VALUES ('e1', 't2')").run();
        });

        test('should suggest tags based on category', async () => {
            const result = await ragService.suggestTags('e2', 5);

            expect(result.success).toBe(true);
            expect(result.suggestions).toBeDefined();
            expect(Array.isArray(result.suggestions)).toBe(true);
        });

        test('should limit number of suggestions', async () => {
            const result = await ragService.suggestTags('e2', 2);

            expect(result.success).toBe(true);
            expect(result.suggestions.length).toBeLessThanOrEqual(2);
        });
    });

    describe('analyzeFullTimeline', () => {
        beforeEach(async () => {
            // Insert events
            db.prepare("INSERT INTO events VALUES ('e1', 'Event 1', 'Desc 1', 'work', '2020-01-01')").run();
            db.prepare("INSERT INTO events VALUES ('e2', 'Event 2', 'Desc 2', 'milestone', '2020-02-01')").run();
            db.prepare("INSERT INTO events VALUES ('e3', 'Event 3', 'Desc 3', 'work', '2020-03-01')").run();

            // Generate embeddings
            for (let i = 1; i <= 3; i++) {
                await embeddingService.generateEventEmbedding(`e${i}`, {
                    title: `Event ${i}`,
                    description: `Desc ${i}`
                });
            }
        });

        test('should analyze timeline for cross-references', async () => {
            // Mock LLM response for analyzeEventCrossReferences
            const originalAnalyze = ragService.analyzeEventCrossReferences;
            ragService.analyzeEventCrossReferences = jest.fn().mockResolvedValue({
                success: true,
                cross_references: []
            });

            const result = await ragService.analyzeFullTimeline(0.5);

            expect(result.success).toBe(true);
            expect(result.total_events).toBe(3);

            // Restore
            ragService.analyzeEventCrossReferences = originalAnalyze;
        });
    });

    describe('relationship types', () => {
        test('should support all relationship types', () => {
            const types = ['causal', 'thematic', 'temporal', 'person', 'location', 'other'];

            types.forEach(type => {
                db.prepare("INSERT INTO events VALUES (?, 'Event', 'Desc', 'work', '2020-01-01')").run(`e${type}1`);
                db.prepare("INSERT INTO events VALUES (?, 'Event', 'Desc', 'work', '2020-01-01')").run(`e${type}2`);

                db.prepare(`
                    INSERT INTO cross_references (reference_id, source_event_id, target_event_id, relationship_type, confidence_score)
                    VALUES (?, ?, ?, ?, 0.8)
                `).run(`r${type}`, `e${type}1`, `e${type}2`, type);

                const refs = ragService.getCrossReferences(`e${type}1`);
                expect(refs).toHaveLength(1);
                expect(refs[0].relationship_type).toBe(type);
            });
        });
    });
});
