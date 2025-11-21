/**
 * Settings State Management
 */

import { create } from 'zustand';

export const useSettingsStore = create((set, get) => ({
    // State
    settings: {
        theme: 'light',
        default_zoom_level: 'month',
        audio_quality: 'high',
        llm_provider: 'anthropic',
        llm_model: 'claude-sonnet-4-20250514',
        llm_max_tokens: '4000',
        llm_temperature: '0.3',
        rag_auto_run_enabled: 'false',
        rag_schedule: 'weekly',
        rag_similarity_threshold: '0.75',
        send_transcripts_only: 'true',
        require_confirmation: 'true'
    },
    isLoading: false,
    error: null,

    // Actions
    loadSettings: async () => {
        set({ isLoading: true, error: null });
        try {
            const result = await window.electronAPI.settings.getAll();

            if (result.success) {
                set({
                    settings: result.data,
                    isLoading: false
                });
            } else {
                set({ error: result.error, isLoading: false });
            }
        } catch (error) {
            set({ error: error.message, isLoading: false });
        }
    },

    updateSetting: async (key, value) => {
        try {
            const result = await window.electronAPI.settings.update(key, value);

            if (result.success) {
                set(state => ({
                    settings: {
                        ...state.settings,
                        [key]: value
                    }
                }));
                return { success: true };
            } else {
                return { success: false, error: result.error };
            }
        } catch (error) {
            return { success: false, error: error.message };
        }
    },

    getSetting: (key) => {
        return get().settings[key];
    }
}));
