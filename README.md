# Memory Timeline Application

A modern desktop application that functions as a personal memory and event timeline with audio recording, LLM-powered event extraction, and RAG-based cross-referencing capabilities.

## Features

### Core Functionality

✅ **Implemented (Phases 1-4 Complete!)**:
- SQLite database with comprehensive schema for events, eras, tags, people, and locations
- Full-text search support for events
- Database service layer with migrations support
- Electron app shell with secure IPC communication
- React-based user interface with Zustand state management
- **Advanced timeline visualization with zoom/pan controls (year/month/week/day views)**
- **Intelligent date markers and labels that adapt to zoom level**
- **Enhanced event bubbles with category icons and hover tooltips**
- **Smooth transitions and animations for zoom changes**
- **Keyboard shortcuts for navigation (←/→ navigate, +/- zoom, T for today)**
- Era-based organization with enhanced gradient backgrounds
- Event CRUD operations (Create, Read, Update, Delete)
- Event details modal with editing capabilities
- Settings panel for app configuration
- Database backup and optimization tools
- Audio recording with pause/resume/cancel controls
- File management with automatic save to user data directory
- Recording queue system with database persistence
- Audio playback from queue with metadata display (duration, file size, timestamps)
- Remove/cancel functionality for queue items
- **Anthropic API integration with secure key storage**
- **LLM-powered event extraction from transcripts**
- **Structured prompt engineering for accurate data extraction**
- **Multi-engine STT service with 9 engine options (local and remote)**
- **STT engine selection UI in Settings with per-engine configuration**
- **Event edit modal for modifying extracted data before approval**
- **Batch processing of queue items**
- **Review queue with extracted event details (title, dates, description, category, tags, people, locations, confidence scores)**
- **Approve/edit/reject workflow that creates timeline events with full relationships**
- **Process Queue button with API key validation**
- **Retry logic with exponential backoff for API calls**
- **Enhanced error handling with detailed error messages**
- **Vector embedding generation with multiple provider support (OpenAI, Voyage AI, Cohere, Local)**
- **Semantic similarity search for finding related events**
- **RAG-powered cross-reference analysis with LLM-determined relationships**
- **Automatic pattern detection (recurring categories, temporal clusters, era transitions)**
- **Smart tag suggestions based on similar events**
- **Cross-reference panel showing connections, similar events, and suggested tags**
- **RAG configuration UI in Settings with embedding provider selection**

✅ **Phase 6 Complete!**
- **Performance optimizations with pagination and caching for 1000+ events**
- **Comprehensive error handling with ErrorBoundary component**
- **Export functionality (JSON, CSV, Markdown) with import support**
- **Database optimization tools with automatic index management**
- **Crash recovery and error logging**
- **Installer configuration for Windows, macOS, and Linux**
- **Complete deployment and installation documentation**

✅ **Completed**: Phases 1-6 complete! Production-ready with full polish and optimization!

📋 **Planned (Phase 7+)**:
- Enhanced search and filtering with facets
- Batch audio import
- Advanced visualizations (graphs, charts)
- Native Windows implementation with touch/pen support

## Technology Stack

- **Frontend**: React 18 with Hooks
- **State Management**: Zustand
- **Desktop Framework**: Electron
- **Database**: SQLite with better-sqlite3
- **Build Tool**: Webpack with Babel
- **Styling**: Custom CSS with CSS Variables
- **Date Handling**: date-fns
- **AI/LLM**: Anthropic Claude API (planned)

## Project Structure

