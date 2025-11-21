/**
 * RAG (Retrieval-Augmented Generation) Service
 * Handles cross-referencing, pattern detection, and tag suggestions
 */

const { v4: uuidv4 } = require('uuid');
const anthropicService = require('./anthropicService');

class RAGService {
    constructor(db, embeddingService) {
        this.db = db;
        this.embeddingService = embeddingService;
    }

    /**
     * Analyze an event and find cross-references with other events
     * @param {string} eventId - Event ID to analyze
     * @param {number} similarityThreshold - Minimum similarity score (0-1)
     * @returns {Promise<Object>} Analysis results with cross-references
     */
    async analyzeEventCrossReferences(eventId, similarityThreshold = 0.75) {
        try {
            // Get the source event
            const sourceEvent = this.db.prepare(`
                SELECT * FROM events WHERE event_id = ?
            `).get(eventId);

            if (!sourceEvent) {
                throw new Error('Event not found');
            }

            // Find similar events using embeddings
            const similarResult = await this.embeddingService.findSimilarEvents(
                eventId,
                similarityThreshold,
                20 // Get top 20 similar events
            );

            if (!similarResult.success || similarResult.similar_events.length === 0) {
                return {
                    success: true,
                    cross_references: [],
                    message: 'No similar events found'
                };
            }

            // Use LLM to analyze relationships
            const crossReferences = [];
            for (const similarEvent of similarResult.similar_events) {
                const analysis = await this._analyzePairRelationship(sourceEvent, similarEvent);
                if (analysis.hasRelationship) {
                    crossReferences.push({
                        event_id_2: similarEvent.event_id,
                        relationship_type: analysis.type,
                        confidence_score: analysis.confidence,
                        explanation: analysis.explanation,
                        similarity_score: similarEvent.similarity_score
                    });
                }
            }

            return {
                success: true,
                cross_references: crossReferences,
                source_event_id: eventId
            };
        } catch (error) {
            console.error('Error analyzing cross-references:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }

    /**
     * Analyze relationship between two events using LLM
     * @private
     */
    async _analyzePairRelationship(event1, event2) {
        if (!anthropicService.isInitialized()) {
            // Fallback to basic heuristics if LLM not available
            return this._basicRelationshipHeuristic(event1, event2);
        }

        const prompt = `Analyze the relationship between these two life events:

EVENT 1:
Title: ${event1.title}
Date: ${event1.start_date}${event1.end_date ? ` to ${event1.end_date}` : ''}
Description: ${event1.description || 'N/A'}
Category: ${event1.category || 'N/A'}

EVENT 2:
Title: ${event2.title}
Date: ${event2.start_date}${event2.end_date ? ` to ${event2.end_date}` : ''}
Description: ${event2.description || 'N/A'}
Category: ${event2.category || 'N/A'}

Determine if there is a meaningful relationship between these events. Return ONLY valid JSON:

{
  "hasRelationship": true/false,
  "type": "causal|thematic|temporal|person|location|other",
  "confidence": 0.85,
  "explanation": "Brief explanation of the relationship"
}

Relationship types:
- causal: Event 1 caused or led to Event 2
- thematic: Events share similar themes or topics
- temporal: Events are part of a sequence or pattern over time
- person: Events involve the same people
- location: Events occurred in the same place
- other: Other meaningful connection

Return false for hasRelationship if the events are not meaningfully connected.`;

        try {
            const response = await anthropicService.client.messages.create({
                model: 'claude-sonnet-4-20250514',
                max_tokens: 1000,
                temperature: 0.3,
                messages: [{
                    role: 'user',
                    content: prompt
                }]
            });

            const content = response.content[0].text;
            const jsonMatch = content.match(/\{[\s\S]*\}/);

            if (jsonMatch) {
                return JSON.parse(jsonMatch[0]);
            }

            return { hasRelationship: false };
        } catch (error) {
            console.error('Error analyzing relationship with LLM:', error);
            return this._basicRelationshipHeuristic(event1, event2);
        }
    }

    /**
     * Basic heuristic for relationship detection (fallback when LLM unavailable)
     * @private
     */
    _basicRelationshipHeuristic(event1, event2) {
        // Check for same category
        if (event1.category === event2.category && event1.category !== 'other') {
            return {
                hasRelationship: true,
                type: 'thematic',
                confidence: 0.6,
                explanation: `Both events are categorized as ${event1.category}`
            };
        }

        // Check for temporal proximity (within 30 days)
        const date1 = new Date(event1.start_date);
        const date2 = new Date(event2.start_date);
        const daysDiff = Math.abs((date2 - date1) / (1000 * 60 * 60 * 24));

        if (daysDiff <= 30) {
            return {
                hasRelationship: true,
                type: 'temporal',
                confidence: 0.5,
                explanation: `Events occurred within ${Math.round(daysDiff)} days of each other`
            };
        }

        return { hasRelationship: false };
    }

    /**
     * Store cross-reference in database
     * @param {string} eventId1 - First event ID
     * @param {string} eventId2 - Second event ID
     * @param {string} relationshipType - Type of relationship
     * @param {number} confidenceScore - Confidence score (0-1)
     * @param {string} explanation - Explanation of the relationship
     */
    storeCrossReference(eventId1, eventId2, relationshipType, confidenceScore, explanation) {
        // Ensure consistent ordering (event_id_1 < event_id_2)
        const [id1, id2] = eventId1 < eventId2 ? [eventId1, eventId2] : [eventId2, eventId1];

        const referenceId = uuidv4();
        const stmt = this.db.prepare(`
            INSERT OR REPLACE INTO cross_references (
                reference_id, event_id_1, event_id_2,
                relationship_type, confidence_score, analysis_details
            ) VALUES (?, ?, ?, ?, ?, ?)
        `);

        stmt.run(
            referenceId,
            id1,
            id2,
            relationshipType,
            confidenceScore,
            JSON.stringify({ explanation })
        );

        return referenceId;
    }

    /**
     * Get cross-references for an event
     * @param {string} eventId - Event ID
     * @returns {Array} Cross-references
     */
    getCrossReferences(eventId) {
        const stmt = this.db.prepare(`
            SELECT cr.*,
                   e1.title as event1_title, e1.start_date as event1_date,
                   e2.title as event2_title, e2.start_date as event2_date
            FROM cross_references cr
            INNER JOIN events e1 ON cr.event_id_1 = e1.event_id
            INNER JOIN events e2 ON cr.event_id_2 = e2.event_id
            WHERE cr.event_id_1 = ? OR cr.event_id_2 = ?
            ORDER BY cr.confidence_score DESC
        `);

        const results = stmt.all(eventId, eventId);

        return results.map(row => ({
            ...row,
            analysis_details: JSON.parse(row.analysis_details)
        }));
    }

    /**
     * Run full RAG analysis on entire timeline
     * @param {number} similarityThreshold - Minimum similarity threshold
     * @returns {Promise<Object>} Analysis results
     */
    async analyzeFullTimeline(similarityThreshold = 0.75) {
        try {
            console.log('Starting full timeline RAG analysis...');

            // Get all events with embeddings
            const events = this.db.prepare(`
                SELECT e.event_id FROM events e
                INNER JOIN event_embeddings em ON e.event_id = em.event_id
            `).all();

            let totalCrossRefs = 0;
            const errors = [];

            for (const event of events) {
                try {
                    const result = await this.analyzeEventCrossReferences(
                        event.event_id,
                        similarityThreshold
                    );

                    if (result.success) {
                        // Store cross-references
                        for (const crossRef of result.cross_references) {
                            this.storeCrossReference(
                                event.event_id,
                                crossRef.event_id_2,
                                crossRef.relationship_type,
                                crossRef.confidence_score,
                                crossRef.explanation
                            );
                            totalCrossRefs++;
                        }
                    }
                } catch (error) {
                    errors.push({ event_id: event.event_id, error: error.message });
                }

                // Add delay to avoid rate limiting
                await new Promise(resolve => setTimeout(resolve, 500));
            }

            console.log(`Timeline analysis complete. Found ${totalCrossRefs} cross-references.`);

            return {
                success: true,
                total_events: events.length,
                cross_references_found: totalCrossRefs,
                errors
            };
        } catch (error) {
            console.error('Error analyzing timeline:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }

    /**
     * Detect patterns in timeline (recurring events, cycles)
     * @returns {Promise<Object>} Detected patterns
     */
    async detectPatterns() {
        try {
            const patterns = [];

            // Pattern 1: Recurring categories
            const categoryPattern = this._detectCategoryPatterns();
            if (categoryPattern.length > 0) {
                patterns.push({
                    type: 'recurring_categories',
                    description: 'Events that occur repeatedly in certain categories',
                    patterns: categoryPattern
                });
            }

            // Pattern 2: Temporal clusters
            const temporalPattern = this._detectTemporalClusters();
            if (temporalPattern.length > 0) {
                patterns.push({
                    type: 'temporal_clusters',
                    description: 'Time periods with high event density',
                    patterns: temporalPattern
                });
            }

            // Pattern 3: Era transitions
            const transitionPattern = this._detectEraTransitions();
            if (transitionPattern.length > 0) {
                patterns.push({
                    type: 'era_transitions',
                    description: 'Significant transitions between life phases',
                    patterns: transitionPattern
                });
            }

            return {
                success: true,
                patterns
            };
        } catch (error) {
            console.error('Error detecting patterns:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }

    /**
     * Detect recurring category patterns
     * @private
     */
    _detectCategoryPatterns() {
        const stmt = this.db.prepare(`
            SELECT category, COUNT(*) as count
            FROM events
            WHERE category IS NOT NULL AND category != 'other'
            GROUP BY category
            HAVING count >= 3
            ORDER BY count DESC
        `);

        return stmt.all();
    }

    /**
     * Detect temporal event clusters
     * @private
     */
    _detectTemporalClusters() {
        // Find periods with high event density
        const stmt = this.db.prepare(`
            SELECT
                strftime('%Y-%m', start_date) as month,
                COUNT(*) as event_count
            FROM events
            GROUP BY month
            HAVING event_count >= 3
            ORDER BY event_count DESC
            LIMIT 10
        `);

        return stmt.all();
    }

    /**
     * Detect era transitions (milestone events near era boundaries)
     * @private
     */
    _detectEraTransitions() {
        const stmt = this.db.prepare(`
            SELECT
                e.event_id,
                e.title,
                e.start_date,
                e.category,
                er.name as era_name
            FROM events e
            INNER JOIN eras er ON e.era_id = er.era_id
            WHERE e.category IN ('milestone', 'achievement', 'challenge')
            ORDER BY e.start_date
        `);

        return stmt.all();
    }

    /**
     * Suggest tags for an event based on similar events
     * @param {string} eventId - Event ID
     * @param {number} limit - Maximum number of suggestions
     * @returns {Promise<Array>} Suggested tags with confidence scores
     */
    async suggestTags(eventId, limit = 5) {
        try {
            // Find similar events
            const similarResult = await this.embeddingService.findSimilarEvents(eventId, 0.7, 10);

            if (!similarResult.success || similarResult.similar_events.length === 0) {
                return { success: true, suggestions: [] };
            }

            // Get tags from similar events
            const similarEventIds = similarResult.similar_events.map(e => e.event_id);
            const placeholders = similarEventIds.map(() => '?').join(',');

            const stmt = this.db.prepare(`
                SELECT t.tag_name, COUNT(*) as frequency,
                       AVG(et.confidence_score) as avg_confidence
                FROM event_tags et
                INNER JOIN tags t ON et.tag_id = t.tag_id
                WHERE et.event_id IN (${placeholders})
                  AND et.event_id NOT IN (
                      SELECT event_id FROM event_tags WHERE event_id = ?
                  )
                GROUP BY t.tag_name
                ORDER BY frequency DESC, avg_confidence DESC
                LIMIT ?
            `);

            const suggestions = stmt.all(...similarEventIds, eventId, limit);

            return {
                success: true,
                suggestions: suggestions.map(s => ({
                    tag_name: s.tag_name,
                    confidence: Math.min(s.avg_confidence * (s.frequency / similarEventIds.length), 1.0),
                    frequency: s.frequency
                }))
            };
        } catch (error) {
            console.error('Error suggesting tags:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }
}

module.exports = RAGService;
