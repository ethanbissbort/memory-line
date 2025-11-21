/**
 * Batch Audio Import Service
 * Handles importing multiple audio files at once with progress tracking
 */

const fs = require('fs');
const path = require('path');
const { v4: uuidv4 } = require('uuid');

class BatchImportService {
  constructor(db) {
    this.db = db;
    this.activeImports = new Map(); // Track active import sessions
  }

  /**
   * Start a batch import session
   * @param {Array<string>} filePaths - Array of audio file paths to import
   * @param {Object} options - Import options
   * @param {string} options.defaultCategory - Default category for all files
   * @param {string} options.defaultEra - Default era ID for all files
   * @param {Array<string>} options.defaultTags - Default tags for all files
   * @param {boolean} options.autoProcess - Automatically process with STT (default: true)
   * @param {boolean} options.extractEvents - Automatically extract events with LLM (default: true)
   * @param {string} options.sttEngine - STT engine to use
   * @returns {Object} Import session details
   */
  startBatchImport(filePaths, options = {}) {
    const sessionId = uuidv4();
    const {
      defaultCategory = 'Personal',
      defaultEra = null,
      defaultTags = [],
      autoProcess = true,
      extractEvents = true,
      sttEngine = 'whisper-local'
    } = options;

    // Validate files
    const validFiles = [];
    const invalidFiles = [];

    for (const filePath of filePaths) {
      try {
        if (!fs.existsSync(filePath)) {
          invalidFiles.push({ path: filePath, error: 'File not found' });
          continue;
        }

        const stats = fs.statSync(filePath);
        if (!stats.isFile()) {
          invalidFiles.push({ path: filePath, error: 'Not a file' });
          continue;
        }

        const ext = path.extname(filePath).toLowerCase();
        const supportedFormats = ['.mp3', '.wav', '.m4a', '.ogg', '.flac', '.webm'];
        if (!supportedFormats.includes(ext)) {
          invalidFiles.push({ path: filePath, error: 'Unsupported format' });
          continue;
        }

        validFiles.push({
          id: uuidv4(),
          path: filePath,
          filename: path.basename(filePath),
          size: stats.size,
          status: 'pending',
          error: null,
          queueId: null
        });
      } catch (error) {
        invalidFiles.push({ path: filePath, error: error.message });
      }
    }

    const session = {
      id: sessionId,
      createdAt: new Date().toISOString(),
      totalFiles: validFiles.length,
      processedFiles: 0,
      successfulFiles: 0,
      failedFiles: 0,
      status: 'pending', // pending, processing, completed, cancelled
      files: validFiles,
      invalidFiles,
      options: {
        defaultCategory,
        defaultEra,
        defaultTags,
        autoProcess,
        extractEvents,
        sttEngine
      }
    };

    this.activeImports.set(sessionId, session);

    return {
      sessionId,
      totalFiles: validFiles.length,
      invalidFilesCount: invalidFiles.length,
      invalidFiles,
      session
    };
  }

  /**
   * Process a batch import session
   * @param {string} sessionId - The import session ID
   * @param {Function} progressCallback - Callback for progress updates
   * @returns {Promise<Object>} Final import results
   */
  async processBatchImport(sessionId, progressCallback = null) {
    const session = this.activeImports.get(sessionId);
    if (!session) {
      throw new Error('Import session not found');
    }

    session.status = 'processing';
    session.startedAt = new Date().toISOString();

    for (const file of session.files) {
      if (session.status === 'cancelled') {
        break;
      }

      try {
        file.status = 'processing';

        // Copy file to app's audio directory
        const appDataPath = this._getAppDataPath();
        const audioDir = path.join(appDataPath, 'audio');
        if (!fs.existsSync(audioDir)) {
          fs.mkdirSync(audioDir, { recursive: true });
        }

        const destPath = path.join(audioDir, `${file.id}${path.extname(file.filename)}`);
        fs.copyFileSync(file.path, destPath);

        // Add to recording queue
        const queueId = this._addToQueue(
          destPath,
          session.options.defaultCategory,
          session.options.defaultEra,
          session.options.sttEngine,
          session.options.autoProcess,
          session.options.extractEvents
        );

        file.queueId = queueId;
        file.copiedPath = destPath;

        // Apply default tags if provided
        if (session.options.defaultTags.length > 0) {
          file.defaultTags = session.options.defaultTags;
        }

        file.status = 'completed';
        session.successfulFiles++;
      } catch (error) {
        file.status = 'failed';
        file.error = error.message;
        session.failedFiles++;
      }

      session.processedFiles++;

      // Progress callback
      if (progressCallback) {
        progressCallback({
          sessionId,
          processedFiles: session.processedFiles,
          totalFiles: session.totalFiles,
          successfulFiles: session.successfulFiles,
          failedFiles: session.failedFiles,
          currentFile: file.filename,
          progress: (session.processedFiles / session.totalFiles) * 100
        });
      }
    }

    session.status = session.status === 'cancelled' ? 'cancelled' : 'completed';
    session.completedAt = new Date().toISOString();

    return {
      sessionId,
      status: session.status,
      totalFiles: session.totalFiles,
      processedFiles: session.processedFiles,
      successfulFiles: session.successfulFiles,
      failedFiles: session.failedFiles,
      files: session.files,
      duration: new Date(session.completedAt) - new Date(session.startedAt)
    };
  }

  /**
   * Add file to recording queue
   */
  _addToQueue(filePath, category, eraId, sttEngine, autoProcess, extractEvents) {
    const id = uuidv4();
    const filename = path.basename(filePath);

    this.db.prepare(`
      INSERT INTO recording_queue (id, filename, file_path, category, era_id, status, stt_engine, auto_process)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?)
    `).run(
      id,
      filename,
      filePath,
      category,
      eraId,
      autoProcess ? 'pending' : 'paused',
      sttEngine,
      autoProcess ? 1 : 0
    );

    return id;
  }