```
memory-line/
├── src/
│   ├── main/                    # Electron main process
│   │   ├── main.js             # Main process entry point
│   │   └── preload.js          # Preload script for secure IPC
│   ├── renderer/                # React renderer process
│   │   ├── components/
│   │   │   ├── audio/          # Audio recording components
│   │   │   ├── common/         # Shared components (Header, Sidebar)
│   │   │   ├── events/         # Event display and editing
│   │   │   ├── settings/       # Settings panel
│   │   │   └── timeline/       # Timeline visualization
│   │   ├── store/              # Zustand state stores
│   │   ├── styles/             # CSS stylesheets
│   │   ├── utils/              # Utility functions
│   │   ├── App.jsx             # Main App component
│   │   └── index.jsx           # React entry point
│   └── database/
│       ├── database.js         # Database service layer
│       └── schemas/
│           └── schema.sql      # Database schema
├── public/
│   └── index.html              # HTML template
├── assets/                      # Static assets (audio files, etc.)
├── package.json
├── webpack.renderer.config.js
└── .babelrc
```

## Database Schema

The application uses SQLite with the following main tables:

- **events**: Core timeline events with dates, descriptions, categories
- **eras**: Life phases/periods with color coding
- **tags**: Categorization tags for events
- **people**: People mentioned in events
- **locations**: Significant places
- **recording_queue**: Audio files awaiting processing
- **pending_events**: Extracted events awaiting user review
- **cross_references**: Relationships between events (RAG-generated)
- **app_settings**: Application configuration

Full-text search is supported via SQLite FTS5 virtual tables.

## Installation & Setup

### Prerequisites

- Node.js 16+ and npm
- Windows 11 (primary target), macOS or Linux for development

### Installation Steps

1. **Clone the repository**:
   ```bash
   git clone <repository-url>
   cd memory-line
   ```

2. **Install dependencies**:
   ```bash
   npm install
   ```

   Note: If you encounter Electron download issues in restricted environments, use:
   ```bash
   npm install --ignore-scripts
   ```

3. **Configure Anthropic API** (for LLM features):
   - Get an API key from https://console.anthropic.com/
   - Enter the key in Settings panel once the app is running
   - Keys are stored securely using OS-native credential storage

### Running the Application

**Development Mode**:
```bash
npm run dev
```
This starts both the webpack dev server and Electron in watch mode.

**Production Build**:
```bash
npm run build
npm run package
```

## Configuration

Settings are stored in the SQLite database and can be modified through the Settings panel or directly:

### Default Settings

```json
{
  "theme": "light",
  "default_zoom_level": "month",
  "audio_quality": "high",
  "llm_provider": "anthropic",
  "llm_model": "claude-sonnet-4-20250514",
  "llm_max_tokens": "4000",
  "llm_temperature": "0.3",
  "rag_auto_run_enabled": "false",
  "rag_schedule": "weekly",
  "rag_similarity_threshold": "0.75",
  "send_transcripts_only": "true",
  "require_confirmation": "true"
}
```

### Audio Quality Settings

- **High**: 16kHz, 16-bit (recommended for best speech-to-text results)
- **Medium**: 8kHz, 16-bit
- **Low**: 8kHz, 8-bit

## Usage Guide

### Creating Events Manually

1. Click on the timeline or use the "New Event" button
2. Fill in event details (title, dates, description, category)
3. Add tags, people, and locations
4. Assign to an era (optional)
5. Save

### Recording Audio Memories

1. Navigate to the "Record" panel
2. Click "Start Recording"
3. Speak clearly about the memory, including:
   - Dates and timeframes
   - Names of people involved
   - Locations
   - Significance of the event
4. Stop recording when finished
5. Preview the audio
6. Submit to queue for processing

### Managing Eras

Eras are life phases (e.g., "College Years", "First Job", "Living in NYC") that provide context and visual organization:

1. Open Settings → Era Management (planned)
2. Create new era with:
   - Name
   - Start/end dates
   - Color code (for timeline visualization)
   - Description
3. Events within the date range will be associated with the era

### Timeline Navigation

- **Zoom**: Use +/- buttons or scroll wheel
- **Pan**: Click and drag the timeline
- **Jump**: Use Previous/Next/Today buttons
- **Zoom Levels**: Year (3-year view), Month (3-month view), Week (3-week view), Day (7-day view)

### Search & Filter

- Use the search box for full-text search across titles, descriptions, and transcripts
- Filter by category, era, tags, people, or locations (UI pending)
- Date range filtering (UI pending)

