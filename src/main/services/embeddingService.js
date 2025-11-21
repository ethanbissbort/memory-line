/**
 * Embedding Service
 * Handles vector embedding generation for semantic similarity search
 * Supports multiple embedding providers: OpenAI, Voyage AI, Cohere, and local models
 */

const { v4: uuidv4 } = require('uuid');

/**
 * Cosine similarity calculation between two vectors
 * @param {number[]} vecA - First vector
 * @param {number[]} vecB - Second vector
 * @returns {number} Similarity score between 0 and 1
 */
function cosineSimilarity(vecA, vecB) {
    if (vecA.length !== vecB.length) {
        throw new Error('Vectors must have the same dimension');
    }

    let dotProduct = 0;
    let normA = 0;
    let normB = 0;

    for (let i = 0; i < vecA.length; i++) {
        dotProduct += vecA[i] * vecB[i];
        normA += vecA[i] * vecA[i];
        normB += vecB[i] * vecB[i];
    }

    return dotProduct / (Math.sqrt(normA) * Math.sqrt(normB));
}

class EmbeddingService {
    constructor(db) {
        this.db = db;
        this.provider = null;
        this.model = null;
        this.apiKey = null;
        this.dimension = null;
    }

    /**
     * Initialize the embedding service with provider configuration
     * @param {string} provider - 'openai', 'voyage', 'cohere', or 'local'
     * @param {string} model - Model name
     * @param {string} apiKey - API key for the provider (if needed)
     */
    async initialize(provider, model, apiKey = null) {
        this.provider = provider;
        this.model = model;
        this.apiKey = apiKey;

        // Set dimension based on provider and model
        switch (provider) {
            case 'openai':
                if (model === 'text-embedding-ada-002') {
                    this.dimension = 1536;
                } else if (model === 'text-embedding-3-small') {
                    this.dimension = 1536;
                } else if (model === 'text-embedding-3-large') {
                    this.dimension = 3072;
                } else {
                    this.dimension = 1536; // default
                }
                break;
            case 'voyage':
                if (model === 'voyage-2') {
                    this.dimension = 1024;
                } else if (model === 'voyage-large-2') {
                    this.dimension = 1536;
                } else {
                    this.dimension = 1024; // default
                }
                break;
            case 'cohere':
                this.dimension = 1024; // Cohere embed-english-v3.0
                break;
            case 'local':
                this.dimension = 384; // Default for sentence-transformers/all-MiniLM-L6-v2
                break;
            default:
                throw new Error(`Unsupported embedding provider: ${provider}`);
        }

        console.log(`Embedding service initialized: ${provider}/${model} (dim: ${this.dimension})`);
    }

    /**
     * Generate embedding for text using configured provider
     * @param {string} text - Text to embed
     * @returns {Promise<number[]>} Embedding vector
     */
    async generateEmbedding(text) {
        if (!this.provider) {
            throw new Error('Embedding service not initialized');
        }

        switch (this.provider) {
            case 'openai':
                return await this._generateOpenAIEmbedding(text);
            case 'voyage':
                return await this._generateVoyageEmbedding(text);
            case 'cohere':
                return await this._generateCohereEmbedding(text);
            case 'local':
                return await this._generateLocalEmbedding(text);
            default:
                throw new Error(`Unsupported provider: ${this.provider}`);
        }
    }

