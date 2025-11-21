/**
 * Cross-Reference Panel Component
 * Displays related events and connections discovered through RAG analysis
 */

import React, { useState, useEffect } from 'react';
import { format, parseISO } from 'date-fns';

function CrossReferencePanel({ eventId }) {
    const [crossRefs, setCrossRefs] = useState([]);
    const [similarEvents, setSimilarEvents] = useState([]);
    const [suggestedTags, setSuggestedTags] = useState([]);
    const [isLoading, setIsLoading] = useState(false);
    const [isAnalyzing, setIsAnalyzing] = useState(false);
    const [activeTab, setActiveTab] = useState('connections'); // connections, similar, tags

    useEffect(() => {
        if (eventId) {
            loadCrossReferences();
            loadSimilarEvents();
            loadSuggestedTags();
        }
    }, [eventId]);

    const loadCrossReferences = async () => {
        try {
            const result = await window.electronAPI.rag.getCrossReferences(eventId);
            if (result.success) {
                setCrossRefs(result.data);
            }
        } catch (error) {
            console.error('Error loading cross-references:', error);
        }
    };

    const loadSimilarEvents = async () => {
        try {
            const result = await window.electronAPI.embedding.findSimilar(eventId, 0.7, 10);
            if (result.success) {
                setSimilarEvents(result.similar_events || []);
            }
        } catch (error) {
            console.error('Error loading similar events:', error);
        }
    };

    const loadSuggestedTags = async () => {
        try {
            const result = await window.electronAPI.rag.suggestTags(eventId, 5);
            if (result.success) {
                setSuggestedTags(result.suggestions || []);
            }
        } catch (error) {
            console.error('Error loading suggested tags:', error);
        }
    };

    const handleAnalyzeEvent = async () => {
        if (!confirm('Analyze this event for cross-references?\n\nThis will use your configured embedding provider (OpenAI, Voyage, etc.) and may incur API costs.')) {
            return;
        }

        setIsAnalyzing(true);
        try {
            const result = await window.electronAPI.rag.analyzeEvent(eventId, 0.75);
            if (result.success) {
                alert(`Analysis complete!\n\nFound ${result.cross_references.length} cross-references.`);
                await loadCrossReferences();
            } else {
                alert('Analysis failed: ' + result.error);
            }
        } catch (error) {
            console.error('Error analyzing event:', error);
            alert('Failed to analyze event: ' + error.message);
        } finally {
            setIsAnalyzing(false);
        }
    };

    const getRelationshipIcon = (type) => {
        const icons = {
            causal: '➡️',
            thematic: '🔗',
            temporal: '⏰',
            person: '👤',
            location: '📍',
            other: '🔷'
        };
        return icons[type] || '🔷';
    };

    const getRelationshipColor = (type) => {
        const colors = {
            causal: '#e74c3c',
            thematic: '#3498db',
            temporal: '#9b59b6',
            person: '#e67e22',
            location: '#27ae60',
            other: '#95a5a6'
        };
        return colors[type] || '#95a5a6';
    };

    if (!eventId) {
        return (
            <div className="cross-reference-panel">
                <div className="empty-state">
                    <p>Select an event to view connections and related events</p>
                </div>
            </div>
        );
    }

    return (
        <div className="cross-reference-panel">
            <div className="panel-header">
                <h3>Related Events & Connections</h3>
                <button
                    className="button primary small"
                    onClick={handleAnalyzeEvent}
                    disabled={isAnalyzing || isLoading}
                >
                    {isAnalyzing ? 'Analyzing...' : '🔍 Analyze Event'}
                </button>
            </div>

            <div className="rag-tabs">
                <button
                    className={`tab-button ${activeTab === 'connections' ? 'active' : ''}`}
                    onClick={() => setActiveTab('connections')}
                >
                    Connections ({crossRefs.length})
                </button>
                <button
                    className={`tab-button ${activeTab === 'similar' ? 'active' : ''}`}
                    onClick={() => setActiveTab('similar')}
                >
                    Similar Events ({similarEvents.length})
                </button>
                <button
                    className={`tab-button ${activeTab === 'tags' ? 'active' : ''}`}
                    onClick={() => setActiveTab('tags')}
                >
                    Suggested Tags ({suggestedTags.length})
                </button>
            </div>

            <div className="rag-content">
                {activeTab === 'connections' && (
                    <div className="connections-tab">
                        {crossRefs.length === 0 ? (
                            <div className="empty-state">
                                <p>No cross-references found yet</p>
                                <p className="hint">Click "Analyze Event" to discover connections with other events</p>
                            </div>
                        ) : (
                            <div className="cross-ref-list">
                                {crossRefs.map(ref => {
                                    const relatedEventId = ref.event_id_1 === eventId ? ref.event_id_2 : ref.event_id_1;
                                    const relatedTitle = ref.event_id_1 === eventId ? ref.event2_title : ref.event1_title;
                                    const relatedDate = ref.event_id_1 === eventId ? ref.event2_date : ref.event1_date;

                                    return (
                                        <div key={ref.reference_id} className="cross-ref-item">
                                            <div className="ref-header">
                                                <span
                                                    className="ref-type-badge"
                                                    style={{ backgroundColor: getRelationshipColor(ref.relationship_type) }}
                                                >
                                                    {getRelationshipIcon(ref.relationship_type)} {ref.relationship_type}
                                                </span>
                                                <span className="ref-confidence">
                                                    {Math.round(ref.confidence_score * 100)}% confidence
                                                </span>
                                            </div>
                                            <div className="ref-event">
                                                <strong>{relatedTitle}</strong>
                                                <span className="ref-date">
                                                    {format(parseISO(relatedDate), 'MMM d, yyyy')}
                                                </span>
                                            </div>
                                            {ref.analysis_details && ref.analysis_details.explanation && (
                                                <div className="ref-explanation">
                                                    {ref.analysis_details.explanation}
                                                </div>
                                            )}
                                        </div>
                                    );
                                })}
                            </div>
                        )}
                    </div>
                )}

                {activeTab === 'similar' && (
                    <div className="similar-tab">
                        {similarEvents.length === 0 ? (
                            <div className="empty-state">
                                <p>No similar events found</p>
                                <p className="hint">This event may not have embeddings generated yet, or there are no semantically similar events.</p>
                            </div>
                        ) : (
                            <div className="similar-list">
                                {similarEvents.map(event => (
                                    <div key={event.event_id} className="similar-item">
                                        <div className="similar-header">
                                            <strong>{event.title}</strong>
                                            <span className="similarity-score">
                                                {Math.round(event.similarity_score * 100)}% similar
                                            </span>
                                        </div>
                                        <div className="similar-meta">
                                            <span className="similar-date">
                                                {format(parseISO(event.start_date), 'MMM d, yyyy')}
                                            </span>
                                            {event.category && (
                                                <span className="similar-category">{event.category}</span>
                                            )}
                                        </div>
                                        {event.description && (
                                            <div className="similar-description">
                                                {event.description.substring(0, 150)}
                                                {event.description.length > 150 && '...'}
                                            </div>
                                        )}
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>
                )}

                {activeTab === 'tags' && (
                    <div className="tags-tab">
                        {suggestedTags.length === 0 ? (
                            <div className="empty-state">
                                <p>No tag suggestions available</p>
                                <p className="hint">Tag suggestions are based on similar events. Add more events to get better suggestions.</p>
                            </div>
                        ) : (
                            <div className="suggested-tags-list">
                                <p className="tags-hint">These tags are commonly used on similar events:</p>
                                {suggestedTags.map((tag, idx) => (
                                    <div key={idx} className="suggested-tag-item">
                                        <div className="tag-info">
                                            <span className="tag-name">{tag.tag_name}</span>
                                            <span className="tag-confidence">
                                                {Math.round(tag.confidence * 100)}% confidence
                                            </span>
                                        </div>
                                        <button
                                            className="button secondary small"
                                            onClick={() => {
                                                // TODO: Implement tag addition
                                                alert('Tag addition will be implemented in the event editing flow');
                                            }}
                                        >
                                            Add Tag
                                        </button>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>
                )}
            </div>
        </div>
    );
}

export default CrossReferencePanel;