## LLM Event Extraction

When audio recordings are processed, the system:

1. Transcribes audio using speech-to-text
2. Sends transcript to Claude with structured prompt
3. Extracts structured event data:
   - Title
   - Start/end dates
   - Description
   - Category
   - Suggested era, tags, people, locations
   - Confidence score
4. Presents extracted data to user for review
5. User can edit, approve, or reject
6. Approved events are added to timeline

### Extraction Prompt Template

The system uses a carefully crafted prompt that instructs Claude to:
- Extract factual information
- Preserve the user's voice
- Provide structured JSON output
- Include confidence scores
- Handle ambiguity gracefully

### Event Editing

Before approving extracted events, you can edit them:
1. Click "Edit" on any event in the Review Queue
2. Modify any field (title, dates, description, category, tags, people, locations)
3. Add or remove tags, people, and locations
4. Save changes to approve and add to timeline
5. Or approve without editing for quick workflow

## Speech-to-Text (STT) Configuration

The app supports multiple STT engines for audio transcription. Configure your preferred engine in Settings → Speech-to-Text Engine.

### Available Engines

**Local (Free) Engines:**
- **Mock (Demo)** - Returns demo transcript for testing
- **Whisper.cpp (Recommended)** - OpenAI Whisper running locally
  - Cost: Free
  - Accuracy: Excellent
  - Setup: `npm install whisper-node`
  - Models: tiny (39MB), base (74MB), small (244MB), medium (769MB), large (1.5GB)
- **Vosk** - Lightweight offline recognition
  - Cost: Free
  - Accuracy: Good
  - Setup: `npm install vosk` + download model from https://alphacephei.com/vosk/models
  - Models: Small (50MB), Large (1.8GB)

**Remote (Paid) Engines:**
- **OpenAI Whisper API (Recommended for ease)** - Cloud version
  - Cost: $0.006/minute
  - Accuracy: Excellent
  - Setup: API key from https://platform.openai.com/api-keys
- **Google Cloud Speech-to-Text**
  - Cost: $0.006-0.024/15 seconds
  - Accuracy: Excellent
  - Setup: Google Cloud account + credentials.json
  - Features: 120+ languages, punctuation, real-time
- **Deepgram**
  - Cost: $0.0125/minute
  - Accuracy: Very good
  - Setup: API key from https://deepgram.com/
- **AssemblyAI**
  - Cost: $0.00025/second ($0.015/minute)
  - Accuracy: Very good
  - Setup: API key from https://www.assemblyai.com/

### Configuring STT Engine

1. Open Settings → Speech-to-Text Engine
2. Select your preferred engine
3. Enter engine-specific configuration:
   - **Whisper API**: OpenAI API key
   - **Whisper Local**: Model size (base recommended)
   - **Vosk**: Path to downloaded model
   - **Google Cloud**: Path to credentials.json
   - **Deepgram/AssemblyAI**: API key
4. Click "Initialize STT Engine"
5. The engine will be used for all future transcriptions

### Error Handling & Retry Logic

The app includes robust error handling:
- **Automatic retries** with exponential backoff for failed API calls
- **Up to 3 retries** for LLM extraction (1s, 2s, 4s delays)
- **Up to 2 retries** for STT transcription (2s, 4s delays)
- **Detailed error messages** showing what failed and why
- **Database tracking** of failed items with timestamps
- **No retry** on authentication errors (401) or invalid requests (400)

## RAG Cross-Referencing

The RAG (Retrieval-Augmented Generation) system analyzes your entire timeline to find connections using vector embeddings and LLM analysis.

### Features

- **Causal relationships**: Event A led to Event B
- **Thematic connections**: Similar themes or topics
- **Temporal patterns**: Recurring events or cycles
- **Person/location links**: Shared people or places
- **Tag suggestions**: Context-aware tagging based on similar events
- **Semantic similarity**: Find related events using embedding-based search

### Setup

