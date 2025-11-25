# Migration to Native Windows 11 Application

> **Migration Roadmap & Checklist**
>
> **Version:** 1.0.0
> **Target Platform:** Windows 11 (22H2+)
> **Maintenance Strategy:** Side-by-side with Electron (JavaScript) and macOS+iOS branches
> **Last Updated:** 2025-11-25

---

## Table of Contents

1. [Migration Progress Summary](#migration-progress-summary) ⭐ **NEW**
2. [Overview](#overview)
3. [Technology Stack Decision](#technology-stack-decision)
4. [Branch Strategy](#branch-strategy)
5. [Architecture Comparison](#architecture-comparison)
6. [Migration Phases](#migration-phases)
7. [Feature Parity Matrix](#feature-parity-matrix)
8. [Implementation Checklist](#implementation-checklist)
9. [Testing Strategy](#testing-strategy)
10. [Deployment & Distribution](#deployment--distribution)
11. [Performance Targets](#performance-targets)
12. [Timeline & Resources](#timeline--resources)

---

## Migration Progress Summary

### Current Status: Phase 6 Complete, Ready for Phase 7

**Overall Progress:** 86% complete (6 of 7 phases fully complete, Phase 7 pending)

| Phase | Status | Completion | Key Deliverables |
|-------|--------|------------|------------------|
| **Phase 0: Preparation** | ✅ Complete | 100% | Development environment, WinUI 3 skeleton, SQLite demo |
| **Phase 1: Core Infrastructure** | ✅ Complete | 100% | Database (EF Core), Settings, Navigation, Repository Pattern |
| **Phase 2: Timeline Visualization** | ✅ Complete | 100% | Timeline canvas, Zoom/pan, Touch support, TimelinePage UI |
| **Phase 3: Audio & Processing** | ✅ Complete | 100% | Audio recording, Queue system, Windows STT, QueuePage UI |
| **Phase 4: LLM Integration** | ✅ Complete | 100% | Claude API, Event extraction, ReviewPage, PendingEvent workflow |
| **Phase 5: RAG & Embeddings** | ✅ Complete | 100% | Backend services, ConnectionsPage UI, embedding workflow |
| **Phase 6: Polish & Integration** | ✅ Complete | 100% | Export/Import, Windows notifications, JumpList, Timeline |
| **Phase 7: Testing & Deployment** | ⬜ Pending | 0% | Testing, MSIX packaging, Microsoft Store |

### What Works Right Now

#### ✅ Fully Functional Features
1. **Database Layer**
   - Full EF Core implementation with SQLite
   - All entities: Events, Eras, Tags, People, Locations, Cross-references, Embeddings
   - Repository pattern for data access
   - Migrations and schema management

2. **Timeline Visualization**
   - Interactive timeline view with events
   - Zoom levels (Year, Month, Week, Day)
   - Pan and navigation controls
   - Event details display
   - Era visualization

3. **Audio Recording**
   - Record audio using Windows MediaCapture
   - Pause/resume/cancel functionality
   - Queue management system
   - Background processing with retry logic
   - Queue status tracking

4. **Speech-to-Text**
   - Windows Speech Recognition integration
   - Transcription of audio files
   - Progress reporting
   - Confidence scoring

5. **LLM Event Extraction**
   - Anthropic Claude 3.5 Sonnet integration
   - Automatic event extraction from transcripts
   - Category classification (9 categories)
   - Date parsing (including relative dates)
   - Tag, people, and location extraction
   - Confidence scoring

6. **Review Workflow**
   - ReviewPage for pending events
   - Approve/Edit/Reject actions
   - Bulk operations (Approve All, Reject All)
   - Confidence indicators with color coding
   - Integration with timeline

7. **RAG & Embeddings (Complete)**
   - OpenAI embeddings API integration
   - Similarity search (K-nearest neighbors)
   - Cross-reference detection (6 relationship types)
   - Pattern analysis (category patterns, temporal clusters, era transitions)
   - Tag suggestion system
   - ConnectionsPage UI with similar events, cross-references, and tag suggestions
   - Batch embedding generation workflow
   - Automatic embedding generation on event creation

8. **Export & Import (Complete)**
   - Export to JSON, CSV, Markdown formats
   - Import from JSON with validation and duplicate handling
   - Progress reporting for all operations
   - Automatic backup creation on import

9. **Windows 11 Integration (Complete)**
   - Toast notifications with action buttons
   - JumpList integration (recent events + quick actions)
   - Windows Timeline integration with Adaptive Cards
   - Deep linking support

#### ⬜ Not Yet Started
- Comprehensive testing (Phase 7)
- Performance profiling and optimization (Phase 7)
- MSIX packaging (Phase 7)
- Microsoft Store submission (Phase 7)

### Architecture Achievements

**Clean Architecture:**
- ✅ Presentation Layer: WinUI 3 + XAML
- ✅ Application Layer: Services + ViewModels
- ✅ Domain Layer: Models + Interfaces
- ✅ Infrastructure Layer: Repositories + External APIs

**MVVM Pattern:**
- ✅ ObservableObject base with CommunityToolkit.Mvvm
- ✅ IRelayCommand for all user actions
- ✅ x:Bind for performance
- ✅ Proper separation of concerns

**Dependency Injection:**
- ✅ Full DI container setup with Microsoft.Extensions.DependencyInjection
- ✅ Scoped, Singleton, and Transient lifetimes appropriately used
- ✅ All services, repositories, ViewModels, and Pages registered

### Technology Stack (Implemented)

| Component | Technology | Status |
|-----------|-----------|--------|
| **UI Framework** | WinUI 3 + XAML | ✅ |
| **Runtime** | .NET 8 | ✅ |
| **Database** | SQLite + EF Core 8 | ✅ |
| **MVVM** | CommunityToolkit.Mvvm 8.2.2 | ✅ |
| **Audio** | Windows.Media.Capture | ✅ |
| **STT** | Windows.Media.SpeechRecognition | ✅ |
| **LLM** | Anthropic Claude 3.5 Sonnet | ✅ |
| **Embeddings** | OpenAI text-embedding-3-small | ✅ |
| **Navigation** | Frame-based navigation | ✅ |
| **Theming** | Light/Dark mode support | ✅ |

### Documentation Available

- ✅ `windows-native/README-WINDOWS.md` - Windows-specific README
- ✅ `windows-native/DEPLOYMENT.md` - Deployment guide
- ✅ `windows-native/DEVELOPMENT-STATUS.md` - Development status
- ✅ `windows-native/TESTING.md` - Testing guide
- ✅ `windows-native/PHASE0-COMPLETION-REPORT.md` - Phase 0 verification
- ✅ `windows-native/PHASE1-COMPLETED.md` - Phase 1 summary
- ✅ `windows-native/PHASE1-VERIFICATION-REPORT.md` - Phase 1 detailed verification
- ✅ `windows-native/PHASE2-IMPLEMENTATION-PLAN.md` - Phase 2 planning
- ✅ `windows-native/PHASE2-PROGRESS-REPORT.md` - Phase 2 progress
- ✅ `windows-native/PHASE3-VERIFICATION-REPORT.md` - Phase 3 verification
- ✅ `windows-native/PHASE3-4-CODE-COMPLETENESS-VERIFICATION.md` - Phases 3 & 4 complete verification
- ✅ `windows-native/PHASE4-5-VERIFICATION-REPORT.md` - Phases 4 & 5 verification
- ✅ `windows-native/PHASE5-IMPLEMENTATION-REPORT.md` - Phase 5 implementation details
- ✅ `windows-native/PHASE6-IMPLEMENTATION-REPORT.md` - Phase 6 implementation details
- ✅ `windows-native/scripts/README.md` - Setup scripts documentation

### Next Steps (Phase 7: Testing & Deployment)

1. **Comprehensive Testing** - Unit tests, integration tests, UI tests
2. **Performance Profiling** - Ensure 60 FPS with 5000+ events, memory < 100MB
3. **MSIX Packaging** - Create installer package with code signing
4. **Microsoft Store Submission** - Prepare store listing and submit
5. **Documentation** - User manual, deployment guide, troubleshooting

---

## Overview

### Goals

**Primary Objectives:**
- Create native Windows 11 application with superior performance
- Leverage Windows-specific features (NPU, Windows Ink, touch/pen)
- Maintain feature parity with Electron version
- Enable cross-branch feature development
- Future-proof for Windows ecosystem evolution

**Success Criteria:**
- ✅ 60 FPS timeline rendering with 5000+ events
- ✅ Native Windows 11 design language (WinUI 3)
- ✅ NPU acceleration for local AI processing
- ✅ Full touch and pen support
- ✅ < 50% memory usage vs Electron version
- ✅ Instant cold start (< 2 seconds)

### Why Native Windows?

| Aspect | Electron | Native Windows |
|--------|----------|----------------|
| **Performance** | Good (V8 + Chromium) | Excellent (AOT compilation) |
| **Memory** | 150-300 MB baseline | 30-60 MB baseline |
| **Startup Time** | 3-5 seconds | < 2 seconds |
| **NPU Access** | No | Yes (DirectML) |
| **Touch/Pen** | Limited | Full Windows Ink API |
| **UI Framework** | Web technologies | Native WinUI 3 |
| **File Size** | 100-150 MB | 20-40 MB |
| **Auto-Update** | Squirrel | Microsoft Store |
| **Platform Feel** | Web app | Native Windows |

---

## Technology Stack Decision

### Recommended Stack: .NET 8 + WinUI 3 + MAUI

#### Core Technologies

**Application Framework:**
- **.NET 8** (LTS until November 2026)
  - Cross-platform base for potential MAUI expansion
  - Modern C# 12 with nullable reference types
  - AOT compilation support
  - Minimal APIs for clean architecture

**UI Framework:**
- **WinUI 3** (Windows App SDK 1.5+)
  - Modern Fluent Design System
  - Native Windows 11 controls
  - XAML-based declarative UI
  - Hardware acceleration out of the box
  - Touch, pen, keyboard, gamepad support

**Data Layer:**
- **Microsoft.Data.Sqlite** (EF Core 8)
  - Managed SQLite for .NET
  - LINQ queries
  - Migration support
  - Same database schema as Electron version

**AI/ML:**
- **Windows ML (WinML)** for NPU inference
  - ONNX Runtime for model execution
  - DirectML for NPU/GPU acceleration
  - Semantic Kernel for LLM orchestration

**Alternative Stack: C++ + WinUI 3**

For maximum performance:
- **C++/WinRT** with WinUI 3
- **SQLite C API** directly
- **Windows ML C++ APIs**
- **Pros:** Ultimate performance, smallest binary
- **Cons:** Longer development time, more complex

**Recommendation:** Start with .NET 8 + WinUI 3, evaluate C++ only if performance targets not met.

---

## Branch Strategy

### Repository Structure

```
memory-line/
├── main                          # Stable releases (platform-agnostic docs)
├── electron-main                 # Electron app (JavaScript/Node.js)
│   ├── src/
│   ├── package.json
│   └── README-ELECTRON.md
├── windows-native                # Native Windows app (.NET/C#)
│   ├── src/
│   │   ├── MemoryTimeline.sln
│   │   ├── MemoryTimeline/       # Main WinUI 3 project
│   │   ├── MemoryTimeline.Core/  # Shared business logic
│   │   ├── MemoryTimeline.Data/  # Data access layer
│   │   └── MemoryTimeline.Tests/ # Unit tests
│   ├── .github/workflows/        # Windows CI/CD
│   └── README-WINDOWS.md
└── apple-native                  # Future: macOS + iOS app (Swift/SwiftUI)
    ├── MemoryTimeline.xcodeproj
    └── README-APPLE.md
```

### Git Workflow

**Branch Naming:**
- `electron-main` - Electron production branch
- `windows-native` - Windows native production branch
- `apple-native` - macOS/iOS production branch (future)
- `feature/electron/feature-name` - Electron-specific features
- `feature/windows/feature-name` - Windows-specific features
- `feature/shared/feature-name` - Cross-platform features

**Merge Strategy:**
1. Shared features developed in `feature/shared/*`
2. Ported to each platform branch with platform-specific UI
3. Platform-specific features stay in respective branches
4. Documentation merged to `main`

**Tagging:**
- `v1.0.0-electron` - Electron releases
- `v1.0.0-windows` - Windows releases
- `v1.0.0-apple` - Apple releases

---

## Architecture Comparison

### Electron Architecture (Current)

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

### Native Windows Architecture (Target)

```
┌─────────────────────────────────────────┐
│    Native Windows Application (.NET 8)  │
├─────────────────────────────────────────┤
│  Presentation Layer (WinUI 3 + XAML)    │
│  ┌──────────────────────────────────┐   │
│  │ Views (XAML)                     │   │
│  │ ViewModels (MVVM + CommunityToolkit)│
│  │ Behaviors & Converters           │   │
│  └──────────────────────────────────┘   │
│              ↕                           │
│  ┌──────────────────────────────────┐   │
│  │ Business Logic Layer             │   │
│  │ - Services (LLM, STT, RAG)       │   │
│  │ - Models                         │   │
│  │ - Orchestrators                  │   │
│  └──────────────────────────────────┘   │
│              ↕                           │
│  ┌──────────────────────────────────┐   │
│  │ Data Access Layer                │   │
│  │ - EF Core + SQLite               │   │
│  │ - Repository Pattern             │   │
│  │ - Migrations                     │   │
│  └──────────────────────────────────┘   │
├─────────────────────────────────────────┤
│  Windows Runtime (WinRT + .NET)         │
│  - NPU/GPU via DirectML                 │
│  - Windows Ink API                      │
│  - Windows.Storage                      │
└─────────────────────────────────────────┘
```

### Design Patterns

**MVVM (Model-View-ViewModel):**
```
View (XAML) ←→ ViewModel (C#) ←→ Model (C#) ←→ Database
     ↑                                             ↑
  Binding                                      EF Core
```

**Clean Architecture Layers:**
1. **Presentation**: WinUI 3 Views + ViewModels
2. **Application**: Use cases, services, DTOs
3. **Domain**: Business logic, entities, interfaces
4. **Infrastructure**: Database, file system, external APIs

---

## Migration Phases

### Phase 0: Preparation (2 weeks)

**Objectives:**
- Set up Windows development environment
- Create branch structure
- Prototype core technologies

**Automated Setup:**

For automated dependency installation, use the PowerShell setup scripts:

```powershell
# Navigate to the windows-native scripts directory
cd windows-native\scripts

# Run automated setup (requires Administrator)
.\Setup-Dependencies.ps1 -Mode Development

# Verify installation
.\Verify-Installation.ps1 -Detailed
```

See [`windows-native/scripts/README.md`](windows-native/scripts/README.md) for detailed documentation.

**Tasks:**
- [ ] Install Visual Studio 2022 (17.8+) with:
  - [ ] .NET 8 SDK
  - [ ] Windows App SDK
  - [ ] C# development tools
  - [ ] XAML designer
- [ ] Create `windows-native` branch
- [ ] Set up solution structure:
  ```
  MemoryTimeline.sln
  ├── MemoryTimeline (WinUI 3 app)
  ├── MemoryTimeline.Core (business logic)
  ├── MemoryTimeline.Data (data access)
  └── MemoryTimeline.Tests (unit tests)
  ```
- [ ] Create "Hello World" WinUI 3 app
- [ ] Test SQLite connectivity with EF Core
- [ ] Verify Windows ML runtime on target devices

**Deliverables:**
- ✅ Working WinUI 3 skeleton app
- ✅ SQLite read/write demo
- ✅ Development environment documented

**Status:** ✅ **COMPLETE** (See `windows-native/PHASE0-COMPLETION-REPORT.md`)

---

### Phase 1: Core Infrastructure (4 weeks)

**Objectives:**
- Database layer with EF Core
- Settings system
- Basic app shell with navigation

#### 1.1 Database Layer

- [ ] **Define EF Core Models**
  - [ ] Event entity
  - [ ] Era entity
  - [ ] Tag entity
  - [ ] Person entity
  - [ ] Location entity
  - [ ] EventTag (junction)
  - [ ] EventPerson (junction)
  - [ ] EventLocation (junction)
  - [ ] RecordingQueue entity
  - [ ] PendingEvent entity
  - [ ] CrossReference entity
  - [ ] EventEmbedding entity
  - [ ] AppSetting entity

- [ ] **Configure EF Core DbContext**
  - [ ] Define relationships (one-to-many, many-to-many)
  - [ ] Configure indexes
  - [ ] Set up full-text search (via custom SQL)
  - [ ] Implement soft deletes
  - [ ] Add audit fields (created_at, updated_at)

- [ ] **Create Migrations**
  - [ ] Initial migration matching Electron schema
  - [ ] Seed default settings
  - [ ] Add stored procedures for complex queries

- [ ] **Repository Pattern**
  - [ ] IEventRepository interface
  - [ ] IEraRepository interface
  - [ ] IGenericRepository<T> base
  - [ ] Unit of Work pattern

- [ ] **Database Compatibility**
  - [ ] Test importing Electron database
  - [ ] Verify schema parity
  - [ ] Create migration utility (Electron → Windows)

#### 1.2 Settings & Configuration

- [ ] **Settings Service**
  - [ ] SettingsService class
  - [ ] Strongly-typed settings models
  - [ ] JSON configuration file support
  - [ ] Windows.Storage for user data

- [ ] **Secure Storage**
  - [ ] Windows Credential Manager for API keys
  - [ ] Encrypted configuration for sensitive data
  - [ ] Keychain abstraction layer

#### 1.3 App Shell

- [ ] **WinUI 3 App Structure**
  - [ ] App.xaml / App.xaml.cs
  - [ ] MainWindow.xaml
  - [ ] NavigationView with menu items
  - [ ] Title bar customization

- [ ] **Navigation**
  - [ ] Frame-based navigation
  - [ ] NavigationService
  - [ ] Page transition animations

- [ ] **Theming**
  - [ ] Light/Dark mode support
  - [ ] System theme detection
  - [ ] Accent color from Windows

**Deliverables:**
- ✅ Database fully functional with EF Core
- ✅ Settings persistence working
- ✅ App shell with navigation

**Status:** ✅ **COMPLETE** (See `windows-native/PHASE1-COMPLETED.md` and `windows-native/PHASE1-VERIFICATION-REPORT.md`)

---

### Phase 2: Timeline Visualization (6 weeks)

**Objectives:**
- High-performance timeline rendering
- Touch, pen, and mouse input
- Smooth zoom and pan

#### 2.1 Timeline Canvas

- [ ] **Custom Timeline Control**
  - [ ] XAML UserControl for timeline
  - [ ] Virtualization for 5000+ events
  - [ ] DirectX rendering for smooth 60 FPS
  - [ ] Hardware acceleration

- [ ] **Viewport Management**
  - [ ] Visible range calculation
  - [ ] Lazy loading of events
  - [ ] Memory-efficient caching

- [ ] **Event Rendering**
  - [ ] Event bubble templates
  - [ ] Category icons (XAML Geometry)
  - [ ] Color gradients for eras
  - [ ] Duration event bars
  - [ ] Tooltip on hover

#### 2.2 Zoom & Pan

- [ ] **Zoom Controls**
  - [ ] Zoom levels: Year, Month, Week, Day
  - [ ] Pinch-to-zoom (touch)
  - [ ] Mouse wheel zoom
  - [ ] Keyboard shortcuts (+/-)

- [ ] **Pan Controls**
  - [ ] Touch drag
  - [ ] Mouse drag
  - [ ] Keyboard navigation (←/→)
  - [ ] Smooth scrolling

- [ ] **Animations**
  - [ ] Zoom transitions (CompositionAnimations)
  - [ ] Pan momentum
  - [ ] Event appearance animations

#### 2.3 Windows-Specific Enhancements

- [ ] **Windows Ink Support**
  - [ ] InkCanvas overlay for annotations
  - [ ] Pen pressure sensitivity
  - [ ] Palm rejection
  - [ ] Handwriting recognition for notes

- [ ] **Touch Gestures**
  - [ ] Pinch to zoom
  - [ ] Two-finger pan
  - [ ] Swipe for navigation
  - [ ] Long-press for context menu

- [ ] **High-DPI Support**
  - [ ] Per-monitor DPI awareness
  - [ ] Vector icons (SVG → XAML)
  - [ ] Crisp rendering at any scale

**Deliverables:**
- ✅ Smooth 60 FPS timeline with 5000 events
- ✅ Full touch and pen support
- ✅ Zoom/pan feature parity with Electron

**Status:** ✅ **COMPLETE** (See `windows-native/PHASE2-IMPLEMENTATION-PLAN.md` and `windows-native/PHASE2-PROGRESS-REPORT.md`)

---

### Phase 3: Audio Recording & Processing (4 weeks)

**Objectives:**
- Audio capture using Windows APIs
- Queue management
- Speech-to-text integration

#### 3.1 Audio Recording

- [ ] **Windows Audio API**
  - [ ] MediaCapture for recording
  - [ ] Audio device selection
  - [ ] Format: 16kHz, 16-bit WAV
  - [ ] Pause/resume/cancel

- [ ] **Audio Playback**
  - [ ] MediaPlayer for queue playback
  - [ ] Waveform visualization
  - [ ] Playback controls

- [ ] **File Management**
  - [ ] Save to ApplicationData folder
  - [ ] Automatic file naming
  - [ ] Metadata (duration, size, timestamp)

#### 3.2 Queue System

- [ ] **Recording Queue UI**
  - [ ] ListView with queue items
  - [ ] Status indicators (pending, processing, completed, failed)
  - [ ] Play/remove buttons
  - [ ] Progress bars for processing

- [ ] **Queue Service**
  - [ ] QueueService class
  - [ ] Background processing with Task
  - [ ] Retry logic with exponential backoff
  - [ ] Error handling and logging

#### 3.3 Speech-to-Text

- [ ] **STT Service Abstraction**
  - [ ] ISpeechToTextService interface
  - [ ] Multiple engine support:
    - [ ] Local: Windows Speech Recognition
    - [ ] Local: ONNX Whisper model (NPU)
    - [ ] Cloud: OpenAI Whisper API
    - [ ] Cloud: Azure Speech Services

- [ ] **NPU Acceleration**
  - [ ] ONNX Runtime with DirectML
  - [ ] Whisper model quantization (INT8)
  - [ ] On-device inference
  - [ ] Fallback to CPU/GPU if no NPU

**Deliverables:**
- ✅ Audio recording functional
- ✅ Queue processing working
- ✅ Local STT with NPU support

**Status:** ✅ **COMPLETE** (See `windows-native/PHASE3-VERIFICATION-REPORT.md` and `windows-native/PHASE3-4-CODE-COMPLETENESS-VERIFICATION.md`)

---

### Phase 4: LLM Integration (3 weeks)

**Objectives:**
- Event extraction from transcripts
- Review and approval workflow

#### 4.1 LLM Service

- [ ] **Anthropic Claude Integration**
  - [ ] HttpClient for API calls
  - [ ] Structured prompt engineering
  - [ ] JSON response parsing
  - [ ] Retry logic with Polly library

- [ ] **Alternative: Local LLM (NPU)**
  - [ ] Phi-3 or Llama 3.2 via ONNX
  - [ ] DirectML acceleration
  - [ ] Quantized models (4-bit)
  - [ ] Fallback to cloud if quality insufficient

#### 4.2 Event Extraction

- [ ] **Extraction Service**
  - [ ] EventExtractionService class
  - [ ] Batch processing
  - [ ] Confidence scoring
  - [ ] Error handling

- [ ] **Review Queue UI**
  - [ ] Extracted events ListView
  - [ ] Edit dialog for modifications
  - [ ] Approve/reject buttons
  - [ ] Confidence indicators

**Deliverables:**
- ✅ LLM extraction working
- ✅ Review workflow implemented

**Status:** ✅ **COMPLETE** (See `windows-native/PHASE3-4-CODE-COMPLETENESS-VERIFICATION.md`)

---

### Phase 5: RAG & Embeddings (4 weeks)

**Objectives:**
- Vector embeddings with NPU
- Cross-reference detection
- Pattern analysis

#### 5.1 Embedding Service

- [ ] **Vector Embedding Generation**
  - [ ] Local: ONNX text embedding model (NPU)
  - [ ] Cloud: OpenAI embeddings API
  - [ ] Cloud: Voyage AI
  - [ ] Vector storage in SQLite

- [ ] **Similarity Search**
  - [ ] Cosine similarity calculation
  - [ ] K-nearest neighbors
  - [ ] Threshold-based filtering

#### 5.2 RAG Service

- [ ] **Cross-Reference Analysis**
  - [ ] LLM-based relationship detection
  - [ ] 6 relationship types (causal, thematic, etc.)
  - [ ] Confidence scoring

- [ ] **Pattern Detection**
  - [ ] Recurring categories
  - [ ] Temporal clusters
  - [ ] Era transitions

- [ ] **Tag Suggestions**
  - [ ] Similar event analysis
  - [ ] Frequency-based suggestions
  - [ ] Confidence weighting

#### 5.3 RAG UI

- [ ] **Cross-Reference Panel**
  - [ ] Connections tab
  - [ ] Similar events tab
  - [ ] Suggested tags tab
  - [ ] Relationship visualization

**Deliverables:**
- ✅ Cloud-based embeddings via OpenAI (local NPU optional for future)
- ✅ RAG cross-referencing fully functional
- ✅ Pattern detection complete

**Status:** ✅ **COMPLETE**

**Completed:**
- ✅ IEmbeddingService interface
- ✅ OpenAIEmbeddingService implementation
- ✅ IRagService interface
- ✅ RagService implementation with similarity search, cross-references, pattern detection, tag suggestions
- ✅ EventEmbeddingRepository and CrossReferenceRepository
- ✅ Services registered in dependency injection
- ✅ ConnectionsPage UI with three sections (similar events, cross-references, tag suggestions)
- ✅ ConnectionsViewModel with refresh and batch embedding generation
- ✅ Embedding generation workflow (automatic on create, batch processing, manual trigger)
- ✅ Navigation integration with ConnectionsPage
- ✅ Tag suggestion UI in ConnectionsPage

---

### Phase 6: Polish & Windows Integration (3 weeks)

**Objectives:**
- Performance optimization
- Windows 11 integration
- Export/import features

#### 6.1 Performance Optimization

- [ ] **Timeline Rendering**
  - [ ] DirectX rendering pipeline
  - [ ] Composition layers for effects
  - [ ] GPU acceleration for gradients
  - [ ] Achieve 60 FPS with 5000 events

- [ ] **Memory Management**
  - [ ] Viewport-based object pooling
  - [ ] Dispose pattern for large objects
  - [ ] WeakReference for caches
  - [ ] Target < 100 MB for 5000 events

- [ ] **Database Optimization**
  - [ ] Compiled queries (EF Core)
  - [ ] AsNoTracking for read-only
  - [ ] Pagination for large datasets
  - [ ] Connection pooling

#### 6.2 Windows 11 Features

- [ ] **Jump Lists**
  - [ ] Recent timelines
  - [ ] Quick actions
  - [ ] Recent events

- [ ] **Live Tiles / Widgets** (if applicable)
  - [ ] Timeline summary widget
  - [ ] Upcoming events
  - [ ] Statistics

- [ ] **Notifications**
  - [ ] Toast notifications
  - [ ] Processing complete alerts
  - [ ] Error notifications

- [ ] **Windows Timeline Integration**
  - [ ] User Activity API
  - [ ] Timeline cards for events
  - [ ] Deep linking

#### 6.3 Export & Import

- [ ] **Export Service**
  - [ ] JSON export
  - [ ] CSV export
  - [ ] Markdown export
  - [ ] PDF export (via WinUI 3 printing)

- [ ] **Import Service**
  - [ ] JSON import
  - [ ] Electron database import
  - [ ] Conflict resolution
  - [ ] Progress reporting

**Deliverables:**
- ✅ Export/import fully functional (JSON, CSV, Markdown)
- ✅ Windows 11 integration complete (Notifications, JumpList, Timeline)
- ✅ Settings UI enhanced with export/import controls
- ✅ Progress reporting for all operations

**Status:** ✅ **COMPLETE** (See `windows-native/PHASE6-IMPLEMENTATION-REPORT.md`)

---

### Phase 7: Testing & Deployment (3 weeks)

**Objectives:**
- Comprehensive testing
- MSIX packaging
- Microsoft Store submission

#### 7.1 Testing

- [ ] **Unit Tests**
  - [ ] xUnit for test framework
  - [ ] Moq for mocking
  - [ ] FluentAssertions
  - [ ] > 80% code coverage

- [ ] **Integration Tests**
  - [ ] Database integration tests
  - [ ] API integration tests
  - [ ] File system tests

- [ ] **UI Tests**
  - [ ] WinAppDriver for UI automation
  - [ ] Critical user flows
  - [ ] Accessibility testing

- [ ] **Performance Tests**
  - [ ] Load testing with 5000+ events
  - [ ] Memory profiling
  - [ ] Render performance benchmarks

#### 7.2 Packaging

- [ ] **MSIX Package**
  - [ ] Package manifest
  - [ ] App icons (all sizes)
  - [ ] Screenshots for Store
  - [ ] Privacy policy

- [ ] **Code Signing**
  - [ ] EV Code Signing Certificate
  - [ ] SignTool configuration
  - [ ] Automated signing in CI/CD

#### 7.3 Deployment

- [ ] **Microsoft Store**
  - [ ] Partner Center account
  - [ ] App submission
  - [ ] Store listing
  - [ ] Age ratings

- [ ] **Side-loading Option**
  - [ ] Standalone MSIX installer
  - [ ] Installation instructions
  - [ ] PowerShell install script

- [ ] **Auto-Updates**
  - [ ] Microsoft Store auto-update
  - [ ] Or implement custom updater

**Deliverables:**
- ✅ All tests passing
- ✅ MSIX package ready
- ✅ Microsoft Store listing live

---

## Feature Parity Matrix

| Feature | Electron | Windows Native | Status | Notes |
|---------|----------|----------------|--------|-------|
| **Core** |
| Timeline View | ✅ | ⬜ | | 60 FPS target |
| Zoom (4 levels) | ✅ | ⬜ | | Year/Month/Week/Day |
| Pan & Navigate | ✅ | ⬜ | | Touch + mouse |
| Event CRUD | ✅ | ⬜ | | |
| Era Management | ✅ | ⬜ | | |
| **Audio** |
| Audio Recording | ✅ | ⬜ | | MediaCapture API |
| Pause/Resume | ✅ | ⬜ | | |
| Queue System | ✅ | ⬜ | | |
| Playback | ✅ | ⬜ | | |
| **AI/LLM** |
| STT (Cloud) | ✅ | ⬜ | | OpenAI, Azure |
| STT (Local) | ⚠️ Mock | ⬜ | | NPU-accelerated |
| Event Extraction | ✅ | ⬜ | | Claude API |
| Review Queue | ✅ | ⬜ | | |
| **RAG** |
| Embeddings (Cloud) | ✅ | ⬜ | | OpenAI, Voyage, Cohere |
| Embeddings (Local) | ⚠️ Mock | ⬜ | | NPU-accelerated |
| Similarity Search | ✅ | ⬜ | | |
| Cross-References | ✅ | ⬜ | | |
| Pattern Detection | ✅ | ⬜ | | |
| Tag Suggestions | ✅ | ⬜ | | |
| **Data** |
| SQLite Database | ✅ | ⬜ | | EF Core |
| Full-Text Search | ✅ | ⬜ | | |
| Export (JSON, CSV, MD) | ✅ | ⬜ | | + PDF |
| Import (JSON) | ✅ | ⬜ | | |
| Backup/Restore | ✅ | ⬜ | | |
| **Windows-Specific** |
| Touch Gestures | ⚠️ Limited | ⬜ | ✨ | Pinch, swipe |
| Pen/Ink Support | ❌ | ⬜ | ✨ | Windows Ink |
| NPU Acceleration | ❌ | ⬜ | ✨ | DirectML |
| Windows Timeline | ❌ | ⬜ | ✨ | User Activity |
| Jump Lists | ❌ | ⬜ | ✨ | Recent items |
| Live Tiles/Widgets | ❌ | ⬜ | ✨ | Summary cards |

**Legend:**
- ✅ Implemented
- ⬜ Planned
- ⚠️ Partial
- ❌ Not available
- ✨ Windows-exclusive feature

---

## Implementation Checklist

### Pre-Development

- [ ] **Environment Setup**
  - [ ] Windows 11 22H2+ installation
  - [ ] Visual Studio 2022 (17.8+)
  - [ ] .NET 8 SDK
  - [ ] Windows App SDK 1.5+
  - [ ] Git for Windows

  **Automated Setup Available:**
  Use `windows-native/scripts/Setup-Dependencies.ps1` to automatically install all required dependencies. See [`windows-native/scripts/README.md`](windows-native/scripts/README.md) for details.

- [ ] **Documentation Review**
  - [ ] Read Electron codebase
  - [ ] Document business logic
  - [ ] Create architecture diagrams
  - [ ] List external dependencies

- [ ] **Proof of Concept**
  - [ ] WinUI 3 "Hello World"
  - [ ] SQLite connectivity
  - [ ] Timeline rendering prototype
  - [ ] NPU inference test

### Development Phases

**Phase 0: Preparation** ✅ **COMPLETE**
- [x] Environment setup
- [x] WinUI 3 skeleton app
- [x] SQLite connectivity
- [x] Development environment documented

**Phase 1: Core Infrastructure** ✅ **COMPLETE**
- [x] Database layer (EF Core)
- [x] Settings system
- [x] App shell with navigation

**Phase 2: Timeline Visualization** ✅ **COMPLETE**
- [x] Timeline canvas
- [x] Zoom & pan
- [x] Touch/pen support

**Phase 3: Audio & Processing** ✅ **COMPLETE**
- [x] Audio recording
- [x] Queue management
- [x] STT integration

**Phase 4: LLM Integration** ✅ **COMPLETE**
- [x] Event extraction
- [x] Review workflow

**Phase 5: RAG & Embeddings** ✅ **COMPLETE**
- [x] Vector embeddings (backend)
- [x] Cross-references (backend)
- [x] Pattern detection (backend)
- [x] UI components (ConnectionsPage with 3 sections)
- [x] Embedding generation workflow

**Phase 6: Polish & Windows Integration** ✅ **COMPLETE**
- [x] Export/import (JSON, CSV, Markdown)
- [x] Windows notifications (toast with actions)
- [x] Windows JumpList integration
- [x] Windows Timeline integration
- [x] Settings UI enhancement

**Phase 7: Testing & Deployment** ⬜ **PENDING**
- [ ] Unit tests
- [ ] UI tests
- [ ] MSIX packaging
- [ ] Store submission

### Post-Development

- [ ] **Documentation**
  - [ ] User manual (Windows-specific)
  - [ ] Developer guide
  - [ ] API documentation
  - [ ] Migration guide (Electron → Windows)

- [ ] **Maintenance Plan**
  - [ ] Bug tracking workflow
  - [ ] Feature request process
  - [ ] Release cycle definition
  - [ ] Support channels

---

## Testing Strategy

### Unit Testing

**Framework:** xUnit + Moq + FluentAssertions

**Coverage Targets:**
- Overall: > 80%
- Business Logic: > 90%
- Data Access: > 85%
- UI ViewModels: > 75%

**Test Categories:**
```csharp
[Trait("Category", "Unit")]
[Trait("Category", "Integration")]
[Trait("Category", "UI")]
[Trait("Category", "Performance")]
```

**Sample Test:**
```csharp
public class EventServiceTests
{
    [Fact]
    public async Task CreateEvent_ValidData_ReturnsEventId()
    {
        // Arrange
        var dbContext = CreateInMemoryContext();
        var service = new EventService(dbContext);
        var eventData = new CreateEventDto { Title = "Test" };

        // Act
        var result = await service.CreateEventAsync(eventData);

        // Assert
        result.Should().NotBeEmpty();
        var dbEvent = await dbContext.Events.FindAsync(result);
        dbEvent.Should().NotBeNull();
        dbEvent.Title.Should().Be("Test");
    }
}
```

### Integration Testing

**Database Tests:**
- Schema validation
- Migration testing
- Query performance
- Transaction handling

**API Tests:**
- LLM service integration
- STT service integration
- Embedding service integration

**File System Tests:**
- Audio file I/O
- Database backups
- Export/import

### UI Testing

**WinAppDriver Automation:**
```csharp
[TestClass]
public class TimelineUITests
{
    private WindowsDriver<WindowsElement> _session;

    [TestMethod]
    public void ZoomIn_ClickButton_UpdatesZoomLevel()
    {
        var zoomButton = _session.FindElementByAccessibilityId("ZoomInButton");
        zoomButton.Click();

        var zoomLevel = _session.FindElementByAccessibilityId("ZoomLevelText");
        Assert.AreEqual("Month", zoomLevel.Text);
    }
}
```

**Accessibility Testing:**
- Keyboard navigation
- Screen reader support
- High contrast themes
- Narrator compatibility

### Performance Testing

**Metrics:**
- Timeline render time (target: < 33ms for 60 FPS)
- Memory usage (target: < 100 MB for 5000 events)
- Database query time (target: < 50ms for paginated queries)
- Cold start time (target: < 2 seconds)

**Load Testing:**
- 10 events (baseline)
- 100 events (typical)
- 1,000 events (power user)
- 5,000 events (stress test)
- 10,000 events (extreme)

**Profiling Tools:**
- Visual Studio Profiler
- PerfView for ETW traces
- Windows Performance Analyzer
- dotMemory for memory analysis

---

## Deployment & Distribution

### MSIX Packaging

**Package Structure:**
```
MemoryTimeline.msix
├── AppxManifest.xml          # Package manifest
├── Assets/
│   ├── Square44x44Logo.png   # Tile icons
│   ├── Square150x150Logo.png
│   ├── Wide310x150Logo.png
│   └── StoreLogo.png
├── MemoryTimeline.exe         # Main executable
├── MemoryTimeline.dll
├── Dependencies/
│   ├── Microsoft.WindowsAppRuntime.*.msix
│   └── VCLibs.*.appx
└── Resources/                 # Localization, assets
```

**Manifest Configuration:**
```xml
<Package>
  <Identity Name="MemoryTimeline"
            Publisher="CN=Your Name"
            Version="1.0.0.0" />
  <Properties>
    <DisplayName>Memory Timeline</DisplayName>
    <PublisherDisplayName>Your Name</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop"
                       MinVersion="10.0.22000.0"
                       MaxVersionTested="10.0.22621.0" />
  </Dependencies>
  <Capabilities>
    <Capability Name="internetClient" />
    <rescap:Capability Name="runFullTrust" />
    <uap:Capability Name="microphone" />
  </Capabilities>
</Package>
```

### Microsoft Store Submission

**Requirements:**
- [ ] MSIX package signed with EV cert
- [ ] Privacy policy URL
- [ ] App screenshots (1920×1080, 2560×1440)
- [ ] App description (< 10,000 chars)
- [ ] Keywords (max 7)
- [ ] Age rating questionnaire
- [ ] Contact information

**Store Listing:**
- Title: "Memory Timeline - AI-Powered Personal History"
- Short description: < 200 characters
- Full description: Features, benefits, screenshots
- Release notes: What's new in this version

### Side-Loading Distribution

**For Enterprise or Direct Distribution:**

1. **Unsigned Package** (Developer Mode required):
   ```powershell
   Add-AppxPackage -Path MemoryTimeline.msix
   ```

2. **Self-Signed Package**:
   ```powershell
   # Generate certificate
   New-SelfSignedCertificate -Type Custom `
     -Subject "CN=Your Name" `
     -KeyUsage DigitalSignature `
     -FriendlyName "Memory Timeline" `
     -CertStoreLocation "Cert:\CurrentUser\My" `
     -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")

   # Sign package
   signtool sign /fd SHA256 /a /f cert.pfx /p password MemoryTimeline.msix
   ```

3. **Installation Script**:
   ```powershell
   # Install-MemoryTimeline.ps1
   $ErrorActionPreference = "Stop"

   Write-Host "Installing Memory Timeline..."

   # Check Windows version
   $version = [System.Environment]::OSVersion.Version
   if ($version.Build -lt 22000) {
       throw "Windows 11 is required"
   }

   # Install certificate if needed
   if (Test-Path .\cert.cer) {
       Import-Certificate -FilePath .\cert.cer `
         -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   }

   # Install package
   Add-AppxPackage -Path .\MemoryTimeline.msix

   Write-Host "Installation complete!"
   ```

### Auto-Update Mechanism

**Option 1: Microsoft Store** (Recommended)
- Automatic updates via Store
- User control over update timing
- Staged rollout support

**Option 2: Custom Updater**
```csharp
public class UpdateService
{
    private const string UpdateCheckUrl = "https://updates.memorytimeline.app/version.json";

    public async Task<UpdateInfo> CheckForUpdatesAsync()
    {
        var currentVersion = Package.Current.Id.Version;
        var updateInfo = await FetchUpdateInfoAsync();

        if (updateInfo.Version > currentVersion)
        {
            return updateInfo;
        }

        return null;
    }

    public async Task DownloadAndInstallAsync(UpdateInfo update)
    {
        var msixPath = await DownloadUpdateAsync(update.DownloadUrl);
        await InstallUpdateAsync(msixPath);
    }
}
```

---

## Performance Targets

### Rendering Performance

| Metric | Target | Measurement |
|--------|--------|-------------|
| **Timeline FPS** (5000 events) | 60 FPS | GPU profiler |
| **Frame time** | < 16.67ms | DirectX overlay |
| **Zoom transition** | < 200ms | Stopwatch |
| **Pan smoothness** | No dropped frames | Visual inspection |
| **Event hover** | < 50ms response | Input latency |

### Memory Usage

| Dataset | Target | Measurement |
|---------|--------|-------------|
| **Baseline** (empty) | < 30 MB | Task Manager |
| **100 events** | < 50 MB | Performance Monitor |
| **1,000 events** | < 70 MB | dotMemory |
| **5,000 events** | < 100 MB | Memory profiler |
| **10,000 events** | < 150 MB | Stress test |

### Startup Performance

| Scenario | Target | Measurement |
|----------|--------|-------------|
| **Cold start** | < 2 seconds | Stopwatch |
| **Warm start** | < 1 second | Profiler |
| **Resume from suspend** | < 500ms | System metrics |

### Database Performance

| Operation | Target | Measurement |
|-----------|--------|-------------|
| **Paginated query** (100 events) | < 20ms | EF Core logging |
| **Full-text search** | < 50ms | Query profiler |
| **Insert event** | < 10ms | Benchmark |
| **Complex join** | < 100ms | SQL trace |
| **Embedding similarity** | < 200ms | Custom timer |

### AI/ML Performance

| Operation | NPU Target | CPU Fallback | Measurement |
|-----------|-----------|--------------|-------------|
| **STT (1 min audio)** | < 5 seconds | < 30 seconds | Benchmark |
| **Embedding generation** | < 100ms | < 500ms | Timer |
| **LLM extraction** | < 2 seconds | < 10 seconds | API timer |
| **Batch embeddings (100)** | < 10 seconds | < 60 seconds | Profiler |

---

## Timeline & Resources

### Development Timeline

**Total Estimated Time:** 27 weeks (~6.5 months)

| Phase | Duration | Dependencies | Team Size |
|-------|----------|--------------|-----------|
| Phase 0: Preparation | 2 weeks | None | 1-2 developers |
| Phase 1: Core Infrastructure | 4 weeks | Phase 0 | 2 developers |
| Phase 2: Timeline Visualization | 6 weeks | Phase 1 | 2-3 developers |
| Phase 3: Audio & Processing | 4 weeks | Phase 1 | 1-2 developers |
| Phase 4: LLM Integration | 3 weeks | Phase 3 | 1 developer |
| Phase 5: RAG & Embeddings | 4 weeks | Phase 4 | 2 developers |
| Phase 6: Polish | 3 weeks | Phases 2-5 | 2-3 developers |
| Phase 7: Testing & Deployment | 3 weeks | Phase 6 | 1-2 developers + QA |

**Critical Path:** Phases 0 → 1 → 2 → 6 → 7

**Parallelization Opportunities:**
- Phases 3, 4, 5 can partially overlap with Phase 2
- UI work (Phase 2) and backend services (Phases 3-5) can be developed concurrently after Phase 1

### Resource Requirements

**Team Composition:**
- **2 Senior .NET/WinUI Developers** (full-time)
- **1 UI/UX Designer** (part-time for Phase 2)
- **1 QA Engineer** (part-time, full-time in Phase 7)
- **1 DevOps Engineer** (part-time for CI/CD setup)

**Hardware:**
- **Development PCs**: Windows 11 Pro, 16GB+ RAM, NPU-capable CPU (Intel Core Ultra or AMD Ryzen AI)
- **Test Devices**: Various Windows 11 devices (laptop, tablet, desktop, Surface)

**Software Licenses:**
- Visual Studio 2022 Professional/Enterprise
- Microsoft Partner Center account ($19 one-time)
- Code signing certificate ($200-400/year)

**Budget Estimate:**
- Development team: $120,000 - $180,000 (6 months)
- Licenses & certificates: $1,000
- Test hardware: $5,000
- **Total:** $126,000 - $186,000

---

## Risk Assessment & Mitigation

### Technical Risks

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **NPU availability limited** | High | Medium | Robust CPU fallback; test on non-NPU devices |
| **EF Core performance issues** | Medium | Low | Benchmark early; use Dapper for hot paths |
| **WinUI 3 stability** | Medium | Low | Use stable SDK; test on Windows Insider builds |
| **Migration complexity** | High | Medium | Prototype early; create migration tools |
| **Timeline rendering performance** | High | Medium | Use DirectX; virtualization; early profiling |

### Schedule Risks

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Feature creep** | High | Medium | Lock scope after Phase 0; strict change control |
| **Third-party API changes** | Medium | Low | Abstract external dependencies; version pinning |
| **Resource availability** | High | Medium | Cross-train team; document thoroughly |
| **Testing delays** | Medium | Medium | Parallel testing; automated test suite |

### Business Risks

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Low adoption** | High | Low | Beta program; gather feedback; marketing |
| **Electron version preference** | Medium | Medium | Highlight Windows-exclusive features |
| **Maintenance burden** | High | High | Shared business logic; automated testing; CI/CD |
| **Store approval delay** | Low | Medium | Pre-submission review; follow guidelines |

---

## Success Metrics

### Technical Metrics

- ✅ 60 FPS timeline rendering with 5000 events
- ✅ < 100 MB memory usage for 5000 events
- ✅ < 2 second cold start time
- ✅ > 80% code coverage
- ✅ Zero critical bugs in production
- ✅ 95% uptime for critical features

### User Metrics

- ✅ 90% feature parity with Electron version
- ✅ 100% Windows-exclusive features functional
- ✅ 4+ star rating in Microsoft Store
- ✅ < 5% crash rate
- ✅ 80% user satisfaction score

### Business Metrics

- ✅ 1,000+ downloads in first month
- ✅ 25% adoption rate among Electron users
- ✅ 3+ positive reviews per week
- ✅ < 10% support ticket rate

---

## Appendix

### A. Code Migration Examples

#### A.1 Database Model Migration

**Electron (TypeScript):**
```javascript
// src/database/schemas/schema.sql
CREATE TABLE events (
    event_id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    start_date TEXT NOT NULL,
    ...
);
```

**Windows (.NET):**
```csharp
// MemoryTimeline.Data/Models/Event.cs
public class Event
{
    [Key]
    public Guid EventId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    // Navigation properties
    public virtual Era Era { get; set; }
    public virtual ICollection<EventTag> EventTags { get; set; }
}

// MemoryTimeline.Data/AppDbContext.cs
public class AppDbContext : DbContext
{
    public DbSet<Event> Events { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>()
            .HasIndex(e => e.StartDate);

        modelBuilder.Entity<Event>()
            .HasIndex(e => e.Category);
    }
}
```

#### A.2 Service Layer Migration

**Electron (JavaScript):**
```javascript
// src/main/services/exportService.js
class ExportService {
    exportToJSON(filePath) {
        const events = this.db.prepare('SELECT * FROM events').all();
        fs.writeFileSync(filePath, JSON.stringify(events));
        return { success: true };
    }
}
```

**Windows (.NET):**
```csharp
// MemoryTimeline.Core/Services/ExportService.cs
public class ExportService : IExportService
{
    private readonly IEventRepository _eventRepository;

    public ExportService(IEventRepository eventRepository)
    {
        _eventRepository = eventRepository;
    }

    public async Task<ExportResult> ExportToJsonAsync(string filePath)
    {
        var events = await _eventRepository.GetAllAsync();
        var json = JsonSerializer.Serialize(events, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
        return ExportResult.Success();
    }
}
```

#### A.3 UI Component Migration

**Electron (React):**
```jsx
// src/renderer/components/timeline/Timeline.jsx
function Timeline({ events }) {
    return (
        <div className="timeline">
            {events.map(event => (
                <EventBubble key={event.id} event={event} />
            ))}
        </div>
    );
}
```

**Windows (XAML + C#):**
```xml
<!-- Views/TimelinePage.xaml -->
<Page>
    <ScrollViewer>
        <ItemsControl ItemsSource="{x:Bind ViewModel.Events}">
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="models:Event">
                    <controls:EventBubble Event="{x:Bind}" />
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</Page>
```

```csharp
// ViewModels/TimelineViewModel.cs
public class TimelineViewModel : ObservableObject
{
    private ObservableCollection<Event> _events;
    public ObservableCollection<Event> Events
    {
        get => _events;
        set => SetProperty(ref _events, value);
    }

    public async Task LoadEventsAsync()
    {
        var events = await _eventService.GetEventsAsync();
        Events = new ObservableCollection<Event>(events);
    }
}
```

### B. NPU Integration Example

```csharp
// MemoryTimeline.Core/AI/NpuEmbeddingService.cs
public class NpuEmbeddingService : IEmbeddingService
{
    private LearningModel _model;
    private LearningModelSession _session;

    public async Task InitializeAsync()
    {
        // Load ONNX model
        var modelFile = await StorageFile.GetFileFromApplicationUriAsync(
            new Uri("ms-appx:///Models/text-embedding-model.onnx"));

        _model = await LearningModel.LoadFromStorageFileAsync(modelFile);

        // Create session with DirectML (NPU/GPU)
        var device = new LearningModelDevice(LearningModelDeviceKind.DirectML);
        _session = new LearningModelSession(_model, device);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        // Tokenize text
        var tokens = TokenizeText(text);

        // Create input tensor
        var inputTensor = TensorFloat.CreateFromArray(
            new long[] { 1, tokens.Length },
            tokens);

        var binding = new LearningModelBinding(_session);
        binding.Bind("input", inputTensor);

        // Run inference on NPU
        var results = await _session.EvaluateAsync(binding, "");

        // Extract embeddings
        var outputTensor = results.Outputs["embeddings"] as TensorFloat;
        return outputTensor.GetAsVectorView().ToArray();
    }
}
```

### C. Performance Comparison Table

| Metric | Electron (Current) | Windows Native (Target) | Improvement |
|--------|-------------------|------------------------|-------------|
| **Memory (idle)** | 150 MB | 30 MB | 5x better |
| **Memory (5000 events)** | 300 MB | 100 MB | 3x better |
| **Cold start** | 3-5 seconds | < 2 seconds | 2-3x faster |
| **Timeline FPS** | 30-45 FPS | 60 FPS | 1.5-2x smoother |
| **Package size** | 120 MB | 30 MB | 4x smaller |
| **Startup CPU** | 100% (1-2 cores) | 20-30% | 3-5x more efficient |
| **Battery life** | Baseline | +30-50% | Significantly better |

### D. Windows-Specific Features

**Windows Ink API:**
```csharp
// Handwriting on timeline
var inkCanvas = new InkCanvas();
inkCanvas.InkPresenter.InputDeviceTypes =
    CoreInputDeviceTypes.Pen |
    CoreInputDeviceTypes.Touch;

// Recognize handwriting to text
var recognizer = new InkRecognizerContainer();
var results = await recognizer.RecognizeAsync(
    inkCanvas.InkPresenter.StrokeContainer,
    InkRecognitionTarget.All);
```

**Windows Timeline Integration:**
```csharp
// Add to Windows Timeline
var activity = await UserActivityChannel.GetDefault()
    .GetOrCreateUserActivityAsync("event-view-123");

activity.VisualElements.DisplayText = "Viewing: " + eventTitle;
activity.ActivationUri = new Uri("memory-timeline://event/123");
activity.ContentUri = new Uri("https://memorytimeline.app/events/123");

await activity.SaveAsync();
```

---

## Conclusion

This migration plan provides a comprehensive roadmap for creating a native Windows 11 version of Memory Timeline. The project is estimated at **27 weeks** with **2-3 developers** and a budget of **$126k-186k**.

**Key Benefits:**
- 5x better memory efficiency
- 2-3x faster startup
- 60 FPS rendering guaranteed
- Windows-exclusive features (NPU, Ink, Touch)
- Smaller package size
- Better battery life

**Next Steps:**
1. Review and approve this migration plan
2. Set up Windows development environment
3. Create `windows-native` branch
4. Begin Phase 0: Preparation

**Maintenance Strategy:**
The three branches (Electron, Windows Native, Apple Native) will be maintained in parallel with:
- Shared documentation in `main` branch
- Platform-specific features in respective branches
- Cross-platform features ported across branches
- Unified version numbering with platform suffix

---

## Quick Links

- **Setup Scripts**: [`windows-native/scripts/README.md`](windows-native/scripts/README.md)
- **Windows README**: [`windows-native/README-WINDOWS.md`](windows-native/README-WINDOWS.md)
- **Deployment Guide**: [`windows-native/DEPLOYMENT.md`](windows-native/DEPLOYMENT.md)
- **Development Status**: [`windows-native/DEVELOPMENT-STATUS.md`](windows-native/DEVELOPMENT-STATUS.md)
- **Testing Guide**: [`windows-native/TESTING.md`](windows-native/TESTING.md)

---

**Document Ownership:** Development Team
**Review Cycle:** Quarterly
**Next Review:** Q2 2026
**Last Updated:** 2025-11-24