    /**
     * Generate embedding using OpenAI API
     */
    async _generateOpenAIEmbedding(text) {
        if (!this.apiKey) {
            throw new Error('OpenAI API key not configured');
        }

        const response = await fetch('https://api.openai.com/v1/embeddings', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${this.apiKey}`
            },
            body: JSON.stringify({
                model: this.model,
                input: text
            })
        });

        if (!response.ok) {
            const error = await response.text();
            throw new Error(`OpenAI API error: ${error}`);
        }

        const data = await response.json();
        return data.data[0].embedding;
    }

    /**
     * Generate embedding using Voyage AI API
     */
    async _generateVoyageEmbedding(text) {
        if (!this.apiKey) {
            throw new Error('Voyage AI API key not configured');
        }

        const response = await fetch('https://api.voyageai.com/v1/embeddings', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${this.apiKey}`
            },
            body: JSON.stringify({
                model: this.model,
                input: [text]
            })
        });

        if (!response.ok) {
            const error = await response.text();
            throw new Error(`Voyage AI API error: ${error}`);
        }

        const data = await response.json();
        return data.data[0].embedding;
    }

    /**
     * Generate embedding using Cohere API
     */
    async _generateCohereEmbedding(text) {
        if (!this.apiKey) {
            throw new Error('Cohere API key not configured');
        }

        const response = await fetch('https://api.cohere.ai/v1/embed', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${this.apiKey}`
            },
            body: JSON.stringify({
                model: this.model || 'embed-english-v3.0',
                texts: [text],
                input_type: 'search_document'
            })
        });

        if (!response.ok) {
            const error = await response.text();
            throw new Error(`Cohere API error: ${error}`);
        }

        const data = await response.json();
        return data.embeddings[0];
    }

    /**
     * Generate embedding using local model (mock implementation)
     * TODO: Implement actual local embedding using transformers.js or similar
     */
    async _generateLocalEmbedding(text) {
        // Mock implementation - returns random normalized vector
        console.warn('Using mock local embeddings. Implement transformers.js for production.');

        const vector = [];
        for (let i = 0; i < this.dimension; i++) {
            vector.push(Math.random() * 2 - 1);
        }

        // Normalize
        const norm = Math.sqrt(vector.reduce((sum, val) => sum + val * val, 0));
        return vector.map(val => val / norm);
    }

    /**
     * Generate and store embedding for an event
     * @param {string} eventId - Event ID
     * @param {Object} eventData - Event data containing title, description, transcript
     * @returns {Promise<Object>} Result with embedding_id
     */
    async generateEventEmbedding(eventId, eventData) {
        try {
            // Combine event text fields for embedding
            const textParts = [];
            if (eventData.title) textParts.push(eventData.title);
            if (eventData.description) textParts.push(eventData.description);
            if (eventData.raw_transcript) textParts.push(eventData.raw_transcript);

            const text = textParts.join(' ');

            if (!text.trim()) {
                throw new Error('No text available to embed');
            }

            // Generate embedding
            const embedding = await this.generateEmbedding(text);

            // Store in database
            const embeddingId = uuidv4();
            const stmt = this.db.prepare(`
                INSERT OR REPLACE INTO event_embeddings (
                    embedding_id, event_id, embedding_vector,
                    embedding_provider, embedding_model, embedding_dimension
                ) VALUES (?, ?, ?, ?, ?, ?)
            `);

            stmt.run(
                embeddingId,
                eventId,
                JSON.stringify(embedding),
                this.provider,
                this.model,
                this.dimension
            );

            console.log(`Generated embedding for event ${eventId}`);

            return {
                success: true,
                embedding_id: embeddingId,
                dimension: this.dimension
            };
        } catch (error) {
            console.error('Error generating event embedding:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }

    /**
     * Get embedding for an event
     * @param {string} eventId - Event ID
     * @returns {Object|null} Embedding data or null if not found
     */
    getEventEmbedding(eventId) {
        const stmt = this.db.prepare(`
            SELECT * FROM event_embeddings WHERE event_id = ?
        `);
        const result = stmt.get(eventId);

        if (result) {
            result.embedding_vector = JSON.parse(result.embedding_vector);
        }

        return result;
    }

    /**
     * Find similar events using cosine similarity
     * @param {string} eventId - Source event ID
     * @param {number} threshold - Minimum similarity threshold (0-1)
     * @param {number} limit - Maximum number of results
     * @returns {Promise<Array>} Array of similar events with similarity scores
     */
    async findSimilarEvents(eventId, threshold = 0.75, limit = 10) {
        try {
            // Get source event embedding
            const sourceEmbedding = this.getEventEmbedding(eventId);
            if (!sourceEmbedding) {
                throw new Error('Source event has no embedding');
            }

            const sourceVector = sourceEmbedding.embedding_vector;

            // Get all other event embeddings
            const stmt = this.db.prepare(`
                SELECT e.event_id, e.title, e.start_date, e.end_date,
                       e.description, e.category, em.embedding_vector
                FROM events e
                INNER JOIN event_embeddings em ON e.event_id = em.event_id
                WHERE e.event_id != ?
            `);

            const allEvents = stmt.all(eventId);

            // Calculate similarities
            const similarities = allEvents.map(event => {
                const targetVector = JSON.parse(event.embedding_vector);
                const similarity = cosineSimilarity(sourceVector, targetVector);

                return {
                    event_id: event.event_id,
                    title: event.title,
                    start_date: event.start_date,
                    end_date: event.end_date,
                    description: event.description,
                    category: event.category,
                    similarity_score: similarity
                };
            });

            // Filter by threshold and sort by similarity
            const filtered = similarities
                .filter(item => item.similarity_score >= threshold)
                .sort((a, b) => b.similarity_score - a.similarity_score)
                .slice(0, limit);

            return {
                success: true,
                similar_events: filtered,
                count: filtered.length
            };
        } catch (error) {
            console.error('Error finding similar events:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }

    /**
     * Generate embeddings for all events without embeddings
     * @returns {Promise<Object>} Result with success/failure counts
     */
    async generateAllMissingEmbeddings() {
        try {
            // Get all events without embeddings
            const stmt = this.db.prepare(`
                SELECT e.* FROM events e
                LEFT JOIN event_embeddings em ON e.event_id = em.event_id
                WHERE em.embedding_id IS NULL
            `);

            const eventsWithoutEmbeddings = stmt.all();
            console.log(`Found ${eventsWithoutEmbeddings.length} events without embeddings`);

            let succeeded = 0;
            let failed = 0;
            const errors = [];

            for (const event of eventsWithoutEmbeddings) {
                const result = await this.generateEventEmbedding(event.event_id, event);
                if (result.success) {
                    succeeded++;
                } else {
                    failed++;
                    errors.push({ event_id: event.event_id, error: result.error });
                }

                // Add small delay to avoid rate limiting
                await new Promise(resolve => setTimeout(resolve, 100));
            }

            return {
                success: true,
                total: eventsWithoutEmbeddings.length,
                succeeded,
                failed,
                errors
            };
        } catch (error) {
            console.error('Error generating all embeddings:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }

    /**
     * Delete embedding for an event
     * @param {string} eventId - Event ID
     */
    deleteEventEmbedding(eventId) {
        const stmt = this.db.prepare(`
            DELETE FROM event_embeddings WHERE event_id = ?
        `);
        stmt.run(eventId);
    }

    /**
     * Clear all embeddings (useful when changing providers)
     */
    clearAllEmbeddings() {
        const stmt = this.db.prepare(`DELETE FROM event_embeddings`);
        stmt.run();
        console.log('All embeddings cleared');
    }
}

module.exports = EmbeddingService;
