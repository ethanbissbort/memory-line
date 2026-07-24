/**
 * EmbeddingService Unit Tests
 */

const EmbeddingService = require('../../src/main/services/embeddingService');
const { createTestDb, insertEvent, insertEmbedding } = require('../helpers/createTestDb');

// Mock fetch for API calls
global.fetch = jest.fn();

describe('EmbeddingService', () => {
    let db;
    let embeddingService;

    beforeEach(() => {
        // Build the database from the canonical production schema
        db = createTestDb();

        embeddingService = new EmbeddingService(db);

        // Clear mock
        fetch.mockClear();
    });

    afterEach(() => {
        db.close();
    });

    describe('initialization', () => {
        test('should initialize with OpenAI provider', async () => {
            await embeddingService.initialize('openai', 'text-embedding-ada-002', 'test-key');

            expect(embeddingService.provider).toBe('openai');
            expect(embeddingService.model).toBe('text-embedding-ada-002');
            expect(embeddingService.dimension).toBe(1536);
        });

        test('should initialize with Voyage provider', async () => {
            await embeddingService.initialize('voyage', 'voyage-2', 'test-key');

            expect(embeddingService.provider).toBe('voyage');
            expect(embeddingService.dimension).toBe(1024);
        });

        test('should initialize with Cohere provider', async () => {
            await embeddingService.initialize('cohere', 'embed-english-v3.0', 'test-key');

            expect(embeddingService.provider).toBe('cohere');
            expect(embeddingService.dimension).toBe(1024);
        });

        test('should initialize with local provider', async () => {
            await embeddingService.initialize('local', 'all-MiniLM-L6-v2');

            expect(embeddingService.provider).toBe('local');
            expect(embeddingService.dimension).toBe(384);
        });

        test('should throw error for unsupported provider', async () => {
            await expect(
                embeddingService.initialize('unsupported', 'model', 'key')
            ).rejects.toThrow();
        });
    });

    describe('generateEventEmbedding', () => {
        beforeEach(async () => {
            await embeddingService.initialize('local', 'test-model');
        });

        test('should generate embedding for event', async () => {
            // Insert test event
            insertEvent(db, {
                event_id: 'e1', title: 'Test Event', start_date: '2020-01-01',
                description: 'Test description'
            });

            const result = await embeddingService.generateEventEmbedding('e1', {
                title: 'Test Event',
                description: 'Test description'
            });

            expect(result.success).toBe(true);
            expect(result.embedding_id).toBeDefined();
            expect(result.dimension).toBe(384);

            // Verify stored in database
            const stored = db.prepare('SELECT * FROM event_embeddings WHERE event_id = ?').get('e1');
            expect(stored).toBeDefined();
            expect(stored.embedding_provider).toBe('local');
        });

        test('should combine multiple text fields', async () => {
            insertEvent(db, {
                event_id: 'e1', title: 'Title', start_date: '2020-01-01',
                description: 'Description', raw_transcript: 'Transcript'
            });

            const result = await embeddingService.generateEventEmbedding('e1', {
                title: 'Title',
                description: 'Description',
                raw_transcript: 'Transcript'
            });

            expect(result.success).toBe(true);
        });

        test('should handle empty text', async () => {
            const result = await embeddingService.generateEventEmbedding('e1', {});

            expect(result.success).toBe(false);
            expect(result.error).toContain('No text available');
        });

        test('should replace an existing embedding for the same event', async () => {
            insertEvent(db, {
                event_id: 'e1', title: 'Test Event', start_date: '2020-01-01'
            });

            await embeddingService.generateEventEmbedding('e1', { title: 'Test Event' });
            await embeddingService.generateEventEmbedding('e1', { title: 'Test Event v2' });

            const count = db.prepare(
                'SELECT COUNT(*) as count FROM event_embeddings WHERE event_id = ?'
            ).get('e1').count;
            expect(count).toBe(1);
        });
    });

    describe('findSimilarEvents (deterministic vectors)', () => {
        // cosineSimilarity is module-private, so it is exercised through
        // findSimilarEvents with embeddings inserted directly with known
        // vectors (the 'local' provider would generate random ones).
        beforeEach(async () => {
            await embeddingService.initialize('local', 'test-model');

            insertEvent(db, { event_id: 'e1', title: 'Event 1', start_date: '2020-01-01', description: 'Description 1' });
            insertEvent(db, { event_id: 'e2', title: 'Event 2', start_date: '2020-02-01', description: 'Description 2' });
            insertEvent(db, { event_id: 'e3', title: 'Event 3', start_date: '2020-03-01', description: 'Description 3' });
            insertEvent(db, { event_id: 'e4', title: 'Event 4', start_date: '2020-04-01', description: 'Description 4' });

            insertEmbedding(db, 'e1', [1, 0, 0]);
            insertEmbedding(db, 'e2', [2, 0, 0]); // same direction as e1 -> similarity 1
            insertEmbedding(db, 'e3', [0, 1, 0]); // orthogonal to e1     -> similarity 0
            insertEmbedding(db, 'e4', [1, 1, 0]); // 45 degrees to e1     -> similarity ~0.7071
        });

        test('should score same-direction vectors as 1 regardless of magnitude', async () => {
            const result = await embeddingService.findSimilarEvents('e1', 0.99, 10);

            expect(result.success).toBe(true);
            expect(result.similar_events.map(e => e.event_id)).toEqual(['e2']);
            expect(result.similar_events[0].similarity_score).toBeCloseTo(1.0, 5);
            expect(result.count).toBe(1);
        });

        test('should sort by similarity and score orthogonal vectors as 0', async () => {
            const result = await embeddingService.findSimilarEvents('e1', 0, 10);

            expect(result.success).toBe(true);
            expect(result.similar_events.map(e => e.event_id)).toEqual(['e2', 'e4', 'e3']);
            expect(result.similar_events[1].similarity_score).toBeCloseTo(Math.SQRT1_2, 5);
            expect(result.similar_events[2].similarity_score).toBeCloseTo(0, 5);
        });

        test('should filter by threshold', async () => {
            const result = await embeddingService.findSimilarEvents('e1', 0.5, 10);

            expect(result.success).toBe(true);
            expect(result.similar_events.map(e => e.event_id)).toEqual(['e2', 'e4']);
        });

        test('should limit results', async () => {
            const result = await embeddingService.findSimilarEvents('e1', 0.0, 1);

            expect(result.success).toBe(true);
            expect(result.similar_events.map(e => e.event_id)).toEqual(['e2']);
        });

        test('should treat dimension-mismatched vectors as similarity 0 instead of throwing', async () => {
            insertEvent(db, { event_id: 'e5', title: 'Event 5', start_date: '2020-05-01' });
            insertEmbedding(db, 'e5', [1, 0]); // 2-dim vs 3-dim source

            const all = await embeddingService.findSimilarEvents('e1', 0, 10);
            const e5 = all.similar_events.find(e => e.event_id === 'e5');
            expect(e5.similarity_score).toBe(0);

            const filtered = await embeddingService.findSimilarEvents('e1', 0.5, 10);
            expect(filtered.similar_events.map(e => e.event_id)).not.toContain('e5');
        });

        test('should return an error result when the source event has no embedding', async () => {
            const result = await embeddingService.findSimilarEvents('missing-event');

            expect(result.success).toBe(false);
            expect(result.error).toContain('no embedding');
        });
    });

    describe('getEventEmbedding / deleteEventEmbedding', () => {
        beforeEach(async () => {
            await embeddingService.initialize('local', 'test-model');
            insertEvent(db, { event_id: 'e1', title: 'Event 1', start_date: '2020-01-01' });
            insertEmbedding(db, 'e1', [0.25, 0.5, 0.75]);
        });

        test('getEventEmbedding should JSON-parse the stored vector', () => {
            const embedding = embeddingService.getEventEmbedding('e1');

            expect(embedding.event_id).toBe('e1');
            expect(embedding.embedding_vector).toEqual([0.25, 0.5, 0.75]);
            expect(embedding.embedding_provider).toBe('local');
            expect(embedding.embedding_dimension).toBe(3);
        });

        test('getEventEmbedding should return undefined for unknown event', () => {
            expect(embeddingService.getEventEmbedding('nope')).toBeUndefined();
        });

        test('deleteEventEmbedding should remove the row', () => {
            embeddingService.deleteEventEmbedding('e1');
            expect(embeddingService.getEventEmbedding('e1')).toBeUndefined();
        });
    });

    describe('clearAllEmbeddings', () => {
        test('should clear all embeddings', async () => {
            await embeddingService.initialize('local', 'test-model');

            insertEvent(db, { event_id: 'e1', title: 'Event 1', start_date: '2020-01-01', description: 'Desc' });
            await embeddingService.generateEventEmbedding('e1', { title: 'Event 1' });

            // Verify embedding exists
            let count = db.prepare('SELECT COUNT(*) as count FROM event_embeddings').get().count;
            expect(count).toBe(1);

            // Clear
            embeddingService.clearAllEmbeddings();

            // Verify cleared
            count = db.prepare('SELECT COUNT(*) as count FROM event_embeddings').get().count;
            expect(count).toBe(0);
        });
    });
});
