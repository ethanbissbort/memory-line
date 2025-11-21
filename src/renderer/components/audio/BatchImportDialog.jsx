import React, { useState, useEffect } from 'react';
import './BatchImportDialog.css';

const BatchImportDialog = ({ onClose, onImportComplete }) => {
  const [files, setFiles] = useState([]);
  const [defaultCategory, setDefaultCategory] = useState('Personal');
  const [defaultEra, setDefaultEra] = useState('');
  const [defaultTags, setDefaultTags] = useState('');
  const [autoProcess, setAutoProcess] = useState(true);
  const [extractEvents, setExtractEvents] = useState(true);
  const [sttEngine, setSttEngine] = useState('whisper-local');
  const [importing, setImporting] = useState(false);
  const [progress, setProgress] = useState(null);
  const [sessionId, setSessionId] = useState(null);
  const [eras, setEras] = useState([]);
  const [scanningDirectory, setScanningDirectory] = useState(false);

  useEffect(() => {
    loadEras();
  }, []);

  const loadEras = async () => {
    const erasData = await window.electronAPI.eras.getAll();
    setEras(erasData);
  };

  const handleSelectFiles = async () => {
    const selectedPaths = await window.electronAPI.dialog.openFiles({
      properties: ['openFile', 'multiSelections'],
      filters: [
        { name: 'Audio Files', extensions: ['mp3', 'wav', 'm4a', 'ogg', 'flac', 'webm'] }
      ]
    });

    if (selectedPaths && selectedPaths.length > 0) {
      const newFiles = selectedPaths.map((path, index) => ({
        id: `file-${Date.now()}-${index}`,
        path,
        filename: path.split(/[\\/]/).pop(),
        status: 'pending'
      }));
      setFiles([...files, ...newFiles]);
    }
  };

  const handleSelectDirectory = async () => {
    setScanningDirectory(true);
    try {
      const directoryPath = await window.electronAPI.dialog.openDirectory();

      if (directoryPath) {
        const result = await window.electronAPI.batchImport.scanDirectory(directoryPath, {
          recursive: true,
          extensions: ['.mp3', '.wav', '.m4a', '.ogg', '.flac', '.webm']
        });

        if (result.filesFound > 0) {
          const newFiles = result.files.map((file, index) => ({
            id: `file-${Date.now()}-${index}`,
            path: file.path,
            filename: file.filename,
            size: file.size,
            status: 'pending'
          }));
          setFiles([...files, ...newFiles]);
        } else {
          alert('No audio files found in the selected directory.');
        }
      }
    } catch (error) {
      console.error('Error scanning directory:', error);
      alert('Error scanning directory: ' + error.message);
    } finally {
      setScanningDirectory(false);
    }
  };

  const handleRemoveFile = (fileId) => {
    setFiles(files.filter(f => f.id !== fileId));
  };

  const handleClearAll = () => {
    if (confirm('Remove all files from the list?')) {
      setFiles([]);
    }
  };

  const handleStartImport = async () => {
    if (files.length === 0) {
      alert('Please select files to import.');
      return;
    }

    setImporting(true);

    try {
      const filePaths = files.map(f => f.path);
      const tags = defaultTags.split(',').map(t => t.trim()).filter(t => t);

      // Start batch import
      const result = await window.electronAPI.batchImport.start(filePaths, {
        defaultCategory,
        defaultEra: defaultEra || null,
        defaultTags: tags,
        autoProcess,
        extractEvents,
        sttEngine
      });

      setSessionId(result.sessionId);

      if (result.invalidFilesCount > 0) {
        alert(`${result.invalidFilesCount} invalid files were skipped.`);
      }

      // Process batch import with progress tracking
      const progressInterval = setInterval(async () => {
        const status = await window.electronAPI.batchImport.getStatus(result.sessionId);
        if (status) {
          setProgress(status);

          if (status.status === 'completed' || status.status === 'cancelled') {
            clearInterval(progressInterval);
            setImporting(false);

            if (status.status === 'completed') {
              alert(`Import completed! ${status.successfulFiles} files imported successfully.`);
              if (onImportComplete) {
                onImportComplete(status);
              }
              onClose();
            }
          }
        }
      }, 1000);

      // Start processing
      await window.electronAPI.batchImport.process(result.sessionId);

    } catch (error) {
      console.error('Import error:', error);
      alert('Error during import: ' + error.message);
      setImporting(false);
    }
  };

  const handleCancelImport = async () => {
    if (sessionId && confirm('Cancel the current import?')) {
      await window.electronAPI.batchImport.cancel(sessionId);
      setImporting(false);
      setProgress(null);
      setSessionId(null);
    }
  };

  const formatFileSize = (bytes) => {
    if (!bytes) return '';
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    return Math.round(bytes / Math.pow(1024, i) * 100) / 100 + ' ' + sizes[i];
  };

  return (
    <div className="batch-import-dialog">
      <div className="dialog-overlay" onClick={!importing ? onClose : null}></div>
      <div className="dialog-content">
        <div className="dialog-header">
          <h2>Batch Audio Import</h2>
          {!importing && (
            <button className="close-btn" onClick={onClose}>×</button>
          )}
        </div>

        <div className="dialog-body">
          {!importing ? (
            <>
              <div className="file-selection">
                <div className="selection-buttons">
                  <button onClick={handleSelectFiles} className="select-btn">
                    <span>📁</span> Select Files
                  </button>
                  <button onClick={handleSelectDirectory} className="select-btn" disabled={scanningDirectory}>
                    <span>📂</span> {scanningDirectory ? 'Scanning...' : 'Scan Folder'}
                  </button>
                  {files.length > 0 && (
                    <button onClick={handleClearAll} className="clear-btn">
                      Clear All
                    </button>
                  )}
                </div>

                <div className="files-list">
                  {files.length === 0 ? (
                    <div className="empty-state">
                      <p>No files selected</p>
                      <p className="empty-hint">Select individual files or scan an entire folder</p>
                    </div>
                  ) : (
                    <div className="file-items">
                      <div className="files-header">
                        <span className="files-count">{files.length} file{files.length !== 1 ? 's' : ''} selected</span>
                      </div>
                      {files.map((file) => (
                        <div key={file.id} className="file-item">
                          <span className="file-icon">🎵</span>
                          <div className="file-info">
                            <div className="file-name">{file.filename}</div>
                            {file.size && (
                              <div className="file-size">{formatFileSize(file.size)}</div>
                            )}
                          </div>
                          <button
                            className="remove-file-btn"
                            onClick={() => handleRemoveFile(file.id)}
                            title="Remove"
                          >
                            ×
                          </button>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>

              <div className="import-options">
                <h3>Import Options</h3>

                <div className="option-group">
                  <label>Default Category</label>
                  <select
                    value={defaultCategory}
                    onChange={(e) => setDefaultCategory(e.target.value)}
                    className="option-input"
                  >
                    <option value="Personal">Personal</option>
                    <option value="Work">Work</option>
                    <option value="Education">Education</option>
                    <option value="Health">Health</option>
                    <option value="Travel">Travel</option>
                    <option value="Social">Social</option>
                    <option value="Family">Family</option>
                    <option value="Creative">Creative</option>
                    <option value="Other">Other</option>
                  </select>
                </div>

                <div className="option-group">
                  <label>Default Era (Optional)</label>
                  <select
                    value={defaultEra}
                    onChange={(e) => setDefaultEra(e.target.value)}
                    className="option-input"
                  >
                    <option value="">None</option>
                    {eras.map((era) => (
                      <option key={era.id} value={era.id}>
                        {era.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="option-group">
                  <label>Default Tags (comma-separated)</label>
                  <input
                    type="text"
                    value={defaultTags}
                    onChange={(e) => setDefaultTags(e.target.value)}
                    placeholder="e.g., imported, batch-2024"
                    className="option-input"
                  />
                </div>

                <div className="option-group">
                  <label>STT Engine</label>
                  <select
                    value={sttEngine}
                    onChange={(e) => setSttEngine(e.target.value)}
                    className="option-input"
                  >
                    <option value="whisper-local">Whisper (Local)</option>
                    <option value="whisper-api">Whisper (API)</option>
                    <option value="google-cloud">Google Cloud</option>
                    <option value="azure">Azure</option>
                    <option value="amazon-transcribe">Amazon Transcribe</option>
                  </select>
                </div>

                <div className="option-group checkbox-group">
                  <label>
                    <input
                      type="checkbox"
                      checked={autoProcess}
                      onChange={(e) => setAutoProcess(e.target.checked)}
                    />
                    <span>Auto-process with STT</span>
                  </label>
                </div>

                <div className="option-group checkbox-group">
                  <label>
                    <input
                      type="checkbox"
                      checked={extractEvents}
                      onChange={(e) => setExtractEvents(e.target.checked)}
                      disabled={!autoProcess}
                    />
                    <span>Extract events with LLM</span>
                  </label>
                </div>
              </div>
            </>
          ) : (
            <div className="import-progress">
              <h3>Importing Files...</h3>

              {progress && (
                <>
                  <div className="progress-bar">
                    <div
                      className="progress-fill"
                      style={{ width: `${progress.progress}%` }}
                    ></div>
                  </div>

                  <div className="progress-stats">
                    <div className="stat">
                      <span className="stat-label">Progress:</span>
                      <span className="stat-value">
                        {progress.processedFiles} / {progress.totalFiles} files
                      </span>
                    </div>
                    <div className="stat">
                      <span className="stat-label">Successful:</span>
                      <span className="stat-value success">{progress.successfulFiles}</span>
                    </div>
                    <div className="stat">
                      <span className="stat-label">Failed:</span>
                      <span className="stat-value error">{progress.failedFiles}</span>
                    </div>
                  </div>

                  {progress.files && progress.files.length > 0 && (
                    <div className="progress-files">
                      {progress.files.map((file) => (
                        <div key={file.id} className={`progress-file ${file.status}`}>
                          <span className="file-status-icon">
                            {file.status === 'completed' && '✓'}
                            {file.status === 'failed' && '✗'}
                            {file.status === 'processing' && '⏳'}
                            {file.status === 'pending' && '⏸'}
                          </span>
                          <span className="file-name">{file.filename}</span>
                          {file.error && (
                            <span className="file-error">{file.error}</span>
                          )}
                        </div>
                      ))}
                    </div>
                  )}
                </>
              )}
            </div>
          )}
        </div>

        <div className="dialog-footer">
          {!importing ? (
            <>
              <button onClick={onClose} className="cancel-btn">
                Cancel
              </button>
              <button
                onClick={handleStartImport}
                className="import-btn"
                disabled={files.length === 0}
              >
                Import {files.length} File{files.length !== 1 ? 's' : ''}
              </button>
            </>
          ) : (
            <button onClick={handleCancelImport} className="cancel-import-btn">
              Cancel Import
            </button>
          )}
        </div>
      </div>
    </div>
  );
};

export default BatchImportDialog;
