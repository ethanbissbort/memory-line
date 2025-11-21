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
    },

    // Audio operations
    audio: {
        save: (audioData, duration) =>
            ipcRenderer.invoke('audio:save', { audioData, duration }),
        getFile: (filePath) =>
            ipcRenderer.invoke('audio:getFile', filePath)
    },

    // Recording queue operations
    queue: {
        add: (filePath, duration, fileSize) =>
            ipcRenderer.invoke('queue:add', { filePath, duration, fileSize }),
        getAll: (status) =>
            ipcRenderer.invoke('queue:getAll', { status }),
        updateStatus: (queueId, status, errorMessage) =>
            ipcRenderer.invoke('queue:updateStatus', { queueId, status, errorMessage }),
        remove: (queueId) =>
            ipcRenderer.invoke('queue:remove', queueId)
    },

    // Pending events operations
    pending: {
        getAll: (status) =>
            ipcRenderer.invoke('pending:getAll', { status }),
        approve: (pendingId, editedData) =>
            ipcRenderer.invoke('pending:approve', { pendingId, editedData }),
        reject: (pendingId) =>
            ipcRenderer.invoke('pending:reject', pendingId)
    },

    // LLM operations
    llm: {
        setApiKey: (apiKey) =>
            ipcRenderer.invoke('llm:setApiKey', apiKey),
        hasApiKey: () =>
            ipcRenderer.invoke('llm:hasApiKey'),
        processQueueItem: (queueId) =>
            ipcRenderer.invoke('llm:processQueueItem', queueId),
        processAllPending: () =>
            ipcRenderer.invoke('llm:processAllPending')
    },

    // STT (Speech-to-Text) operations
    stt: {
        getAvailableEngines: () =>
            ipcRenderer.invoke('stt:getAvailableEngines'),
        initializeEngine: (engine, config) =>
            ipcRenderer.invoke('stt:initializeEngine', { engine, config })
    }
});

console.log('Preload script loaded');
