/**
 * Settings Panel Component
 * Application settings and configuration
 */

import React, { useState, useEffect } from 'react';
import { useSettingsStore } from '../../store/settingsStore';

function SettingsPanel() {
    const { settings, updateSetting } = useSettingsStore();
    const [apiKey, setApiKey] = useState('');
    const [hasApiKey, setHasApiKey] = useState(false);
    const [isSaving, setIsSaving] = useState(false);
    const [availableEngines, setAvailableEngines] = useState([]);
    const [selectedEngine, setSelectedEngine] = useState('mock');
    const [engineConfig, setEngineConfig] = useState({});

    useEffect(() => {
        // Check if API key is already set
        const checkApiKey = async () => {
            try {
                const result = await window.electronAPI.llm.hasApiKey();
                if (result.success) {
                    setHasApiKey(result.hasKey);
                }
            } catch (error) {
                console.error('Error checking API key:', error);
            }
        };
        checkApiKey();

        // Load available STT engines
        const loadSTTEngines = async () => {
            try {
                const result = await window.electronAPI.stt.getAvailableEngines();
                if (result.success) {
                    setAvailableEngines(result.engines);
                }
            } catch (error) {
                console.error('Error loading STT engines:', error);
            }
        };
        loadSTTEngines();

        // Set current STT engine from settings
        if (settings.stt_engine) {
            setSelectedEngine(settings.stt_engine);
        }
        if (settings.stt_config) {
            try {
                setEngineConfig(JSON.parse(settings.stt_config));
            } catch (error) {
                console.error('Error parsing STT config:', error);
            }
        }
    }, []);

    const handleSettingChange = async (key, value) => {
        const result = await updateSetting(key, value);
        if (!result.success) {
            alert('Failed to update setting: ' + result.error);
        }
    };

    const handleBackup = async () => {
        try {
            const result = await window.electronAPI.db.backup();
            if (result.success && !result.canceled) {
                alert('Database backed up successfully!');
            }
        } catch (error) {
            alert('Failed to backup database: ' + error.message);
        }
    };

    const handleVacuum = async () => {
        if (confirm('This will optimize the database. Continue?')) {
            try {
                const result = await window.electronAPI.db.vacuum();
                if (result.success) {
                    alert('Database optimized successfully!');
                }
            } catch (error) {
                alert('Failed to optimize database: ' + error.message);
            }
        }
    };

    const handleInitializeSTTEngine = async () => {
        setIsSaving(true);
        try {
            const result = await window.electronAPI.stt.initializeEngine(selectedEngine, engineConfig);
            if (result.success) {
                alert('STT engine initialized successfully!');
                await updateSetting('stt_engine', selectedEngine);
                await updateSetting('stt_config', JSON.stringify(engineConfig));
            } else {
                alert('Failed to initialize STT engine: ' + result.error);
            }
        } catch (error) {
            alert('Failed to initialize STT engine: ' + error.message);
        } finally {
            setIsSaving(false);
        }
    };

    return (
        <div className="settings-panel">
            <div className="settings-header">
                <h2>Settings</h2>
            </div>

            <div className="settings-content">
                {/* Appearance Settings */}
                <div className="settings-section">
                    <h3>Appearance</h3>

                    <div className="setting-item">
                        <label>Theme</label>
                        <select
                            value={settings.theme}
                            onChange={(e) => handleSettingChange('theme', e.target.value)}
                        >
                            <option value="light">Light</option>
                            <option value="dark">Dark</option>
                        </select>
                    </div>

                    <div className="setting-item">
                        <label>Default Zoom Level</label>
                        <select
                            value={settings.default_zoom_level}
                            onChange={(e) => handleSettingChange('default_zoom_level', e.target.value)}
                        >
                            <option value="year">Year</option>
                            <option value="month">Month</option>
                            <option value="week">Week</option>
                            <option value="day">Day</option>
                        </select>
                    </div>
                </div>

                {/* Audio Settings */}
                <div className="settings-section">
                    <h3>Audio</h3>

                    <div className="setting-item">
                        <label>Audio Quality</label>
                        <select
                            value={settings.audio_quality}
                            onChange={(e) => handleSettingChange('audio_quality', e.target.value)}
                        >
                            <option value="high">High (16kHz, 16-bit)</option>
                            <option value="medium">Medium (8kHz, 16-bit)</option>
                            <option value="low">Low (8kHz, 8-bit)</option>
                        </select>
                        <p className="setting-hint">
                            High quality recommended for best speech-to-text results
                        </p>
                    </div>
                </div>

                {/* STT Settings */}
                <div className="settings-section">
                    <h3>Speech-to-Text Engine</h3>

                    <div className="setting-item">
                        <label>STT Engine</label>
                        <select
                            value={selectedEngine}
                            onChange={(e) => {
                                setSelectedEngine(e.target.value);
                                setEngineConfig({}); // Reset config when changing engine
                            }}
                        >
                            {availableEngines.map(engine => (
                                <option key={engine.id} value={engine.id}>
                                    {engine.name} ({engine.cost}, {engine.type})
                                </option>
                            ))}
                        </select>
                        <p className="setting-hint">
                            {availableEngines.find(e => e.id === selectedEngine)?.description || ''}
                        </p>
                    </div>

                    {/* Engine-specific configuration */}
                    {selectedEngine === 'whisper-api' && (
                        <div className="setting-item">
                            <label>OpenAI API Key</label>
                            <input
                                type="password"
                                placeholder="sk-..."
                                value={engineConfig.apiKey || ''}
                                onChange={(e) => setEngineConfig({...engineConfig, apiKey: e.target.value})}
                            />
                        </div>
                    )}

                    {selectedEngine === 'whisper-local' && (
                        <div className="setting-item">
                            <label>Model Size</label>
                            <select
                                value={engineConfig.model || 'base'}
                                onChange={(e) => setEngineConfig({...engineConfig, model: e.target.value})}
                            >
                                <option value="tiny">Tiny (39MB)</option>
                                <option value="base">Base (74MB)</option>
                                <option value="small">Small (244MB)</option>
                                <option value="medium">Medium (769MB)</option>
                                <option value="large">Large (1.5GB)</option>
                            </select>
                        </div>
                    )}

                    {selectedEngine === 'vosk' && (
                        <div className="setting-item">
                            <label>Model Path</label>
                            <input
                                type="text"
                                placeholder="/path/to/vosk/model"
                                value={engineConfig.modelPath || ''}
                                onChange={(e) => setEngineConfig({...engineConfig, modelPath: e.target.value})}
                            />
                            <p className="setting-hint">
                                Download models from: https://alphacephei.com/vosk/models
                            </p>
                        </div>
                    )}

                    {selectedEngine === 'google' && (
                        <div className="setting-item">
                            <label>Google Cloud Key File Path</label>
                            <input
                                type="text"
                                placeholder="/path/to/credentials.json"
                                value={engineConfig.keyFilePath || ''}
                                onChange={(e) => setEngineConfig({...engineConfig, keyFilePath: e.target.value})}
                            />
                        </div>
                    )}

                    {selectedEngine === 'deepgram' && (
                        <div className="setting-item">
                            <label>Deepgram API Key</label>
                            <input
                                type="password"
                                placeholder="api-key"
                                value={engineConfig.apiKey || ''}
                                onChange={(e) => setEngineConfig({...engineConfig, apiKey: e.target.value})}
                            />
                        </div>
                    )}

                    {selectedEngine === 'assemblyai' && (
                        <div className="setting-item">
                            <label>AssemblyAI API Key</label>
                            <input
                                type="password"
                                placeholder="api-key"
                                value={engineConfig.apiKey || ''}
                                onChange={(e) => setEngineConfig({...engineConfig, apiKey: e.target.value})}
                            />
                        </div>
                    )}

                    <div className="setting-item">
                        <button
                            className="button primary"
                            onClick={handleInitializeSTTEngine}
                            disabled={isSaving}
                        >
                            {isSaving ? 'Initializing...' : 'Initialize STT Engine'}
                        </button>
                        <p className="setting-hint">
                            Click to save and initialize the selected STT engine
                        </p>
                    </div>

                    {availableEngines.find(e => e.id === selectedEngine)?.setup && (
                        <div className="setting-item">
                            <p className="setting-hint">
                                <strong>Setup:</strong> {availableEngines.find(e => e.id === selectedEngine)?.setup}
                            </p>
                        </div>
                    )}
                </div>

                {/* LLM Settings */}
                <div className="settings-section">
                    <h3>AI / LLM Configuration</h3>

                    <div className="setting-item">
                        <label>Provider</label>
                        <select
                            value={settings.llm_provider}
                            onChange={(e) => handleSettingChange('llm_provider', e.target.value)}
                        >
                            <option value="anthropic">Anthropic (Claude)</option>
                        </select>
                    </div>

                    <div className="setting-item">
                        <label>Model</label>
                        <select
                            value={settings.llm_model}
                            onChange={(e) => handleSettingChange('llm_model', e.target.value)}
                        >
                            <option value="claude-sonnet-4-20250514">Claude Sonnet 4</option>
                            <option value="claude-3-5-sonnet-20241022">Claude 3.5 Sonnet</option>
                            <option value="claude-3-opus-20240229">Claude 3 Opus</option>
                        </select>
                    </div>

                    <div className="setting-item">
                        <label>API Key</label>
                        {hasApiKey && (
                            <p className="api-key-status success">
                                ✓ API key is configured
                            </p>
                        )}
                        <input
                            type="password"
                            placeholder="sk-ant-..."
                            value={apiKey}
                            onChange={(e) => setApiKey(e.target.value)}
                        />
                        <p className="setting-hint">
                            Your API key is stored securely and never sent anywhere except to Anthropic.
                            Get your API key from: https://console.anthropic.com/
                        </p>
                        <button
                            className="button secondary"
                            onClick={async () => {
                                if (!apiKey.trim()) {
                                    alert('Please enter an API key');
                                    return;
                                }

                                setIsSaving(true);
                                try {
                                    const result = await window.electronAPI.llm.setApiKey(apiKey);
                                    if (result.success) {
                                        alert('API key saved successfully!');
                                        setHasApiKey(true);
                                        setApiKey(''); // Clear input for security
                                    } else {
                                        alert('Failed to save API key: ' + result.error);
                                    }
                                } catch (error) {
                                    alert('Failed to save API key: ' + error.message);
                                } finally {
                                    setIsSaving(false);
                                }
                            }}
                            disabled={isSaving}
                        >
                            {isSaving ? 'Saving...' : (hasApiKey ? 'Update API Key' : 'Save API Key')}
                        </button>
                    </div>

                    <div className="setting-item">
                        <label>Max Tokens</label>
                        <input
                            type="number"
                            value={settings.llm_max_tokens}
                            onChange={(e) => handleSettingChange('llm_max_tokens', e.target.value)}
                        />
                    </div>

                    <div className="setting-item">
                        <label>Temperature</label>
                        <input
                            type="number"
                            step="0.1"
                            min="0"
                            max="1"
                            value={settings.llm_temperature}
                            onChange={(e) => handleSettingChange('llm_temperature', e.target.value)}
                        />
                    </div>
                </div>

                {/* RAG Settings */}
                <div className="settings-section">
                    <h3>RAG Analysis</h3>

                    <div className="setting-item">
                        <label>
                            <input
                                type="checkbox"
                                checked={settings.rag_auto_run_enabled === 'true'}
                                onChange={(e) => handleSettingChange('rag_auto_run_enabled', e.target.checked ? 'true' : 'false')}
                            />
                            Enable Scheduled Analysis
                        </label>
                    </div>

                    <div className="setting-item">
                        <label>Schedule</label>
                        <select
                            value={settings.rag_schedule}
                            onChange={(e) => handleSettingChange('rag_schedule', e.target.value)}
                            disabled={settings.rag_auto_run_enabled !== 'true'}
                        >
                            <option value="daily">Daily</option>
                            <option value="weekly">Weekly</option>
                            <option value="monthly">Monthly</option>
                        </select>
                    </div>

                    <div className="setting-item">
                        <label>Similarity Threshold</label>
                        <input
                            type="number"
                            step="0.05"
                            min="0"
                            max="1"
                            value={settings.rag_similarity_threshold}
                            onChange={(e) => handleSettingChange('rag_similarity_threshold', e.target.value)}
                        />
                        <p className="setting-hint">
                            Higher values = stricter matching (0.75 recommended)
                        </p>
                    </div>

                    <button className="button primary">
                        Run Analysis Now
                    </button>
                </div>

                {/* Privacy Settings */}
                <div className="settings-section">
                    <h3>Privacy</h3>

                    <div className="setting-item">
                        <label>
                            <input
                                type="checkbox"
                                checked={settings.send_transcripts_only === 'true'}
                                onChange={(e) => handleSettingChange('send_transcripts_only', e.target.checked ? 'true' : 'false')}
                            />
                            Send transcripts only (not audio files)
                        </label>
                    </div>

                    <div className="setting-item">
                        <label>
                            <input
                                type="checkbox"
                                checked={settings.require_confirmation === 'true'}
                                onChange={(e) => handleSettingChange('require_confirmation', e.target.checked ? 'true' : 'false')}
                            />
                            Require confirmation before LLM operations
                        </label>
                    </div>
                </div>

                {/* Database Management */}
                <div className="settings-section">
                    <h3>Database Management</h3>

                    <div className="setting-item">
                        <button className="button secondary" onClick={handleBackup}>
                            Backup Database
                        </button>
                        <p className="setting-hint">
                            Save a copy of your timeline database
                        </p>
                    </div>

                    <div className="setting-item">
                        <button className="button secondary" onClick={handleVacuum}>
                            Optimize Database
                        </button>
                        <p className="setting-hint">
                            Reclaim space and improve performance
                        </p>
                    </div>
                </div>
            </div>
        </div>
    );
}

export default SettingsPanel;
