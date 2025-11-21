/**
 * Timeline State Management
 * Using Zustand for lightweight state management
 */

import { create } from 'zustand';

export const useTimelineStore = create((set, get) => ({
    // State
    events: [],
    eras: [],
    filteredEvents: [],
    selectedDateRange: {
        start: null,
        end: null
    },
    zoomLevel: 'month', // year, month, week, day
    currentViewDate: new Date(),
    isLoading: false,
    error: null,

    // Actions
    loadEvents: async (startDate, endDate, limit = 1000) => {
        set({ isLoading: true, error: null });
        try {
            const result = await window.electronAPI.events.getRange(startDate, endDate, limit);

            if (result.success) {
                set({
                    events: result.data,
                    filteredEvents: result.data,
                    selectedDateRange: { start: startDate, end: endDate },
                    isLoading: false
                });
            } else {
                set({ error: result.error, isLoading: false });
            }
        } catch (error) {
            set({ error: error.message, isLoading: false });
        }
    },

    loadEras: async () => {
        try {
            const result = await window.electronAPI.eras.getAll();

            if (result.success) {
                set({ eras: result.data });
            } else {
                console.error('Failed to load eras:', result.error);
            }
        } catch (error) {
            console.error('Failed to load eras:', error);
        }
    },

    createEvent: async (eventData) => {
        try {
            const result = await window.electronAPI.events.create(eventData);

            if (result.success) {
                // Reload events to include the new one
                const { start, end } = get().selectedDateRange;
                if (start && end) {
                    await get().loadEvents(start, end);
                }
                return { success: true, eventId: result.data.event_id };
            } else {
                return { success: false, error: result.error };
            }
        } catch (error) {
            return { success: false, error: error.message };
        }
    },

    updateEvent: async (eventId, updates) => {
        try {
            const result = await window.electronAPI.events.update(eventId, updates);

            if (result.success) {
                // Update local state
                set(state => ({
                    events: state.events.map(e =>
                        e.event_id === eventId ? { ...e, ...updates } : e
                    ),
                    filteredEvents: state.filteredEvents.map(e =>
                        e.event_id === eventId ? { ...e, ...updates } : e
                    )
                }));
                return { success: true };
            } else {
                return { success: false, error: result.error };
            }
        } catch (error) {
            return { success: false, error: error.message };
        }
    },

    deleteEvent: async (eventId) => {
        try {
            const result = await window.electronAPI.events.delete(eventId);

            if (result.success) {
                // Remove from local state
                set(state => ({
                    events: state.events.filter(e => e.event_id !== eventId),
                    filteredEvents: state.filteredEvents.filter(e => e.event_id !== eventId)
                }));
                return { success: true };
            } else {
                return { success: false, error: result.error };
            }
        } catch (error) {
            return { success: false, error: error.message };
        }
    },

    searchEvents: async (query) => {
        set({ isLoading: true, error: null });
        try {
            const result = await window.electronAPI.events.search(query, 50);

            if (result.success) {
                set({
                    filteredEvents: result.data,
                    isLoading: false
                });
            } else {
                set({ error: result.error, isLoading: false });
            }
        } catch (error) {
            set({ error: error.message, isLoading: false });
        }
    },

    setZoomLevel: (level) => {
        set({ zoomLevel: level });
    },

    setCurrentViewDate: (date) => {
        set({ currentViewDate: date });
    },

    filterEvents: (filterFn) => {
        const { events } = get();
        set({ filteredEvents: events.filter(filterFn) });
    },

    clearFilter: () => {
        const { events } = get();
        set({ filteredEvents: events });
    },

    // Era management
    createEra: async (eraData) => {
        try {
            const result = await window.electronAPI.eras.create(eraData);

            if (result.success) {
                await get().loadEras();
                return { success: true, eraId: result.data.era_id };
            } else {
                return { success: false, error: result.error };
            }
        } catch (error) {
            return { success: false, error: error.message };
        }
    },

    updateEra: async (eraId, updates) => {
        try {
            const result = await window.electronAPI.eras.update(eraId, updates);

            if (result.success) {
                await get().loadEras();
                return { success: true };
            } else {
                return { success: false, error: result.error };
            }
        } catch (error) {
            return { success: false, error: error.message };
        }
    },

    deleteEra: async (eraId) => {
        try {
            const result = await window.electronAPI.eras.delete(eraId);

            if (result.success) {
                await get().loadEras();
                return { success: true };
            } else {
                return { success: false, error: result.error };
            }
        } catch (error) {
            return { success: false, error: error.message };
        }
    }
}));