1. **Configure Embedding Provider** (Settings → RAG Settings):
   - Choose provider: OpenAI, Voyage AI, Cohere, or Local
   - Select model (e.g., text-embedding-ada-002 for OpenAI)
   - Enter API key (if using cloud provider)
   - Click "Initialize Embedding Service"

2. **Generate Embeddings**:
   - Click "Generate All Embeddings" to process all events
   - Or enable "Auto-generate embeddings for new events"
   - Embeddings are vector representations of event text

3. **Analyze Timeline**:
   - Click "Analyze Timeline for Cross-References" to discover connections
   - Set similarity threshold (0.5-0.95) - higher = more strict
   - LLM will analyze similar events and determine relationships

### Using Cross-References

When viewing an event's details, the Cross-Reference Panel shows:

**Connections Tab**:
- Discovered relationships between events
- Relationship types: causal, thematic, temporal, person, location
- Confidence scores and explanations

**Similar Events Tab**:
- Events with similar content (based on embeddings)
- Similarity scores (cosine similarity)
- Quick preview of event details

**Suggested Tags Tab**:
- Tags commonly used on similar events
- Confidence scores based on frequency
- One-click tag addition

### Pattern Detection

Click "Detect Patterns" to find:
- **Recurring Categories**: Event types that appear frequently
- **Temporal Clusters**: Time periods with high event density
- **Era Transitions**: Milestone events near era boundaries

### Privacy & Control

- **Explicit user consent required** - Never runs automatically without permission
- **Manual trigger** - Run on-demand with confirmation dialog
- **Transparent** - Shows confidence scores and reasoning
- **Local storage** - Embeddings and cross-references stored in local SQLite database

## Data Privacy

- **Local-first**: All data stored locally in SQLite
- **No automatic cloud sync**
- **Selective API calls**: Only transcripts sent to LLM (configurable)
- **User confirmation**: Required for all LLM operations (configurable)
- **Secure storage**: API keys stored using OS credential manager
- **Audio retention**: Original recordings never deleted

## Performance Considerations

### Timeline Rendering

For large timelines (110+ years of data), the app uses:

- **Lazy loading**: Only loads events in current view range
- **Efficient queries**: Indexed date ranges in SQLite
- **DOM virtualization**: Renders only visible events
- **Memory threshold**: Keeps database < 132MB for in-RAM operation

### Database Size Management

- Regular vacuum to reclaim space
- Efficient blob storage for audio (external files, not in DB)
- Indexed columns for fast queries
- Full-text search virtual tables

## Development Roadmap

### Phase 1: Core Infrastructure ✅ (COMPLETED)
- Database schema and migrations
- Basic GUI shell with timeline view
- Event CRUD operations
- Era management foundation
- Settings system

### Phase 2: Audio & Recording ✅ (COMPLETED)
- Audio recording functionality with MediaRecorder API
- Queue system implementation with database integration
- File management with auto-save to user data directory
- Audio playback from queue
- Remove/cancel queue items
- Review queue with approve/reject functionality

### Phase 3: LLM Integration - Extraction ✅ (COMPLETED)
- Anthropic API integration with @anthropic-ai/sdk
- Secure API key storage in database
- Mock transcription service (placeholder for STT integration)
- Event extraction with structured prompts
- JSON parsing and validation
- Batch processing of queue items
- IPC handlers for LLM operations
- Settings UI for API key management
- Process Queue button with validation
- Automatic pending event creation
- Full approval workflow to timeline

### Phase 4: Timeline Visualization Enhancement ✅ (COMPLETED)
- Advanced zoom/pan controls with smooth transitions
- Enhanced event bubbles with category icons and interactive hover tooltips
- Intelligent date markers and labels that adapt to zoom level
- Era visualization with enhanced gradients and backdrop effects
- Keyboard shortcuts for navigation (←/→, +/-, T)
- Responsive sizing based on zoom level
- Duration events shown differently from point events
- Keyboard hints displayed in timeline info panel

