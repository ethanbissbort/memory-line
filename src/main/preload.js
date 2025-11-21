/**
 * Preload Script
 * Exposes secure IPC methods to the renderer process
 */

const { contextBridge, ipcRenderer } = require('electron');

// Expose protected methods that allow the renderer process to use
// ipcRenderer without exposing the entire object
contextBridge.exposeInMainWorld('electronAPI', {
    // Database operations
    db: {
        getStats: () => ipcRenderer.invoke('db:getStats'),
        backup: () => ipcRenderer.invoke('db:backup'),
        vacuum: () => ipcRenderer.invoke('db:vacuum')
    },

    // Event operations
    events: {
        getRange: (startDate, endDate, limit) =>
            ipcRenderer.invoke('events:getRange', { startDate, endDate, limit }),
        getById: (eventId) =>
            ipcRenderer.invoke('events:getById', eventId),
        create: (eventData) =>
            ipcRenderer.invoke('events:create', eventData),
        update: (eventId, updates) =>
            ipcRenderer.invoke('events:update', { eventId, updates }),
        delete: (eventId) =>
            ipcRenderer.invoke('events:delete', eventId),
        search: (query, limit) =>
            ipcRenderer.invoke('events:search', { query, limit })
    },

    // Era operations
    eras: {
        getAll: () =>
            ipcRenderer.invoke('eras:getAll'),
        create: (eraData) =>
            ipcRenderer.invoke('eras:create', eraData),
        update: (eraId, updates) =>
            ipcRenderer.invoke('eras:update', { eraId, updates }),
        delete: (eraId) =>
            ipcRenderer.invoke('eras:delete', eraId)
    },

    // Settings operations
    settings: {
        getAll: () =>
            ipcRenderer.invoke('settings:getAll'),
        update: (key, value) =>
            ipcRenderer.invoke('settings:update', { key, value })
    }
});

console.log('Preload script loaded');
