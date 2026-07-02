/**
 * Header Component
 * Top navigation bar with app title and quick actions
 */

import React from 'react';

function Header({ activePanel, setActivePanel }) {
    return (
        <header className="app-header">
            <div className="header-left">
                <h1 className="app-title">Memory Timeline</h1>
            </div>

            <nav className="header-nav">
                <button
                    className={`nav-button ${activePanel === 'timeline' ? 'active' : ''}`}
                    onClick={() => setActivePanel('timeline')}
                    title="View Timeline"
                >
                    Timeline
                </button>
                <button
                    className={`nav-button ${activePanel === 'recorder' ? 'active' : ''}`}
                    onClick={() => setActivePanel('recorder')}
                    title="Record Audio"
                >
                    Record
                </button>
                <button
                    className={`nav-button ${activePanel === 'queue' ? 'active' : ''}`}
                    onClick={() => setActivePanel('queue')}
                    title="View Queue"
                >
                    Queue
                </button>
                <button
                    className={`nav-button ${activePanel === 'search' ? 'active' : ''}`}
                    onClick={() => setActivePanel('search')}
                    title="Search Events"
                >
                    Search
                </button>
                <button
                    className={`nav-button ${activePanel === 'analytics' ? 'active' : ''}`}
                    onClick={() => setActivePanel('analytics')}
                    title="View Analytics"
                >
                    Analytics
                </button>
                <button
                    className={`nav-button ${activePanel === 'insights' ? 'active' : ''}`}
                    onClick={() => setActivePanel('insights')}
                    title="Related Events & Connections"
                >
                    Insights
                </button>
                <button
                    className={`nav-button ${activePanel === 'rag' ? 'active' : ''}`}
                    onClick={() => setActivePanel('rag')}
                    title="RAG & Embeddings"
                >
                    RAG
                </button>
                <button
                    className={`nav-button ${activePanel === 'settings' ? 'active' : ''}`}
                    onClick={() => setActivePanel('settings')}
                    title="Settings"
                >
                    Settings
                </button>
            </nav>

            <div className="header-right">
                <button className="icon-button" title="Help">
                    ?
                </button>
            </div>
        </header>
    );
}

export default Header;
