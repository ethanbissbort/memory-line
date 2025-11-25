# Claude.md - Memory Timeline Application

## Project Overview

Memory Timeline is an Electron-based desktop application that serves as a personal memory and event timeline with audio recording, LLM-powered event extraction, and RAG-based cross-referencing capabilities. The application combines React for the UI, SQLite for data storage, and integrates with Anthropic's Claude API for intelligent event extraction and analysis.

## Architecture

### Technology Stack
- **Frontend**: React 18 with Hooks, Zustand for state management
- **Desktop Framework**: Electron with secure IPC communication
- **Database**: SQLite3 (better-sqlite3) with full-text search (FTS5)
- **Build System**: Webpack + Babel
- **AI/LLM**: Anthropic Claude API, multiple embedding providers (OpenAI, Voyage AI, Cohere)
- **Date Handling**: date-fns library

### Key Architectural Decisions

1. **Electron IPC Pattern**: Main process handles all database operations and file I/O; renderer process handles UI only
2. **Local-First**: All data stored locally in SQLite, API calls only for transcription and LLM analysis
3. **Secure Communication**: Preload script exposes limited API surface via contextBridge
4. **State Management**: Zustand stores for UI state, database as single source of truth
5. **Audio Storage**: Files stored externally in user data directory, not as blobs in database

## Project Structure

```
memory-line/
├── src/
│   ├── main/                      # Electron main process
│   │   ├── main.js               # Entry point, IPC handlers, window management
│   │   └── preload.js            # Secure bridge between main and renderer
│   ├── renderer/                  # React application
│   │   ├── components/
│   │   │   ├── audio/            # AudioRecorder, RecordingQueue
│   │   │   ├── common/           # Header, Sidebar (shared UI)
│   │   │   ├── events/           # EventDetails, EventForm
│   │   │   ├── settings/         # SettingsPanel
│   │   │   └── timeline/         # Timeline, EventBubble
│   │   ├── store/                # Zustand state stores
│   │   ├── styles/               # Component-specific CSS
│   │   ├── utils/                # Helper functions, constants
│   │   ├── App.jsx               # Main app component, routing
│   │   └── index.jsx             # React DOM entry
│   └── database/
│       ├── database.js           # Database service layer
│       └── schemas/
│           └── schema.sql        # Complete schema with migrations
├── public/
│   └── index.html                # HTML shell
├── package.json                  # Dependencies and scripts
├── webpack.renderer.config.js    # Webpack configuration
└── .babelrc                      # Babel presets
```

## Database Schema

### Core Tables
- **events**: Timeline events (id, title, start_date, end_date, description, category, era_id, transcript, embedding_id)
- **eras**: Life phases (id, name, start_date, end_date, color, description)
- **tags**: Event categorization (id, name, color)
- **people**: People mentioned (id, name, relationship, notes)
- **locations**: Places (id, name, latitude, longitude, notes)

### Junction Tables
- **event_tags**: Many-to-many events ↔ tags
- **event_people**: Many-to-many events ↔ people
- **event_locations**: Many-to-many events ↔ locations

### Processing Tables
- **recording_queue**: Audio files awaiting transcription (id, file_path, created_at, status, transcript, error_message)
- **pending_events**: Extracted events awaiting review (id, recording_id, extracted_data JSON, status, reviewed_at)

### RAG Tables
- **embeddings**: Vector embeddings (id, vector BLOB, model, dimension, created_at)
- **cross_references**: Event relationships (from_event_id, to_event_id, relationship_type, confidence, explanation, discovered_at)

### System Tables
- **app_settings**: Key-value configuration store
- **events_fts**: Full-text search virtual table

## Development Guidelines

### Code Conventions

1. **File Naming**:
   - Components: PascalCase (e.g., `AudioRecorder.jsx`)
   - Utilities: camelCase (e.g., `dateUtils.js`)
   - Styles: Match component name (e.g., `AudioRecorder.css`)

2. **Component Structure**:
   ```javascript
   // Imports
   import React, { useState, useEffect } from 'react';
   import './ComponentName.css';

   // Component definition
   function ComponentName({ prop1, prop2 }) {
     // State hooks
     const [state, setState] = useState(null);

     // Effect hooks
     useEffect(() => {
       // Side effects
     }, [dependencies]);

     // Event handlers
     const handleEvent = () => {
       // Handler logic
     };

     // Render
     return (
       <div className="component-name">
         {/* JSX */}
       </div>
     );
   }

   export default ComponentName;
   ```

