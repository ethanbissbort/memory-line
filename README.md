# Memory Timeline

A personal memory and event timeline application with audio recording, LLM-powered event extraction, and RAG-based cross-referencing capabilities.

**Status:** Production Ready (Electron v1.0.0-rc1) | Windows Native Beta (86% complete)

---

## Overview

Memory Timeline helps you capture, organize, and discover connections in your personal history through:

- **Audio Recording** - Record memories with voice, transcribe automatically
- **AI Event Extraction** - LLM extracts structured events from your recordings
- **Smart Connections** - RAG technology discovers relationships between events
- **Rich Timeline** - Visualize your life with interactive zoom/pan controls
- **Advanced Search** - Find memories with powerful filtering and faceted search
- **Analytics** - Gain insights with visualizations and statistics

### Two Implementations

| Version | Platform | Status | Tech Stack |
|---------|----------|--------|------------|
| **Electron** | Windows, macOS, Linux | v1.0.0-rc1 (100%) | React, Electron, SQLite |
| **Windows Native** | Windows 11 | Beta (86%) | .NET 8, WinUI 3, SQLite |

---

## Features

### Core Features (Both Versions)

- **Timeline Visualization**
  - Interactive timeline with zoom levels (Year/Month/Week/Day)
  - Smooth pan and zoom with keyboard shortcuts
  - Event bubbles with category icons and tooltips
  - Era-based organization with gradient backgrounds

- **Audio Recording & Processing**
  - Record audio with pause/resume/cancel controls
  - Queue system with background processing
  - Multiple STT engines (9 in Electron, Windows Speech Recognition in Native)
  - Audio playback and metadata display

- **LLM Event Extraction**
  - Anthropic Claude API integration
  - Extract title, dates, description, category
  - Extract tags, people, locations
  - Confidence scoring per field
  - Review queue with approve/edit/reject workflow

- **RAG Cross-Referencing**
  - Vector embeddings (OpenAI, Voyage AI, Cohere)
  - Semantic similarity search
  - 6 relationship types (causal, thematic, temporal, person, location, follow-up)
  - Pattern detection (recurring categories, clusters, era transitions)
  - Smart tag suggestions

- **Data Management**
  - SQLite database with full-text search
  - Export to JSON, CSV, Markdown
  - Import from JSON with duplicate detection
  - Database backup and optimization

### Electron-Exclusive Features

- **Advanced Search & Filtering**
  - Multi-dimensional faceted search
  - Autocomplete suggestions
  - Save favorite searches
  - Pagination with customizable page sizes

- **Batch Audio Import**
  - Import multiple audio files at once
  - Directory scanning (recursive)
  - Auto-process with STT and extraction

- **Analytics & Visualizations**
  - Category distribution charts
  - Timeline density analysis
  - Tag cloud visualization
  - People network graph
  - Activity heatmaps

### Windows Native-Exclusive Features