### Phase 5: RAG Cross-Referencing ✅ (COMPLETED)
- Vector embedding generation with multiple providers (OpenAI, Voyage, Cohere, Local)
- Cosine similarity calculation for semantic event matching
- RAG-powered cross-reference analysis using LLM
- Cross-reference UI panel with connections, similar events, and tag suggestions
- Pattern detection system (recurring categories, temporal clusters, era transitions)
- Smart tag suggestion based on similar events
- RAG configuration in Settings panel
- Database schema for embeddings and cross-references

### Phase 6: Polish & Optimization ✅ (COMPLETED)
- Performance optimization for 1000+ events with pagination and caching
- Comprehensive error handling with ErrorBoundary component
- Export functionality (JSON, CSV, Markdown) with import support
- Error boundaries and crash recovery
- Installer creation configured for Windows, macOS, and Linux
- Comprehensive deployment and installation documentation

### Phase 7: Native Windows Implementation 📋 (PLANNED)
- **Native Windows App Branch**: Create separate implementation using Windows-native technologies
- **Touch & Pen Support**:
  - Windows Ink integration for handwritten notes and annotations
  - Touch gesture support for timeline navigation (pinch-to-zoom, swipe)
  - Pen pressure sensitivity for drawing on timeline
  - Palm rejection and hover states
- **Performance Optimizations**:
  - DirectX-based timeline rendering for smooth 60fps
  - Windows Composition APIs for fluid animations
  - Hardware-accelerated graphics pipeline
  - Memory-mapped file I/O for large databases
- **NPU/AI Acceleration Stubs**:
  - Windows ML (WinML) integration layer for NPU detection
  - DirectML adapter for neural processing units
  - Stub implementations for:
    - Local speech-to-text transcription (when NPU available)
    - On-device embedding generation for RAG
    - Local sentiment analysis and event categorization
    - Real-time audio preprocessing and enhancement
  - Fallback to CPU/GPU when NPU unavailable
  - Configuration to prefer local NPU vs cloud LLM
- **Windows-Specific Features**:
  - Timeline integration with Windows Timeline API
  - Cortana voice command support
  - Windows Hello authentication
  - OneDrive backup integration (optional)
  - Notification system integration
  - Jump list support for recent events
- **Tech Stack Considerations**:
  - UWP (Universal Windows Platform) or WinUI 3
  - C#/.NET 8 with MAUI for cross-device support
  - Windows App SDK for modern APIs
  - ONNX Runtime for NPU model inference
  - System.Numerics.Tensors for ML operations

## Future Enhancements

- Photo/document attachments
- Collaborative timelines (multi-user)
- Mobile companion app
- Voice commands for recording
- Multi-language support
- Import from other sources (social media, calendar)
- Timeline templates
- Advanced analytics and insights

## Cross-Platform Support

**Current**: Windows 11 (primary target)

**Planned**:
- macOS via Electron
- iOS via Marzipan/Mac Catalyst (long-term)
- Linux via Electron

The current Electron + React architecture supports easy cross-platform deployment.

## Troubleshooting

### Database Issues

- **Backup regularly** using Settings → Database Management
- If corrupted, restore from backup
- Use "Optimize Database" to reclaim space

### Audio Recording Problems

- Check microphone permissions in OS settings
- Verify correct input device is selected
- Test audio quality with preview before submitting

### Performance Issues

- Vacuum database regularly
- Limit timeline view to smaller date ranges
- Clear browser cache (Dev Tools → Application → Storage)

## Contributing

This is a personal project but contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## License

MIT License (see LICENSE file)

## Acknowledgments

- Anthropic for Claude API
- Electron team for the framework
- better-sqlite3 for SQLite bindings
- React team for the UI library

## Support

For issues, questions, or suggestions:
- Open an issue on GitHub
- Check documentation in `/docs` (planned)
- Review the prompt template for LLM customization

---

**Version**: 1.0.0-rc1
**Status**: Release Candidate - Phases 1-6 complete! Production-ready with full polish, optimization, error handling, export functionality, and comprehensive deployment documentation.
**Last Updated**: 2025-11-21
