/**
 * Queue Panel Component
 * Displays processing queue and review queue
 */

import React, { useState, useEffect } from 'react';

function QueuePanel() {
    const [activeTab, setActiveTab] = useState('outgoing'); // outgoing or review
    const [outgoingQueue, setOutgoingQueue] = useState([]);
    const [reviewQueue, setReviewQueue] = useState([]);

    useEffect(() => {
        loadQueues();
    }, []);

    const loadQueues = async () => {
        // TODO: Load from database
        console.log('Loading queues...');
    };

    return (
        <div className="queue-panel">
            <div className="queue-header">
                <h2>Processing Queue</h2>
                <button className="button secondary" onClick={loadQueues}>
                    Refresh
                </button>
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
                                        <div className="queue-item-info">
                                            <span className="filename">{item.audio_file_path}</span>
                                            <span className="status">{item.status}</span>
                                        </div>
                                        <audio controls src={item.audio_file_path} />
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
                                        <div className="review-audio">
                                            <audio controls src={item.audio_file_path} />
                                        </div>
                                        <div className="review-data">
                                            <h4>Extracted Event Data</h4>
                                            {/* TODO: Display extracted data */}
                                        </div>
                                        <div className="review-actions">
                                            <button className="button danger">Reject</button>
                                            <button className="button secondary">Edit</button>
                                            <button className="button primary">Approve</button>
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