  /**
   * Cancel an active import session
   * @param {string} sessionId - The import session ID
   */
  cancelBatchImport(sessionId) {
    const session = this.activeImports.get(sessionId);
    if (!session) {
      throw new Error('Import session not found');
    }

    session.status = 'cancelled';
    return { success: true };
  }

  /**
   * Get import session status
   * @param {string} sessionId - The import session ID
   * @returns {Object} Session status
   */
  getImportStatus(sessionId) {
    const session = this.activeImports.get(sessionId);
    if (!session) {
      return null;
    }

    return {
      sessionId: session.id,
      status: session.status,
      totalFiles: session.totalFiles,
      processedFiles: session.processedFiles,
      successfulFiles: session.successfulFiles,
      failedFiles: session.failedFiles,
      progress: session.totalFiles > 0 ? (session.processedFiles / session.totalFiles) * 100 : 0,
      files: session.files.map(f => ({
        id: f.id,
        filename: f.filename,
        status: f.status,
        error: f.error,
        queueId: f.queueId
      }))
    };
  }

  /**
   * Get all active import sessions
   * @returns {Array} Active sessions
   */
  getActiveSessions() {
    const sessions = [];
    for (const [sessionId, session] of this.activeImports) {
      sessions.push({
        sessionId: session.id,
        status: session.status,
        totalFiles: session.totalFiles,
        processedFiles: session.processedFiles,
        createdAt: session.createdAt
      });
    }
    return sessions;
  }

  /**
   * Clear completed import sessions
   */
  clearCompletedSessions() {
    for (const [sessionId, session] of this.activeImports) {
      if (session.status === 'completed' || session.status === 'cancelled') {
        this.activeImports.delete(sessionId);
      }
    }
    return { success: true };
  }

  /**
   * Import files from a directory (recursive)
   * @param {string} directoryPath - Directory to scan
   * @param {Object} options - Import options
   * @param {boolean} options.recursive - Scan subdirectories (default: false)
   * @param {Array<string>} options.extensions - File extensions to include
   * @returns {Object} Files found
   */
  scanDirectory(directoryPath, options = {}) {
    const { recursive = false, extensions = ['.mp3', '.wav', '.m4a', '.ogg', '.flac', '.webm'] } = options;
    const files = [];

    const scanDir = (dir) => {
      try {
        const items = fs.readdirSync(dir);

        for (const item of items) {
          const fullPath = path.join(dir, item);
          const stats = fs.statSync(fullPath);

          if (stats.isDirectory() && recursive) {
            scanDir(fullPath);
          } else if (stats.isFile()) {
            const ext = path.extname(fullPath).toLowerCase();
            if (extensions.includes(ext)) {
              files.push({
                path: fullPath,
                filename: item,
                size: stats.size,
                modified: stats.mtime
              });
            }
          }
        }
      } catch (error) {
        console.error(`Error scanning directory ${dir}:`, error);
      }
    };

    scanDir(directoryPath);

    return {
      directory: directoryPath,
      filesFound: files.length,
      files: files.sort((a, b) => b.modified - a.modified) // Most recent first
    };
  }

  /**
   * Batch tag imported events
   * @param {string} sessionId - The import session ID
   * @param {Array<string>} tags - Tags to apply
   * @returns {Object} Results
   */
  async batchTagImportedEvents(sessionId, tags) {
    const session = this.activeImports.get(sessionId);
    if (!session) {
      throw new Error('Import session not found');
    }

    let tagged = 0;
    const tagIds = [];

    // Get or create tag IDs
    for (const tagName of tags) {
      let tagId = this.db.prepare('SELECT id FROM tags WHERE name = ?').get(tagName)?.id;

      if (!tagId) {
        const result = this.db.prepare('INSERT INTO tags (id, name) VALUES (?, ?)').run(uuidv4(), tagName);
        tagId = this.db.prepare('SELECT id FROM tags WHERE name = ?').get(tagName).id;
      }

      tagIds.push(tagId);
    }

    // For each file, find the pending event and tag it
    for (const file of session.files) {
      if (file.status === 'completed' && file.queueId) {
        // Find events created from this queue item
        const events = this.db.prepare(`
          SELECT e.id FROM events e
          JOIN pending_events pe ON e.id = pe.event_id
          JOIN recording_queue rq ON pe.queue_id = rq.id
          WHERE rq.id = ?
        `).all(file.queueId);

        for (const event of events) {
          for (const tagId of tagIds) {
            try {
              this.db.prepare(`
                INSERT OR IGNORE INTO event_tags (event_id, tag_id)
                VALUES (?, ?)
              `).run(event.id, tagId);
              tagged++;
            } catch (error) {
              console.error('Error tagging event:', error);
            }
          }
        }
      }
    }

    return { success: true, eventsTagged: tagged, tagsApplied: tagIds.length };
  }

  /**
   * Get app data path (platform-specific)
   */
  _getAppDataPath() {
    const { app } = require('electron');
    return app.getPath('userData');
  }

  /**
   * Get import history
   * @param {number} limit - Maximum number of records to return
   * @returns {Array} Import history
   */
  getImportHistory(limit = 50) {
    const history = [];

    for (const [sessionId, session] of this.activeImports) {
      if (session.status === 'completed') {
        history.push({
          sessionId: session.id,
          createdAt: session.createdAt,
          completedAt: session.completedAt,
          totalFiles: session.totalFiles,
          successfulFiles: session.successfulFiles,
          failedFiles: session.failedFiles
        });
      }
    }

    return history
      .sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt))
      .slice(0, limit);
  }
}

module.exports = BatchImportService;
