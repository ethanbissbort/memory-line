/**
 * Main Application Component
 */

import React, { useState, useEffect } from 'react';
import Timeline from './components/timeline/Timeline';
import Sidebar from './components/common/Sidebar';
import Header from './components/common/Header';
import EventDetailsModal from './components/events/EventDetailsModal';
import AudioRecorder from './components/audio/AudioRecorder';
import QueuePanel from './components/audio/QueuePanel';
import SettingsPanel from './components/settings/SettingsPanel';
import { useTimelineStore } from './store/timelineStore';
import { useSettingsStore } from './store/settingsStore';

function App() {
    const [activePanel, setActivePanel] = useState('timeline'); // timeline, recorder, queue, settings
    const [selectedEvent, setSelectedEvent] = useState(null);
    const [isLoading, setIsLoading] = useState(true);

    const { loadEvents, loadEras } = useTimelineStore();
    const { loadSettings } = useSettingsStore();

    useEffect(() => {
        // Initialize app - load settings and initial data
        const initializeApp = async () => {
            try {
                await loadSettings();
                await loadEras();

                // Load initial event range (current year)
                const now = new Date();
                const startOfYear = new Date(now.getFullYear(), 0, 1).toISOString().split('T')[0];
                const endOfYear = new Date(now.getFullYear(), 11, 31).toISOString().split('T')[0];

                await loadEvents(startOfYear, endOfYear);
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
    );
}

export default App;