3. **Database Operations**:
   - All database calls go through `database.js` service layer
   - Use prepared statements for all queries
   - Wrap multi-step operations in transactions
   - Always handle errors and return meaningful messages

4. **IPC Communication**:
   ```javascript
   // Main process (main.js)
   ipcMain.handle('channel-name', async (event, arg1, arg2) => {
     try {
       // Operation
       return { success: true, data };
     } catch (error) {
       return { success: false, error: error.message };
     }
   });

   // Renderer process (via preload.js)
   const result = await window.electronAPI.channelName(arg1, arg2);
   if (result.success) {
     // Handle success
   } else {
     // Handle error
   }
   ```

5. **State Management**:
   - Use Zustand for global UI state (current view, selected event, etc.)
   - Database is source of truth; re-fetch after mutations
   - Don't duplicate database data in Zustand stores

### Error Handling

1. **API Calls**: Use exponential backoff retry logic
   - LLM extraction: 3 retries (1s, 2s, 4s delays)
   - STT transcription: 2 retries (2s, 4s delays)
   - Skip retry on 401 (auth) or 400 (bad request)

2. **Database Errors**: Catch and log, show user-friendly message

3. **File Operations**: Check existence, handle permissions errors

### Testing Approach

- Manual testing in dev mode: `npm run dev`
- Test database operations through Settings panel
- Audio recording: Use mock STT for quick testing
- LLM extraction: Use small test audio files

## Common Patterns

### Adding a New Component

1. Create component file in appropriate folder under `src/renderer/components/`
2. Create matching CSS file in `src/renderer/styles/`
3. Import and use in parent component
4. If needs data, add IPC handler in `main.js` and database method in `database.js`

### Adding a Database Table

1. Update `schema.sql` with CREATE TABLE statement
2. Add indexes for frequently queried columns
3. Create database methods in `database.js`
4. Add IPC handlers in `main.js`
5. Update preload.js to expose IPC channels
6. Use in renderer components

### Adding an App Setting

1. Insert default in `schema.sql` (app_settings table)
2. Add UI control in `SettingsPanel.jsx`
3. Create getter/setter in `database.js`
4. Expose via IPC in `main.js` and `preload.js`

### Working with Dates

- **Storage**: Store as ISO 8601 strings in SQLite (`YYYY-MM-DD` or `YYYY-MM-DD HH:MM:SS`)
- **Display**: Use `date-fns` for formatting (`format(date, 'MMM d, yyyy')`)
- **Parsing**: Use `new Date(isoString)` or `parseISO` from date-fns
- **Null dates**: Use `NULL` in database, handle gracefully in UI

### Audio File Management

- **Recording**: MediaRecorder API → Blob → saved to disk
- **Storage Path**: `${app.getPath('userData')}/recordings/`
- **Format**: WebM with Opus codec (or WAV for compatibility)
- **Metadata**: Duration, file size, timestamps stored in recording_queue table
- **Lifecycle**: Never delete original recordings

## Integration Points

### Anthropic Claude API

- **Authentication**: API key stored in app_settings table
- **Model**: claude-sonnet-4-20250514 (configurable)
- **Use Cases**: Event extraction from transcripts, cross-reference analysis
- **Prompt Engineering**: Structured prompts in `main.js` (extractEventsFromTranscript)
- **Response Format**: JSON with strict schema validation

### Embedding Providers

Supported providers (configurable in Settings):
1. **OpenAI**: text-embedding-ada-002 (1536 dimensions)
2. **Voyage AI**: voyage-2 (1024 dimensions)
3. **Cohere**: embed-english-v3.0 (1024 dimensions)
4. **Local**: Placeholder for local embedding models

### Speech-to-Text Engines

9 engines supported (see README.md for details):
- **Mock**: Demo/testing
- **Whisper.cpp**: Local, free, excellent accuracy
- **OpenAI Whisper API**: Cloud, $0.006/min
- **Vosk**: Local, free, good accuracy
- **Google Cloud Speech-to-Text**: Cloud, excellent
- **Deepgram**: Cloud, very good
- **AssemblyAI**: Cloud, very good

## Development Workflow

### Starting Development

```bash
npm run dev
```

This starts:
1. Webpack dev server on http://localhost:8080
2. Electron app with hot reload enabled

### Making Changes

1. **UI Changes**: Edit files in `src/renderer/`, hot reload applies automatically
2. **Main Process Changes**: Edit `src/main/main.js`, requires app restart
3. **Database Changes**: Edit `database.js`, restart app, may need to delete DB file for schema changes
4. **Styles**: Edit CSS files, hot reload applies

