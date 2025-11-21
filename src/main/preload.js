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
    },

    // Embedding operations
    embedding: {
        initialize: (provider, model, apiKey) =>
            ipcRenderer.invoke('embedding:initialize', { provider, model, apiKey }),
        generateForEvent: (eventId) =>
            ipcRenderer.invoke('embedding:generateForEvent', eventId),
        generateAll: () =>
            ipcRenderer.invoke('embedding:generateAll'),
        findSimilar: (eventId, threshold, limit) =>
            ipcRenderer.invoke('embedding:findSimilar', { eventId, threshold, limit }),
        clearAll: () =>
            ipcRenderer.invoke('embedding:clearAll')
    },

    // RAG (Retrieval-Augmented Generation) operations
    rag: {
        analyzeEvent: (eventId, threshold) =>
            ipcRenderer.invoke('rag:analyzeEvent', { eventId, threshold }),
        analyzeTimeline: (threshold) =>
            ipcRenderer.invoke('rag:analyzeTimeline', { threshold }),
        getCrossReferences: (eventId) =>
            ipcRenderer.invoke('rag:getCrossReferences', eventId),
        detectPatterns: () =>
            ipcRenderer.invoke('rag:detectPatterns'),
        suggestTags: (eventId, limit) =>
            ipcRenderer.invoke('rag:suggestTags', { eventId, limit })
    },

    // Search operations
    search: {
        search: (options) =>
            ipcRenderer.invoke('search:search', options),
        getSuggestions: (query) =>
            ipcRenderer.invoke('search:getSuggestions', query),
        saveSearch: (name, options) =>
            ipcRenderer.invoke('search:saveSearch', name, options),
        getSavedSearches: () =>
            ipcRenderer.invoke('search:getSavedSearches'),
        deleteSavedSearch: (key) =>
            ipcRenderer.invoke('search:deleteSavedSearch', key)
    },

    // Batch import operations
    batchImport: {
        start: (filePaths, options) =>
            ipcRenderer.invoke('batchImport:start', filePaths, options),
        process: (sessionId) =>
            ipcRenderer.invoke('batchImport:process', sessionId),
        getStatus: (sessionId) =>
            ipcRenderer.invoke('batchImport:getStatus', sessionId),
        cancel: (sessionId) =>
            ipcRenderer.invoke('batchImport:cancel', sessionId),
        scanDirectory: (directoryPath, options) =>
            ipcRenderer.invoke('batchImport:scanDirectory', directoryPath, options),
        getActiveSessions: () =>
            ipcRenderer.invoke('batchImport:getActiveSessions')
    },

    // Analytics/Visualization operations
    analytics: {
        getSummaryStatistics: () =>
            ipcRenderer.invoke('analytics:getSummaryStatistics'),
        getCategoryDistribution: () =>
            ipcRenderer.invoke('analytics:getCategoryDistribution'),
        getTimelineDensity: (groupBy, startDate, endDate) =>
            ipcRenderer.invoke('analytics:getTimelineDensity', groupBy, startDate, endDate),
        getTagCloud: (limit) =>
            ipcRenderer.invoke('analytics:getTagCloud', limit),
        getPeopleNetwork: () =>
            ipcRenderer.invoke('analytics:getPeopleNetwork'),
        getLocationHeatmap: () =>
            ipcRenderer.invoke('analytics:getLocationHeatmap'),
        getEraStatistics: () =>
            ipcRenderer.invoke('analytics:getEraStatistics'),
        getActivityHeatmap: () =>
            ipcRenderer.invoke('analytics:getActivityHeatmap'),
        getTrendAnalysis: (groupBy) =>
            ipcRenderer.invoke('analytics:getTrendAnalysis', groupBy),
        compareTimePeriods: (period1Start, period1End, period2Start, period2End) =>
            ipcRenderer.invoke('analytics:compareTimePeriods', period1Start, period1End, period2Start, period2End)
    },

    // Dialog operations
    dialog: {
        openFiles: (options) =>
            ipcRenderer.invoke('dialog:openFiles', options),
        openDirectory: () =>
            ipcRenderer.invoke('dialog:openDirectory')
    }
});

console.log('Preload script loaded');
