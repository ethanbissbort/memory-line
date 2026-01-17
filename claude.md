# Claude.md - Memory Timeline Application

## Project Overview

Memory Timeline is a personal memory and event timeline application with audio recording, LLM-powered event extraction, and RAG-based cross-referencing capabilities. The project has **two implementations**:

1. **Electron Version** (main branch) - Cross-platform desktop app using JavaScript/React
2. **Windows Native Version** (windows-native/) - Native Windows 11 app using .NET 8/WinUI 3

---

## Quick Reference

### Project Status

| Version | Status | Completion |
|---------|--------|------------|
| **Electron** | Release Candidate (v1.0.0-rc1) | 100% |
| **Windows Native** | Beta | 86% (Phase 7 pending) |

### Key Directories

| Path | Description |
|------|-------------|
| `src/main/` | Electron main process |
| `src/renderer/` | React UI components |
| `src/database/` | Database service layer |
| `windows-native/src/` | Windows native .NET solution |

### Running the App

```bash
# Electron (development)
npm run dev

# Electron (production)
npm run build && npm run package

# Windows Native (Visual Studio)
# Open windows-native/src/MemoryTimeline.sln, press F5
```

---

## Architecture

### Electron Version

```
┌─────────────────────────────────────────┐
│         Electron Application             │
├─────────────────────────────────────────┤
│  Renderer Process (React + Zustand)     │
│  ┌──────────────────────────────────┐   │
│  │ Components (JSX)                 │   │
│  │ State Management (Zustand)       │   │
│  │ Styles (CSS)                     │   │
│  └──────────────────────────────────┘   │
│              ↕ IPC                       │
│  ┌──────────────────────────────────┐   │
│  │ Main Process (Node.js)           │   │
│  │ - Database (better-sqlite3)      │   │
│  │ - File System                    │   │
│  │ - Services (LLM, STT, RAG)       │   │
│  └──────────────────────────────────┘   │
├─────────────────────────────────────────┤
│  Chromium + V8 (Web Runtime)            │
└─────────────────────────────────────────┘
```

**Tech Stack:**
- Frontend: React 18 with Hooks, Zustand
- Desktop Framework: Electron
- Database: SQLite (better-sqlite3), FTS5
- Build: Webpack + Babel
- AI/LLM: Anthropic Claude API

### Windows Native Version

```
┌─────────────────────────────────────────┐
│    Native Windows Application (.NET 8)  │
├─────────────────────────────────────────┤
│  Presentation Layer (WinUI 3 + XAML)    │
│  ┌──────────────────────────────────┐   │
│  │ Views (XAML)                     │   │
│  │ ViewModels (CommunityToolkit.Mvvm)│   │
│  └──────────────────────────────────┘   │
│              ↕                           │
│  ┌──────────────────────────────────┐   │
│  │ Application Layer (Services)     │   │
│  └──────────────────────────────────┘   │
│              ↕                           │
│  ┌──────────────────────────────────┐   │
│  │ Data Access Layer (EF Core)      │   │
│  └──────────────────────────────────┘   │
└─────────────────────────────────────────┘
```

**Tech Stack:**
- UI Framework: WinUI 3 + XAML
- Runtime: .NET 8
- Database: SQLite + EF Core 8
- MVVM: CommunityToolkit.Mvvm
- AI/LLM: Anthropic Claude API

---

## Project Structure

### Electron Version

```
memory-line/
├── src/
│   ├── main/
│   │   ├── main.js               # Entry point, IPC handlers
│   │   └── preload.js            # Secure bridge
│   ├── renderer/
│   │   ├── components/
│   │   │   ├── audio/            # AudioRecorder, RecordingQueue
│   │   │   ├── common/           # Header, Sidebar
│   │   │   ├── events/           # EventDetails, EventForm
│   │   │   ├── settings/         # SettingsPanel
│   │   │   └── timeline/         # Timeline, EventBubble
│   │   ├── store/                # Zustand state stores
│   │   ├── styles/               # Component CSS
│   │   ├── utils/                # Helpers
│   │   └── App.jsx               # Main component
│   └── database/
│       ├── database.js           # Database service
│       └── schemas/schema.sql    # Schema
├── package.json
├── webpack.renderer.config.js
└── forge.config.js               # Electron Forge config
```

### Windows Native Version

```
windows-native/
├── src/
│   ├── MemoryTimeline/           # WinUI 3 app
│   │   ├── Views/                # XAML pages
│   │   ├── ViewModels/           # MVVM ViewModels
│   │   ├── Controls/             # Custom controls
│   │   └── Assets/               # Resources
│   ├── MemoryTimeline.Core/      # Business logic
│   │   └── Services/             # Service interfaces
│   ├── MemoryTimeline.Data/      # Data access
│   │   ├── Models/               # EF Core entities
│   │   └── Repositories/         # Repository pattern
│   └── MemoryTimeline.Tests/     # Tests
├── scripts/                      # Setup scripts
├── README.md
├── DEVELOPMENT-STATUS.md
├── DEVELOPMENT-HISTORY.md
├── TESTING.md
└── DEPLOYMENT.md
```

---

## Database Schema

Both versions use the **same schema** for compatibility.

### Core Tables
- `events` - Timeline events (id, title, start_date, end_date, description, category, era_id, transcript, embedding_id)
- `eras` - Life phases (id, name, start_date, end_date, color, description)
- `tags` - Categorization (id, name, color)
- `people` - People mentioned (id, name, relationship, notes)
- `locations` - Places (id, name, latitude, longitude, notes)

