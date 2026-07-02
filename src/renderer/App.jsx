/**
 * Main Application Component
 */

import React, { useState, useEffect } from 'react';
import ErrorBoundary from './components/common/ErrorBoundary';
import Timeline from './components/timeline/Timeline';
import Sidebar from './components/common/Sidebar';
import Header from './components/common/Header';
import EventDetailsModal from './components/events/EventDetailsModal';
import AudioRecorder from './components/audio/AudioRecorder';
import QueuePanel from './components/audio/QueuePanel';
import SettingsPanel from './components/settings/SettingsPanel';
import SearchPanel from './components/search/SearchPanel';
import AnalyticsDashboard from './components/analytics/AnalyticsDashboard';
import CrossReferencePanel from './components/rag/CrossReferencePanel';
import RAGSettings from './components/rag/RAGSettings';
import { useTimelineStore } from './store/timelineStore';
import { useSettingsStore } from './store/settingsStore';

function App() {
    const [activePanel, setActivePanel] = useState('timeline'); // timeline, recorder, queue, settings
    const [selectedEvent, setSelectedEvent] = useState(null);
    const [isLoading, setIsLoading] = useState(true);

    const { loadEras } = useTimelineStore();
    const { loadSettings } = useSettingsStore();

    useEffect(() => {
        // Initialize app - load settings and eras. The initial event range is
        // loaded by Timeline.jsx on mount (based on the current view/zoom), so we
        // don't load events here to avoid a redundant/overridden fetch.
        const initializeApp = async () => {
            try {
                await loadSettings();
                await loadEras();
                setIsLoading(false);
            } catch (error) {
                console.error('Failed to initialize app:', error);
                setIsLoading(false);
            }
        };

        initializeApp();
    }, []);

    const handleEventClick = (event) => {
        setSelectedEvent(event);
    };

    const handleCloseEventModal = () => {
        setSelectedEvent(null);
    };

    if (isLoading) {
        return (
            <div className="app-loading">
                <div className="loading-spinner"></div>
                <p>Loading Memory Timeline...</p>
            </div>
        );
    }

    return (
        <ErrorBoundary>
            <div className="app">
                <Header activePanel={activePanel} setActivePanel={setActivePanel} />

                <div className="app-content">
                    <Sidebar activePanel={activePanel} setActivePanel={setActivePanel} />

                    <main className="main-content">
                        {activePanel === 'timeline' && (
                            <Timeline onEventClick={handleEventClick} />
                        )}

                        {activePanel === 'recorder' && (
                            <AudioRecorder />
                        )}

                        {activePanel === 'queue' && (
                            <QueuePanel />
                        )}

                        {activePanel === 'search' && (
                            <SearchPanel
                                onClose={() => setActivePanel('timeline')}
                                onEventClick={handleEventClick}
                            />
                        )}

                        {activePanel === 'analytics' && (
                            <AnalyticsDashboard
                                onClose={() => setActivePanel('timeline')}
                            />
                        )}

                        {activePanel === 'insights' && (
                            <CrossReferencePanel eventId={selectedEvent?.event_id} />
                        )}

                        {activePanel === 'rag' && (
                            <RAGSettings />
                        )}

                        {activePanel === 'settings' && (
                            <SettingsPanel />
                        )}
                    </main>
                </div>

                {selectedEvent && (
                    <EventDetailsModal
                        event={selectedEvent}
                        onClose={handleCloseEventModal}
                    />
                )}
            </div>
        </ErrorBoundary>
    );
}

export default App;