- **Deep Windows Integration**
  - Toast notifications with action buttons
  - JumpList (recent events, quick actions)
  - Windows Timeline integration
  - Protocol handler (memory-timeline://)

- **Performance**
  - 60 FPS timeline rendering
  - 5x better memory efficiency
  - 2x faster cold start
  - 4x smaller package size

- **Input Support**
  - Full touch gestures (pinch, swipe)
  - Windows Ink ready
  - High-DPI support

---

## Quick Start

### Electron Version

**Prerequisites:**
- Node.js 16+ and npm
- Windows 11, macOS, or Linux

**Installation:**
```bash
# Clone repository
git clone <repository-url>
cd memory-line

# Install dependencies
npm install

# Run in development mode
npm run dev
```

**Production Build:**
```bash
npm run build
npm run package
```

### Windows Native Version

**Prerequisites:**
- Windows 11 (22H2+)
- Visual Studio 2022 (17.8+)
- .NET 8 SDK

**Installation:**
```powershell
# Navigate to windows-native
cd windows-native/src

# Restore packages
dotnet restore

# Build
dotnet build

# Or open MemoryTimeline.sln in Visual Studio and press F5
```

See [`windows-native/README.md`](./windows-native/README.md) for detailed Windows native setup.

---

## Configuration

### API Keys Required

1. **Anthropic API Key** (for Claude LLM)
   - Get from: https://console.anthropic.com/
   - Used for: Event extraction, cross-reference analysis

2. **OpenAI API Key** (optional, for embeddings)
   - Get from: https://platform.openai.com/api-keys
   - Used for: Vector embeddings for similarity search

### Default Settings

```json
{
  "theme": "light",
  "default_zoom_level": "month",
  "audio_quality": "high",
  "llm_provider": "anthropic",
  "llm_model": "claude-sonnet-4-20250514",
  "rag_similarity_threshold": "0.75"
}
```

Configure via Settings panel in the application.

---

## Project Structure

```
memory-line/
├── src/                           # Electron source code
│   ├── main/                      # Main process
│   │   ├── main.js               # Entry point, IPC handlers
│   │   └── preload.js            # Secure bridge
│   ├── renderer/                  # React UI
│   │   ├── components/           # UI components
│   │   ├── store/                # Zustand state
│   │   ├── styles/               # CSS
│   │   └── App.jsx               # Main component
│   └── database/
│       ├── database.js           # Database service
│       └── schemas/schema.sql    # Database schema
│
├── windows-native/                # Windows native implementation
│   ├── src/
│   │   ├── MemoryTimeline/       # WinUI 3 app
│   │   ├── MemoryTimeline.Core/  # Business logic
│   │   ├── MemoryTimeline.Data/  # Data access
│   │   └── MemoryTimeline.Tests/ # Tests
│   ├── README.md
│   ├── DEVELOPMENT-STATUS.md
│   ├── DEVELOPMENT-HISTORY.md
│   ├── TESTING.md
│   └── DEPLOYMENT.md
│
├── package.json                   # npm dependencies
├── README.md                      # This file
├── claude.md                      # AI assistant guide
├── DEPLOYMENT-INSTALL.md          # Electron deployment
└── MIGRATION-TO-NATIVE-WIN.md     # Windows migration roadmap
```

---

## Technology Stack

### Electron Version

| Component | Technology |
|-----------|------------|
| Frontend | React 18, Zustand |
| Desktop | Electron |
| Database | SQLite (better-sqlite3), FTS5 |
| Build | Webpack, Babel |
| AI/LLM | Anthropic Claude API |
| Date Handling | date-fns |

### Windows Native Version

| Component | Technology |
|-----------|------------|
| UI Framework | WinUI 3 + XAML |
| Runtime | .NET 8 |
| Database | SQLite + EF Core 8 |
| MVVM | CommunityToolkit.Mvvm |
| Audio | Windows.Media.Capture |
| STT | Windows.Media.SpeechRecognition |
| AI/LLM | Anthropic Claude API |

---

## Database Schema

Both versions use the same SQLite schema for compatibility:

**Core Tables:**
- `events` - Timeline events
- `eras` - Life phases/periods
- `tags`, `people`, `locations` - Categorization

**Processing Tables:**
- `recording_queue` - Audio awaiting processing
- `pending_events` - Extracted events awaiting review

**RAG Tables:**
- `embeddings` / `event_embeddings` - Vector embeddings
- `cross_references` - Event relationships

**Database Location:**
- Electron: `userData/memory-timeline.db`
- Windows: `%LOCALAPPDATA%\MemoryTimeline\memory-timeline.db`

---

## Usage Guide

### Recording Audio Memories

1. Navigate to the "Record" panel
2. Click "Start Recording"
3. Speak clearly about the memory, including dates, names, locations
4. Stop recording when finished
5. Preview and submit to queue

### Processing Queue

1. Ensure API key is configured in Settings
2. Click "Process Queue"
3. STT transcribes audio
4. LLM extracts structured events
5. Review extracted events in Review Queue

### Managing Timeline

- **Zoom:** Use +/- buttons or scroll wheel
- **Pan:** Click and drag
- **Navigate:** Use Previous/Next/Today buttons
- **Keyboard:** ←/→ navigate, +/- zoom, T for today

### RAG Cross-References

1. Configure embedding provider in Settings
2. Generate embeddings for events
3. Use "Analyze Timeline" to discover connections
4. View connections in event details panel

---

## Development Status

### Electron Version - Complete (v1.0.0-rc1)

All 7 phases complete:
- Phase 1: Core Infrastructure
- Phase 2: Audio & Recording
- Phase 3: LLM Integration
- Phase 4: Timeline Visualization
- Phase 5: RAG Cross-Referencing
- Phase 6: Polish & Optimization
- Phase 7: Extended Features (Search, Batch Import, Analytics)

### Windows Native Version - 86% Complete

| Phase | Status |
|-------|--------|
| Phase 0: Preparation | ✅ Complete |
| Phase 1: Core Infrastructure | ✅ Complete |
| Phase 2: Timeline Visualization | ✅ Complete |
| Phase 3: Audio & Processing | ✅ Complete |
| Phase 4: LLM Integration | ✅ Complete |
| Phase 5: RAG & Embeddings | ✅ Complete |
| Phase 6: Polish & Integration | ✅ Complete |
| Phase 7: Testing & Deployment | ⬜ Pending |

See [`windows-native/DEVELOPMENT-STATUS.md`](./windows-native/DEVELOPMENT-STATUS.md) for details.

---

## Documentation

| Document | Description |
|----------|-------------|
| [`README.md`](./README.md) | This file - project overview |
| [`claude.md`](./claude.md) | AI assistant development guide |
| [`DEPLOYMENT-INSTALL.md`](./DEPLOYMENT-INSTALL.md) | Electron deployment guide |
| [`MIGRATION-TO-NATIVE-WIN.md`](./MIGRATION-TO-NATIVE-WIN.md) | Windows migration roadmap |
| [`windows-native/README.md`](./windows-native/README.md) | Windows native overview |
| [`windows-native/DEVELOPMENT-STATUS.md`](./windows-native/DEVELOPMENT-STATUS.md) | Windows development status |
| [`windows-native/DEVELOPMENT-HISTORY.md`](./windows-native/DEVELOPMENT-HISTORY.md) | Consolidated phase reports |
| [`windows-native/TESTING.md`](./windows-native/TESTING.md) | Windows testing guide |
| [`windows-native/DEPLOYMENT.md`](./windows-native/DEPLOYMENT.md) | Windows deployment guide |

---

## Privacy & Security

- **Local-First:** All data stored locally in SQLite
- **No Automatic Cloud Sync:** You control when data leaves your device
- **Selective API Calls:** Only transcripts sent to LLM (configurable)
- **User Confirmation:** Required for all LLM operations (configurable)
- **Secure Storage:** API keys stored using OS credential manager
- **Audio Retention:** Original recordings never automatically deleted

---

## Troubleshooting

### Electron

**npm install fails:**
```bash
npm install --ignore-scripts  # For restricted environments
```

**Database issues:**
- Backup database before troubleshooting
- Delete `memory-timeline.db` to reset (data loss!)
- Use "Optimize Database" in Settings

**Audio recording issues:**
- Check microphone permissions
- Verify input device in Settings

### Windows Native

**Build errors:**
- Ensure Visual Studio 2022 17.8+
- Install .NET 8 SDK
- Run `dotnet restore --force-evaluate`

**Runtime errors:**
- Check `%LOCALAPPDATA%\MemoryTimeline\` exists
- Verify microphone permissions

See respective documentation for detailed troubleshooting.

---

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

### Guidelines

- Follow existing code style
- Add tests for new features
- Update documentation as needed
- Maintain feature parity between versions when appropriate

---

## Future Enhancements

- Mobile companion apps (iOS/Android)
- Cloud sync and backup options
- Collaborative timeline sharing
- Photo/document attachments
- Enhanced AI features (automatic clustering, smart summaries)
- Multi-language support

---

## License

MIT License - See LICENSE file

---

## Support

- **Issues:** GitHub Issues
- **Discussions:** GitHub Discussions
- **Documentation:** See links above

---

**Version:** 1.0.0-rc1 (Electron) | 0.9.0-beta (Windows Native)
**Last Updated:** 2026-01-17