### Junction Tables
- `event_tags`, `event_people`, `event_locations`

### Processing Tables
- `recording_queue` - Audio files awaiting transcription
- `pending_events` - Extracted events awaiting review

### RAG Tables
- `embeddings` / `event_embeddings` - Vector embeddings
- `cross_references` - Event relationships

### System Tables
- `app_settings` - Key-value configuration
- `events_fts` - Full-text search (Electron only)

---

## Development Guidelines

### Code Conventions

**Electron:**
- Components: PascalCase (`AudioRecorder.jsx`)
- Utilities: camelCase (`dateUtils.js`)
- Styles: Match component (`AudioRecorder.css`)
- Event handlers: Prefix with `handle` (`handleRecordStart`)

**Windows Native:**
- Views: PascalCase (`TimelinePage.xaml`)
- ViewModels: PascalCase with suffix (`TimelineViewModel.cs`)
- Services: Interface + Implementation (`IEventService`, `EventService`)
- Commands: Suffix with `Command` (`SaveCommand`)

### Data Flow Patterns

**Electron:**
```
Renderer (React) → IPC Channel → Main Process → Database
                                              → External API
```

**Windows Native:**
```
View (XAML) → ViewModel → Service → Repository → Database
                       → External API
```

### Adding Features

**Electron:**
1. Update `schema.sql` if new tables needed
2. Add database methods in `database.js`
3. Add IPC handlers in `main.js`
4. Expose via `preload.js`
5. Create React components
6. Add to Zustand store if needed

**Windows Native:**
1. Add EF Core entity in `MemoryTimeline.Data/Models/`
2. Create migration
3. Add repository in `MemoryTimeline.Data/Repositories/`
4. Create service interface in `MemoryTimeline.Core/Services/`
5. Create service implementation
6. Register in DI container
7. Create ViewModel
8. Create XAML View

---

## Integration Points

### Anthropic Claude API
- **Model:** claude-sonnet-4-20250514
- **Use Cases:** Event extraction, cross-reference analysis
- **Prompt Engineering:** Structured prompts for JSON output
- **Retry Logic:** 3 retries with exponential backoff

### Embedding Providers
- OpenAI: text-embedding-3-small (1536 dimensions)
- Voyage AI: voyage-2 (1024 dimensions)
- Cohere: embed-english-v3.0 (1024 dimensions)

### Speech-to-Text Engines

**Electron:**
- Mock (Demo)
- Whisper.cpp (Local)
- OpenAI Whisper API
- Vosk (Local)
- Google Cloud STT
- Deepgram
- AssemblyAI

**Windows Native:**
- Windows Speech Recognition
- Mock (Testing)
- Cloud STT ready

---

## Important Notes for AI Assistants

### When Editing Code

1. **Always read files first** before making changes
2. **Check which version** you're editing (Electron vs Windows Native)
3. **Database operations:**
   - Electron: All in `database.js`, exposed via IPC
   - Windows: Repository pattern with DI
4. **No direct require() in Electron renderer** - use preload.js bridge
5. **CSS organization:** One file per component

### Common Tasks

| Task | Electron | Windows Native |
|------|----------|----------------|
| Add setting | schema.sql → database.js → SettingsPanel.jsx | Entity → Migration → Service → ViewModel → View |
| Add IPC channel | main.js → preload.js → component | N/A (use DI services) |
| Fix DB issue | Delete `memory-timeline.db` | Delete DB, re-run migrations |
| Clear audio queue | Delete files in `userData/recordings/` | Delete from ApplicationData |

### Error Handling

**API Calls:**
- LLM extraction: 3 retries (1s, 2s, 4s delays)
- STT transcription: 2 retries (2s, 4s delays)
- Skip retry on 401/400 errors

### Performance Considerations

1. **Timeline:** Only load events in visible date range
2. **Large audio:** Stream instead of loading entire file
3. **Embeddings:** Generate in batches
4. **Full-text search:** Use FTS5/indexes, not full scans

### Security Considerations

1. **API keys:** Never log or expose
2. **User data:** Stored locally, cloud calls only for transcripts
3. **IPC validation:** Validate all renderer input
4. **SQL injection:** Use prepared statements (Electron) / parameterized queries (EF Core)

---

## Documentation

### Top-Level
| File | Description |
|------|-------------|
| `README.md` | Main project documentation |
| `claude.md` | This file - AI assistant guide |
| `DEPLOYMENT-INSTALL.md` | Deployment guide for Electron |
| `MIGRATION-TO-NATIVE-WIN.md` | Windows native migration roadmap |

### Windows Native (`windows-native/`)
| File | Description |
|------|-------------|
| `README.md` | Windows native overview |
| `DEVELOPMENT-STATUS.md` | Current status and next steps |
| `DEVELOPMENT-HISTORY.md` | Consolidated phase reports |
| `TESTING.md` | Testing guide |
| `DEPLOYMENT.md` | Windows deployment guide |
| `scripts/README.md` | Setup scripts documentation |

---

## Version History

### Electron Version
- **v1.0.0-rc1** - All 7 phases complete, production-ready

### Windows Native Version
- **v0.9.0-beta** - Phases 0-6 complete (86%)
- **Pending:** Phase 7 (Testing & Deployment)

---

**Last Updated:** 2026-01-17
**Electron Version:** 1.0.0-rc1
**Windows Native Version:** 0.9.0-beta
