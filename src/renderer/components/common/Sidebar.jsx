/**
 * Sidebar Component
 * Left sidebar with navigation and quick stats
 */

import React, { useState, useEffect } from 'react';

function Sidebar({ activePanel, setActivePanel }) {
    const [stats, setStats] = useState(null);

    useEffect(() => {
        loadStats();
    }, []);

    const loadStats = async () => {
        try {
            const result = await window.electronAPI.db.getStats();
            if (result.success) {
                setStats(result.data);
            }
        } catch (error) {
            console.error('Failed to load stats:', error);
        }
    };

    return (
        <aside className="sidebar">
            <div className="sidebar-section">
                <h3>Quick Stats</h3>
                {stats ? (
                    <div className="stats-list">
                        <div className="stat-item">
                            <span className="stat-label">Events:</span>
                            <span className="stat-value">{stats.events}</span>
                        </div>
                        <div className="stat-item">
                            <span className="stat-label">Eras:</span>
                            <span className="stat-value">{stats.eras}</span>
                        </div>
                        <div className="stat-item">
                            <span className="stat-label">Tags:</span>
                            <span className="stat-value">{stats.tags}</span>
                        </div>
                        <div className="stat-item">
                            <span className="stat-label">Pending Reviews:</span>
                            <span className="stat-value">{stats.pendingEvents}</span>
                        </div>
                        <div className="stat-item">
                            <span className="stat-label">Queue:</span>
                            <span className="stat-value">{stats.queuedRecordings}</span>
                        </div>
                    </div>
                ) : (
                    <p>Loading...</p>
                )}
            </div>

            <div className="sidebar-section">
                <button
                    className="sidebar-button"
                    onClick={() => setActivePanel('timeline')}
                >
                    + New Event
                </button>
                <button
                    className="sidebar-button secondary"
                    onClick={loadStats}
                >
                    Refresh Stats
                </button>
            </div>
        </aside>
    );
}

export default Sidebar;
