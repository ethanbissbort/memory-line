/**
 * RAG Settings Component
 * Configuration panel for RAG cross-referencing and embeddings
 */

import React, { useState, useEffect } from 'react';

function RAGSettings() {
    const [embeddingProvider, setEmbeddingProvider] = useState('openai');
    const [embeddingModel, setEmbeddingModel] = useState('text-embedding-ada-002');
    const [embeddingApiKey, setEmbeddingApiKey] = useState('');
    const [similarityThreshold, setSimilarityThreshold] = useState(0.75);
    const [autoGenerate, setAutoGenerate] = useState(true);
    const [isInitialized, setIsInitialized] = useState(false);
    const [isGenerating, setIsGenerating] = useState(false);
    const [isAnalyzing, setIsAnalyzing] = useState(false);
    const [stats, setStats] = useState({ events: 0, embeddings: 0, crossRefs: 0 });

    useEffect(() => {
        loadSettings();
        loadStats();
    }, []);

    const loadSettings = async () => {
        try {
            const result = await window.electronAPI.settings.getAll();
            if (result.success) {
                const settings = result.data;
                if (settings.embedding_provider) setEmbeddingProvider(settings.embedding_provider);
                if (settings.embedding_model) setEmbeddingModel(settings.embedding_model);
                if (settings.embedding_api_key) setEmbeddingApiKey(settings.embedding_api_key);
                if (settings.rag_similarity_threshold) setSimilarityThreshold(parseFloat(settings.rag_similarity_threshold));
                if (settings.auto_generate_embeddings) setAutoGenerate(settings.auto_generate_embeddings === 'true');
            }
        } catch (error) {
            console.error('Error loading settings:', error);
        }
    };

    const loadStats = async () => {
        // TODO: Add IPC handler to get embedding and cross-reference stats
        // For now, just placeholder
        setStats({ events: 0, embeddings: 0, crossRefs: 0 });
    };

    const handleInitializeEmbedding = async () => {
        if (!embeddingApiKey && embeddingProvider !== 'local') {
            alert('Please enter an API key for ' + embeddingProvider);
            return;
        }

        try {
            const result = await window.electronAPI.embedding.initialize(
                embeddingProvider,
                embeddingModel,
                embeddingApiKey
            );

            if (result.success) {
                setIsInitialized(true);
                alert('Embedding service initialized successfully!');
            } else {
                alert('Failed to initialize embedding service: ' + result.error);
            }
        } catch (error) {
            console.error('Error initializing embedding service:', error);
            alert('Failed to initialize: ' + error.message);
        }
    };

    const handleGenerateAllEmbeddings = async () => {
        if (!confirm('Generate embeddings for all events without embeddings?\n\nThis will process each event and may incur API costs depending on your provider.')) {
            return;
        }

        setIsGenerating(true);
        try {
            const result = await window.electronAPI.embedding.generateAll();
            if (result.success) {
                alert(`Embedding generation complete!\n\nTotal: ${result.total}\nSucceeded: ${result.succeeded}\nFailed: ${result.failed}`);
                await loadStats();
            } else {
                alert('Failed to generate embeddings: ' + result.error);
            }
        } catch (error) {
            console.error('Error generating embeddings:', error);
            alert('Failed to generate embeddings: ' + error.message);
        } finally {
            setIsGenerating(false);
        }
    };

    const handleAnalyzeTimeline = async () => {
        if (!confirm(`Analyze entire timeline for cross-references?\n\nThis will:\n1. Compare all events using embeddings\n2. Use LLM to determine relationships\n3. Store discovered connections\n\nThis may take several minutes and incur API costs.`)) {
            return;
        }

        setIsAnalyzing(true);
        try {
            const result = await window.electronAPI.rag.analyzeTimeline(similarityThreshold);
            if (result.success) {
                alert(`Timeline analysis complete!\n\nTotal events: ${result.total_events}\nCross-references found: ${result.cross_references_found}`);
                await loadStats();
            } else {
                alert('Failed to analyze timeline: ' + result.error);
            }
        } catch (error) {
            console.error('Error analyzing timeline:', error);
            alert('Failed to analyze timeline: ' + error.message);
        } finally {
            setIsAnalyzing(false);
        }
    };

    const handleDetectPatterns = async () => {
        try {
            const result = await window.electronAPI.rag.detectPatterns();
            if (result.success) {
                const patterns = result.patterns;
                let message = 'Patterns Detected:\n\n';

                patterns.forEach(pattern => {
                    message += `${pattern.type}:\n${pattern.description}\n`;
                    if (pattern.patterns.length > 0) {
                        message += `Found ${pattern.patterns.length} instances\n\n`;
                    }
                });

                alert(message || 'No patterns detected');
            } else {
                alert('Failed to detect patterns: ' + result.error);
            }
        } catch (error) {
            console.error('Error detecting patterns:', error);
            alert('Failed to detect patterns: ' + error.message);
        }
    };

    const handleClearAllEmbeddings = async () => {
        if (!confirm('Clear all embeddings?\n\nThis will delete all generated embeddings. You will need to regenerate them. This action cannot be undone.')) {
            return;
        }

        try {
            const result = await window.electronAPI.embedding.clearAll();
            if (result.success) {
                alert('All embeddings cleared successfully!');
                await loadStats();
            } else {
                alert('Failed to clear embeddings: ' + result.error);
            }
        } catch (error) {
            console.error('Error clearing embeddings:', error);
            alert('Failed to clear embeddings: ' + error.message);
        }
    };

    const handleSaveSettings = async () => {
        try {
            await window.electronAPI.settings.update('embedding_provider', embeddingProvider);
            await window.electronAPI.settings.update('embedding_model', embeddingModel);
            await window.electronAPI.settings.update('embedding_api_key', embeddingApiKey);
            await window.electronAPI.settings.update('rag_similarity_threshold', similarityThreshold.toString());
            await window.electronAPI.settings.update('auto_generate_embeddings', autoGenerate.toString());

            alert('Settings saved successfully!');
        } catch (error) {
            console.error('Error saving settings:', error);
            alert('Failed to save settings: ' + error.message);
        }
    };

    const providerModels = {
        openai: [
            { value: 'text-embedding-ada-002', label: 'Ada-002 (1536 dim, $0.0001/1K tokens)' },
            { value: 'text-embedding-3-small', label: 'Embedding-3-Small (1536 dim)' },
            { value: 'text-embedding-3-large', label: 'Embedding-3-Large (3072 dim)' }
        ],
        voyage: [
            { value: 'voyage-2', label: 'Voyage-2 (1024 dim)' },
            { value: 'voyage-large-2', label: 'Voyage-Large-2 (1536 dim)' }
        ],
        cohere: [
            { value: 'embed-english-v3.0', label: 'Embed English v3.0 (1024 dim)' }
        ],
        local: [
            { value: 'all-MiniLM-L6-v2', label: 'All-MiniLM-L6-v2 (384 dim, local)' }
        ]
    };

    return (
        <div className="rag-settings">
            <div className="settings-section">
                <h3>Embedding Provider</h3>
                <p className="section-description">
                    Configure the embedding service for semantic similarity search
                </p>

                <div className="form-group">
                    <label>Provider</label>
                    <select
                        value={embeddingProvider}
                        onChange={(e) => setEmbeddingProvider(e.target.value)}
                    >
                        <option value="openai">OpenAI</option>
                        <option value="voyage">Voyage AI</option>
                        <option value="cohere">Cohere</option>
                        <option value="local">Local (Mock)</option>
                    </select>
                </div>

                <div className="form-group">
                    <label>Model</label>
                    <select
                        value={embeddingModel}
                        onChange={(e) => setEmbeddingModel(e.target.value)}
                    >
                        {providerModels[embeddingProvider]?.map(model => (
                            <option key={model.value} value={model.value}>
                                {model.label}
                            </option>
                        ))}
                    </select>
                </div>

                {embeddingProvider !== 'local' && (
                    <div className="form-group">
                        <label>API Key</label>
                        <input
                            type="password"
                            value={embeddingApiKey}
                            onChange={(e) => setEmbeddingApiKey(e.target.value)}
                            placeholder={`Enter ${embeddingProvider} API key`}
                        />
                    </div>
                )}

                <button
                    className="button primary"
                    onClick={handleInitializeEmbedding}
                >
                    Initialize Embedding Service
                </button>
            </div>

            <div className="settings-section">
                <h3>RAG Configuration</h3>

                <div className="form-group">
                    <label>Similarity Threshold: {similarityThreshold.toFixed(2)}</label>
                    <input
                        type="range"
                        min="0.5"
                        max="0.95"
                        step="0.05"
                        value={similarityThreshold}
                        onChange={(e) => setSimilarityThreshold(parseFloat(e.target.value))}
                    />
                    <p className="field-hint">
                        Minimum similarity score (0.5-0.95) for considering events as related
                    </p>
                </div>

                <div className="form-group">
                    <label>
                        <input
                            type="checkbox"
                            checked={autoGenerate}
                            onChange={(e) => setAutoGenerate(e.target.checked)}
                        />
                        Auto-generate embeddings for new events
                    </label>
                </div>

                <button
                    className="button secondary"
                    onClick={handleSaveSettings}
                >
                    Save Settings
                </button>
            </div>

            <div className="settings-section">
                <h3>Embedding Management</h3>

                <div className="rag-stats">
                    <div className="stat">
                        <span className="stat-label">Total Events:</span>
                        <span className="stat-value">{stats.events}</span>
                    </div>
                    <div className="stat">
                        <span className="stat-label">With Embeddings:</span>
                        <span className="stat-value">{stats.embeddings}</span>
                    </div>
                    <div className="stat">
                        <span className="stat-label">Cross-References:</span>
                        <span className="stat-value">{stats.crossRefs}</span>
                    </div>
                </div>

                <div className="button-group">
                    <button
                        className="button primary"
                        onClick={handleGenerateAllEmbeddings}
                        disabled={isGenerating}
                    >
                        {isGenerating ? 'Generating...' : 'Generate All Embeddings'}
                    </button>
                    <button
                        className="button danger"
                        onClick={handleClearAllEmbeddings}
                    >
                        Clear All Embeddings
                    </button>
                </div>
            </div>

            <div className="settings-section">
                <h3>Timeline Analysis</h3>
                <p className="section-description">
                    Run analysis to discover connections and patterns across your entire timeline
                </p>

                <div className="button-group">
                    <button
                        className="button primary"
                        onClick={handleAnalyzeTimeline}
                        disabled={isAnalyzing}
                    >
                        {isAnalyzing ? 'Analyzing...' : 'Analyze Timeline for Cross-References'}
                    </button>
                    <button
                        className="button secondary"
                        onClick={handleDetectPatterns}
                    >
                        Detect Patterns
                    </button>
                </div>
            </div>
        </div>
    );
}

export default RAGSettings;
