/**
 * RAGService Unit Tests
 */

const RAGService = require('../../src/main/services/ragService');
const EmbeddingService = require('../../src/main/services/embeddingService');
const {
    createTestDb,
    insertEvent,
    insertTag,
    linkTag,
    insertCrossReference
} = require('../helpers/createTestDb');

describe('RAGService', () => {
    let db;
    let embeddingService;
    let ragService;

    beforeEach(async () => {
        // Build the database from the canonical production schema
        db = createTestDb();

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
            insertEvent(db, { event_id: 'e1', title: 'Event 1', description: 'Desc 1', category: 'work', start_date: '2020-01-01' });
            insertEvent(db, { event_id: 'e2', title: 'Event 2', description: 'Desc 2', category: 'milestone', start_date: '2020-02-01' });

            insertCrossReference(db, {
                reference_id: 'r1', event_id_1: 'e1', event_id_2: 'e2',
                relationship_type: 'causal', confidence_score: 0.9,
                analysis_details: JSON.stringify({ explanation: 'e1 led to e2' })
            });

            const refs = ragService.getCrossReferences('e1');

            expect(refs).toHaveLength(1);
            expect(refs[0].event_id_1).toBe('e1');
            expect(refs[0].event_id_2).toBe('e2');
            expect(refs[0].relationship_type).toBe('causal');
            expect(refs[0].event1_title).toBe('Event 1');
            expect(refs[0].event2_title).toBe('Event 2');
            // analysis_details is JSON-parsed by the service
            expect(refs[0].analysis_details).toEqual({ explanation: 'e1 led to e2' });
        });

        test('should return references from either side of the pair', () => {
            insertEvent(db, { event_id: 'e1', title: 'Event 1', description: 'Desc 1', category: 'work', start_date: '2020-01-01' });
            insertEvent(db, { event_id: 'e2', title: 'Event 2', description: 'Desc 2', category: 'milestone', start_date: '2020-02-01' });
            insertCrossReference(db, {
                reference_id: 'r1', event_id_1: 'e1', event_id_2: 'e2',
                relationship_type: 'causal', confidence_score: 0.9
            });

            // Query by the second event of the ordered pair
            const refs = ragService.getCrossReferences('e2');
            expect(refs).toHaveLength(1);
            expect(refs[0].reference_id).toBe('r1');
        });

        test('should return empty array if no references', () => {
            insertEvent(db, { event_id: 'e1', title: 'Event 1', description: 'Desc 1', category: 'work', start_date: '2020-01-01' });

            const refs = ragService.getCrossReferences('e1');

            expect(refs).toEqual([]);
        });
    });

    describe('detectPatterns', () => {
        beforeEach(() => {
            // Insert events with patterns
            for (let i = 1; i <= 10; i++) {
                const category = i % 3 === 0 ? 'work' : 'milestone';
                insertEvent(db, {
                    event_id: `e${i}`, title: `Event ${i}`,
                    description: `Description ${i}`, category,
                    start_date: `2020-0${Math.ceil(i / 3)}-01`
                });
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
            insertEvent(db, { event_id: 'e1', title: 'Work Project', description: 'Project desc', category: 'work', start_date: '2020-01-01' });
            insertEvent(db, { event_id: 'e2', title: 'Work Meeting', description: 'Meeting desc', category: 'work', start_date: '2020-02-01' });

            insertTag(db, { tag_id: 't1', tag_name: 'important' });
            insertTag(db, { tag_id: 't2', tag_name: 'career' });

            linkTag(db, 'e1', 't1');
            linkTag(db, 'e1', 't2');
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
            insertEvent(db, { event_id: 'e1', title: 'Event 1', description: 'Desc 1', category: 'work', start_date: '2020-01-01' });
            insertEvent(db, { event_id: 'e2', title: 'Event 2', description: 'Desc 2', category: 'milestone', start_date: '2020-02-01' });
            insertEvent(db, { event_id: 'e3', title: 'Event 3', description: 'Desc 3', category: 'work', start_date: '2020-03-01' });

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
        test('should support all relationship types allowed by the schema CHECK', () => {
            const types = ['causal', 'thematic', 'temporal', 'person', 'location', 'other'];

            types.forEach(type => {
                insertEvent(db, { event_id: `e${type}1`, title: 'Event', description: 'Desc', category: 'work', start_date: '2020-01-01' });
                insertEvent(db, { event_id: `e${type}2`, title: 'Event', description: 'Desc', category: 'work', start_date: '2020-01-01' });

                insertCrossReference(db, {
                    reference_id: `r${type}`,
                    event_id_1: `e${type}1`, event_id_2: `e${type}2`,
                    relationship_type: type, confidence_score: 0.8
                });

                const refs = ragService.getCrossReferences(`e${type}1`);
                expect(refs).toHaveLength(1);
                expect(refs[0].relationship_type).toBe(type);
            });
        });

        test('storeCrossReference should order the pair and dedupe on re-store', () => {
            insertEvent(db, { event_id: 'a1', title: 'A', start_date: '2020-01-01' });
            insertEvent(db, { event_id: 'b1', title: 'B', start_date: '2020-02-01' });

            // Passed in "wrong" order: must be stored as (a1, b1) to satisfy
            // the schema CHECK (event_id_1 < event_id_2).
            const id1 = ragService.storeCrossReference('b1', 'a1', 'thematic', 0.6, 'first');
            const id2 = ragService.storeCrossReference('a1', 'b1', 'thematic', 0.9, 'updated');

            expect(id2).toBe(id1); // same row updated, not duplicated
            const rows = db.prepare('SELECT * FROM cross_references').all();
            expect(rows).toHaveLength(1);
            expect(rows[0].event_id_1).toBe('a1');
            expect(rows[0].event_id_2).toBe('b1');
            expect(rows[0].confidence_score).toBe(0.9);
            expect(JSON.parse(rows[0].analysis_details)).toEqual({ explanation: 'updated' });
        });
    });
});
