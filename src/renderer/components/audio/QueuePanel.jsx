/**
 * Queue Panel Component
 * Displays processing queue and review queue
 */

import React, { useState, useEffect } from 'react';
import { format, parseISO } from 'date-fns';

function QueuePanel() {
    const [activeTab, setActiveTab] = useState('outgoing'); // outgoing or review
    const [outgoingQueue, setOutgoingQueue] = useState([]);
    const [reviewQueue, setReviewQueue] = useState([]);
    const [audioUrls, setAudioUrls] = useState({});
    const [isLoading, setIsLoading] = useState(false);
    const [isProcessing, setIsProcessing] = useState(false);

    useEffect(() => {
        loadQueues();
    }, []);

    const loadQueues = async () => {
        setIsLoading(true);
        try {
            // Load outgoing queue
            const outgoingResult = await window.electronAPI.queue.getAll();
            if (outgoingResult.success) {
                setOutgoingQueue(outgoingResult.data);

                // Load audio files for playback
                const urls = {};
                for (const item of outgoingResult.data) {
                    const audioResult = await window.electronAPI.audio.getFile(item.audio_file_path);
                    if (audioResult.success) {
                        urls[item.queue_id] = audioResult.data;
                    }
                }
                setAudioUrls(urls);
            }

            // Load review queue
            const reviewResult = await window.electronAPI.pending.getAll('pending_review');
            if (reviewResult.success) {
                setReviewQueue(reviewResult.data);
            }
        } catch (error) {
            console.error('Error loading queues:', error);
            alert('Failed to load queues: ' + error.message);
        } finally {
            setIsLoading(false);
        }
    };

    const handleRemoveFromQueue = async (queueId) => {
        if (!confirm('Are you sure you want to remove this recording from the queue?')) {
            return;
        }

        try {
            const result = await window.electronAPI.queue.remove(queueId);
            if (result.success) {
                loadQueues(); // Reload queues
            } else {
                alert('Failed to remove item: ' + result.error);
            }
        } catch (error) {
            console.error('Error removing from queue:', error);
            alert('Failed to remove item: ' + error.message);
        }
    };

    const handleApprove = async (pendingId, extractedData) => {
        try {
            const result = await window.electronAPI.pending.approve(pendingId, extractedData);
            if (result.success) {
                alert('Event approved and added to timeline!');
                loadQueues(); // Reload queues
            } else {
                alert('Failed to approve event: ' + result.error);
            }
        } catch (error) {
            console.error('Error approving event:', error);
            alert('Failed to approve event: ' + error.message);
        }
    };

    const handleReject = async (pendingId) => {
        if (!confirm('Are you sure you want to reject this event? This cannot be undone.')) {
            return;
        }

        try {
            const result = await window.electronAPI.pending.reject(pendingId);
            if (result.success) {
                alert('Event rejected');
                loadQueues(); // Reload queues
            } else {
                alert('Failed to reject event: ' + result.error);
            }
        } catch (error) {
            console.error('Error rejecting event:', error);
            alert('Failed to reject event: ' + error.message);
        }
    };

    const handleProcessQueue = async () => {
        // Check if API key is set
        const apiKeyCheck = await window.electronAPI.llm.hasApiKey();
        if (!apiKeyCheck.success || !apiKeyCheck.hasKey) {
            alert('Please configure your Anthropic API key in Settings before processing recordings.');
            return;
        }

        const pendingCount = outgoingQueue.filter(item => item.status === 'pending').length;
        if (pendingCount === 0) {
            alert('No pending recordings to process.');
            return;
        }

        if (!confirm(`Process ${pendingCount} pending recording(s) with LLM extraction?\n\nThis will:\n1. Transcribe audio (mock for demo)\n2. Extract event data using Claude\n3. Create pending events for your review\n\nNote: This uses your Anthropic API credits.`)) {
            return;
        }

        setIsProcessing(true);
        try {
            const result = await window.electronAPI.llm.processAllPending();

            if (result.success) {
                const { results } = result;
                if (results) {
                    let message = `Processing complete!\n\nSucceeded: ${results.succeeded}\nFailed: ${results.failed}`;

                    if (results.errors && results.errors.length > 0) {
                        message += '\n\nErrors:\n' + results.errors.map(e => `- ${e.error}`).join('\n');
                    }

                    alert(message);
                } else {
                    alert(result.message || 'Processing complete!');
                }

                // Reload queues to show new pending events
                await loadQueues();
            } else {
                alert('Failed to process queue: ' + result.error);
            }
        } catch (error) {
            console.error('Error processing queue:', error);
            alert('Failed to process queue: ' + error.message);
        } finally {
            setIsProcessing(false);
        }
    };

    const formatFileSize = (bytes) => {
        if (!bytes) return '0 B';
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
    };

    const formatDuration = (seconds) => {
        if (!seconds) return '0:00';
        const mins = Math.floor(seconds / 60);
        const secs = Math.floor(seconds % 60);
        return `${mins}:${secs.toString().padStart(2, '0')}`;
    };

    const getStatusBadgeClass = (status) => {
        switch (status) {
            case 'pending': return 'status-badge pending';
            case 'processing': return 'status-badge processing';
            case 'completed': return 'status-badge completed';
            case 'failed': return 'status-badge failed';
            default: return 'status-badge';
        }
    };

    return (
        <div className="queue-panel">
            <div className="queue-header">
                <h2>Processing Queue</h2>
                <div className="queue-header-actions">
                    <button
                        className="button primary"
                        onClick={handleProcessQueue}
                        disabled={isProcessing || isLoading}
                    >
                        {isProcessing ? 'Processing...' : 'Process Queue with LLM'}
                    </button>
                    <button
                        className="button secondary"
                        onClick={loadQueues}
                        disabled={isLoading}
                    >
                        {isLoading ? 'Loading...' : 'Refresh'}
                    </button>
                </div>
            </div>

            <div className="queue-tabs">
                <button
                    className={`tab-button ${activeTab === 'outgoing' ? 'active' : ''}`}
                    onClick={() => setActiveTab('outgoing')}
                >
                    Outgoing ({outgoingQueue.length})
                </button>
                <button
                    className={`tab-button ${activeTab === 'review' ? 'active' : ''}`}
                    onClick={() => setActiveTab('review')}
                >
                    Review ({reviewQueue.length})
                </button>
            </div>

            <div className="queue-content">
                {activeTab === 'outgoing' && (
                    <div className="outgoing-queue">
                        {outgoingQueue.length === 0 ? (
                            <div className="empty-state">
                                <p>No recordings in queue</p>
                                <p className="hint">Record audio to add items to the queue</p>
                            </div>
                        ) : (
                            <div className="queue-list">
                                {outgoingQueue.map(item => (
                                    <div key={item.queue_id} className="queue-item">
                                        <div className="queue-item-header">
                                            <div className="queue-item-info">
                                                <strong>Recording</strong>
                                                <span className="timestamp">
                                                    {item.created_at && format(parseISO(item.created_at), 'MMM d, yyyy h:mm a')}
                                                </span>
                                            </div>
                                            <span className={getStatusBadgeClass(item.status)}>
                                                {item.status}
                                            </span>
                                        </div>

                                        <div className="queue-item-metadata">
                                            <span>Duration: {formatDuration(item.duration_seconds)}</span>
                                            <span>Size: {formatFileSize(item.file_size_bytes)}</span>
                                        </div>

                                        {audioUrls[item.queue_id] && (
                                            <audio
                                                controls
                                                src={audioUrls[item.queue_id]}
                                                className="queue-audio-player"
                                            />
                                        )}

                                        {item.error_message && (
                                            <div className="error-message">
                                                Error: {item.error_message}
                                            </div>
                                        )}

                                        <div className="queue-item-actions">
                                            <button
                                                className="button danger small"
                                                onClick={() => handleRemoveFromQueue(item.queue_id)}
                                            >
                                                Remove
                                            </button>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>
                )}

                {activeTab === 'review' && (
                    <div className="review-queue">
                        {reviewQueue.length === 0 ? (
                            <div className="empty-state">
                                <p>No events awaiting review</p>
                                <p className="hint">Processed recordings will appear here for your approval</p>
                            </div>
                        ) : (
                            <div className="review-list">
                                {reviewQueue.map(item => (
                                    <div key={item.pending_id} className="review-item">
                                        <div className="review-header">
                                            <h3>Extracted Event</h3>
                                            <span className="timestamp">
                                                {item.created_at && format(parseISO(item.created_at), 'MMM d, yyyy h:mm a')}
                                            </span>
                                        </div>

                                        {item.audio_file_path && audioUrls[item.pending_id] && (
                                            <div className="review-audio">
                                                <audio controls src={audioUrls[item.pending_id]} />
                                            </div>
                                        )}

                                        <div className="review-data">
                                            {item.extracted_data && (
                                                <div className="extracted-fields">
                                                    <div className="field">
                                                        <label>Title:</label>
                                                        <span>{item.extracted_data.title}</span>
                                                    </div>
                                                    <div className="field">
                                                        <label>Start Date:</label>
                                                        <span>{item.extracted_data.start_date}</span>
                                                    </div>
                                                    {item.extracted_data.end_date && (
                                                        <div className="field">
                                                            <label>End Date:</label>
                                                            <span>{item.extracted_data.end_date}</span>
                                                        </div>
                                                    )}
                                                    {item.extracted_data.description && (
                                                        <div className="field">
                                                            <label>Description:</label>
                                                            <p>{item.extracted_data.description}</p>
                                                        </div>
                                                    )}
                                                    {item.extracted_data.category && (
                                                        <div className="field">
                                                            <label>Category:</label>
                                                            <span>{item.extracted_data.category}</span>
                                                        </div>
                                                    )}
                                                    {item.extracted_data.suggested_tags && item.extracted_data.suggested_tags.length > 0 && (
                                                        <div className="field">
                                                            <label>Suggested Tags:</label>
                                                            <div className="tag-list">
                                                                {item.extracted_data.suggested_tags.map((tag, idx) => (
                                                                    <span key={idx} className="tag">{tag}</span>
                                                                ))}
                                                            </div>
                                                        </div>
                                                    )}
                                                    {item.extracted_data.confidence && (
                                                        <div className="field">
                                                            <label>Confidence:</label>
                                                            <span>{Math.round(item.extracted_data.confidence * 100)}%</span>
                                                        </div>
                                                    )}
                                                </div>
                                            )}

                                            {item.transcript && (
                                                <div className="transcript-section">
                                                    <label>Transcript:</label>
                                                    <p className="transcript">{item.transcript}</p>
                                                </div>
                                            )}
                                        </div>

                                        <div className="review-actions">
                                            <button
                                                className="button danger"
                                                onClick={() => handleReject(item.pending_id)}
                                            >
                                                Reject
                                            </button>
                                            <button
                                                className="button secondary"
                                                onClick={() => alert('Edit functionality coming in Phase 3!')}
                                            >
                                                Edit
                                            </button>
                                            <button
                                                className="button primary"
                                                onClick={() => handleApprove(item.pending_id, null)}
                                            >
                                                Approve
                                            </button>
                                        </div>
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

export default QueuePanel;