### Building for Production

```bash
npm run build        # Build renderer
npm run package      # Package Electron app
```

## Important Notes for AI Assistants (Claude Code)

### Current Development Context

**Electron Version (Main Branch)**:
- Status: Release Candidate (v1.0.0-rc1)
- All 7 phases complete and production-ready
- Focus: Bug fixes, minor enhancements, user feedback

**Native Windows Version (windows-native Branch)**:
- Status: 86% complete (Phases 0-6 done)
- Next: Phase 7 (Testing & Deployment)
- See MIGRATION-TO-NATIVE-WIN.md for implementation status

### When Editing Code

1. **Always check file structure first**: Use Read tool on key files before making changes
2. **Database operations**: All DB code goes in `database.js`, exposed via IPC
3. **No direct require() in renderer**: Use preload.js bridge
4. **CSS organization**: One file per component, imported in JSX
5. **Event handlers**: Prefix with `handle` (e.g., `handleRecordStart`)
6. **Branch awareness**: Electron code in main branch, Windows native in windows-native branch

### When Adding Features

1. **Determine target platform**: Electron (main) or Windows Native (windows-native)?
2. **Start with database schema**: Does this need new tables/columns?
3. **Design the data flow**:
   - Electron: Main process (DB) → IPC → Renderer (UI)
   - Windows Native: Repository → Service → ViewModel → View (XAML)
4. **Consider error states**: What if API fails? What if file doesn't exist?
5. **Update Settings if needed**: New features often need configuration
6. **Test the full flow**: Record → Process → Review → Approve → Timeline
7. **Documentation**: Update README.md for Electron, MIGRATION-TO-NATIVE-WIN.md for Windows

### When Debugging

1. **Check DevTools**: Renderer console for UI errors
2. **Check Terminal**: Main process console for DB/IPC errors
3. **Inspect Database**: Use sqlite3 CLI or DB Browser for SQLite
4. **Test IPC**: Add console.log in both main and renderer processes

### Performance Considerations

1. **Timeline rendering**: Only load events in visible date range
2. **Large audio files**: Stream instead of loading entire file
3. **Embeddings**: Generate in batches, not all at once
4. **Full-text search**: Use FTS5 indexes, don't scan full table

### Security Considerations

1. **API keys**: Never log or expose API keys
2. **User data**: All stored locally, never sent to cloud except transcripts (if configured)
3. **IPC validation**: Validate all input from renderer process
4. **SQL injection**: Use prepared statements always

## Quick Reference

### Running the App

- Development: `npm run dev`
- Build: `npm run build && npm run package`
- Start (after build): `npm start`

### Key Files

- Entry point: `src/main/main.js`
- Database: `src/database/database.js`
- Main UI: `src/renderer/App.jsx`
- IPC bridge: `src/main/preload.js`

### Common Tasks

- Add setting: Edit schema.sql, database.js, SettingsPanel.jsx
- Add IPC channel: Update main.js, preload.js, component
- Fix DB issue: Delete `memory-timeline.db`, restart (data loss!)
- Clear audio queue: Delete files in `userData/recordings/`

## Project Status

**Current Phase**: All Phases Complete (Release Candidate)

**Electron Version** (main application):
- ✅ Phases 1-7 Complete
- ✅ Core infrastructure and database
- ✅ Audio recording and queue system
- ✅ LLM integration for event extraction
- ✅ Advanced timeline visualization
- ✅ RAG cross-referencing with embeddings
- ✅ Polish, optimization, export functionality
- ✅ Extended features (advanced search, batch import, analytics)

**Native Windows Version** (separate branch):
- ✅ Phases 0-6 Complete (86% overall)
- ⬜ Phase 7 Pending (Testing & Deployment)
- See MIGRATION-TO-NATIVE-WIN.md for details

**Next**:
- 📋 Continue native Windows implementation (Phase 7)
- 📋 Mobile companion apps (iOS/Android)
- 📋 Cloud sync and collaborative features

## Resources

- Main README: `README.md`
- Database Schema: `src/database/schemas/schema.sql`
- Dependencies: `package.json`
- Anthropic API Docs: https://docs.anthropic.com/
- Electron IPC Docs: https://www.electronjs.org/docs/latest/api/ipc-main

---

**Last Updated**: 2025-11-25
**Version**: 1.0.0-rc1 (Electron), 0.9.0 (Windows Native)
