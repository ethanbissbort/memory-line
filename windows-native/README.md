# Memory Timeline - Windows Native Application

A native Windows 11 implementation of Memory Timeline, featuring local Whisper transcription, AI-powered event extraction, RAG-based cross-referencing, and deep Windows integration.

**This is the primary, actively developed product.** The cross-platform Electron build under the repo's `src/` directory is now in maintenance only.

**Platform:** Windows 11 (22H2+)
**Framework:** .NET 8 + WinUI 3
**Status:** Active development — Phases 0-6 complete; core pipeline recently rebuilt and hardened via a multi-agent feature audit. Builds are green in CI; end-to-end runtime validation is ongoing. Phase 7 (testing, MSIX, Microsoft Store) is in progress.

> This is not "production ready" or runtime-verified yet. See [Recent hardening](#recent-hardening) and [Development Status](#development-status).

---

## Overview

This is the native Windows implementation of Memory Timeline: a **local-first** desktop app for recording, organizing, and rediscovering personal history. You speak a memory, the app transcribes it **locally with Whisper**, a **Claude** model extracts a structured event, you review and approve it, and it lands on an interactive timeline. Optional embeddings surface connections between related memories.

It is designed to provide strong performance and to leverage Windows-specific features such as native touch/pen support (Windows Ink), toast notifications, JumpList, and Windows Timeline integration.

### Advantages Over the Electron Version

Native WinUI 3 replaces a Chromium runtime with the OS's own UI stack. The intended benefits are lower memory use, faster cold start, smoother timeline rendering, a smaller package, and first-class Windows integration.

The table below lists **targets**, not measured/verified results — treat them as design goals for the native app while runtime validation is ongoing:

| Aspect | Electron | Windows Native (target) |
|--------|----------|-------------------------|
| Memory (idle) | ~150 MB | ~30 MB |
| Memory (5000 events) | ~300 MB | ~100 MB |
| Cold start | 3-5 seconds | < 2 seconds |
| Timeline FPS | 30-45 FPS | 60 FPS |
| Package size | ~120 MB | ~30 MB |
| Touch/Pen | Limited | Full Windows Ink |
| Windows Integration | Basic | Full (JumpList, Timeline, Notifications) |

---

## The memory pipeline

Every stage persists its state, so a failure is recoverable and visible in the UI rather than silently swallowed:

```
🎙  Record            →  ⏱  Queue           →  📝  Transcribe        →  🤖  Extract
 Windows MediaCapture     recording_queue       Whisper (local)          Claude → pending_events
 (16 kHz mono WAV)        (pending → done)      offline, on-device       structured, reviewable
                                                                                 │
                                                                                 ▼
🗓  Timeline          ←  ✅  Approve          ←  👀  Review
 event appears,          atomic write:            edit / approve / reject
 auto-refresh            event + tags + people    per pending event
                         + locations, one txn
```

- **Record** — `Windows.Media.Capture` writes a 16 kHz mono WAV under `%LOCALAPPDATA%\MemoryTimeline\AudioRecordings`.
- **Transcribe** — local Whisper transcribes the recorded file; the transcript is persisted.
- **Extract** — Claude produces a structured event (title, dates, description, category, tags/people/locations) into `pending_events`.
- **Approve** — approving writes the event and all its tags/people/locations in **one transaction**; the timeline auto-refreshes via a `WeakReferenceMessenger` `EventCreatedMessage`.

---

## Features

### Core Functionality
- **Timeline Visualization** - Interactive timeline with Year/Month/Week/Day zoom, pan, and keyboard navigation; new/approved events refresh onto the timeline automatically.
- **Audio Recording** - Record audio memories with pause/resume/cancel using Windows `MediaCapture`.
- **Local Speech-to-Text** - Transcribe recordings **on-device** with **Whisper** ([Whisper.net](https://github.com/sandrohanea/whisper.net)); no audio leaves the machine.
- **LLM Event Extraction** - Extract structured events from transcripts using Anthropic **Claude**, held in a review queue with approve / edit / reject.
- **RAG Cross-References** - Discover connections between events using optional OpenAI embeddings; degrades gracefully when no embedding key is configured.
- **Search & Analytics** - Faceted search across events, tags, people, locations, and eras; category distribution, timeline density, tag cloud, people network.
- **Export/Import** - JSON, CSV, and Markdown export with JSON import support.

### Windows 11 Integration
- **Toast Notifications** - Processing-complete alerts.
- **JumpList** - Quick access to recent events and common actions.
- **Windows Timeline** - Events published to Windows Timeline with Adaptive Cards.
- **Theme Support** - Light/Dark/System theme switching.
- **Touch & Pen** - Gesture support with Windows Ink ready.

---

## Quick Start

### Prerequisites

1. **Windows 11** (Version 22H2 or later)
   - Check version: `winver` in the Run dialog.

2. **Visual Studio 2022** (Version 17.8+)
   - Download: https://visualstudio.microsoft.com/
   - Required workloads:
     - `.NET Desktop Development`
     - `Windows App SDK` (Windows App SDK C# templates)

3. **A .NET SDK** — the build is pinned to the **.NET 8** SDK by `windows-native/src/global.json`
   (`"version": "8.0.100"`, `"rollForward": "major"`). If you only have a newer major installed
   (e.g. .NET 9), `rollForward: "major"` lets the build use it — it just won't select the .NET 10
   SDK, which is incompatible with the Windows App SDK PRI resource build task. So developers with
   only .NET 9 are not blocked.

### Automated Setup

```powershell
# Navigate to scripts directory
cd windows-native\scripts

# Run setup (requires Administrator)
.\Setup-Dependencies.ps1 -Mode Development

# Verify installation
.\Verify-Installation.ps1 -Detailed
```

See [`scripts/README.md`](./scripts/README.md) for detailed script documentation.

### Build and Run

The solution defines **x64 / x86 / ARM64** platforms only (**no AnyCPU**), and the app is built
**unpackaged** (`WindowsPackageType=None`). Build **Release | x64**.

> **Important — build with Visual Studio or `msbuild`, not `dotnet build`.**
> WinUI 3 PRI resource generation (`MrtCore.PriGen` → `ExpandPriContent`) uses a .NET Framework
> MSBuild task that does **not** load under the `dotnet` CLI's build engine. Running
> `dotnet build` / `dotnet run` on the WinUI app fails with **error MSB4062**. Build from
> **Visual Studio (F5)** or the Visual Studio **`msbuild.exe`** for a full app build. (`dotnet
> build -p:Platform=x64` still works fine for the `MemoryTimeline.Core` / `MemoryTimeline.Data`
> class libraries in isolation — it's specifically the WinUI app that needs VS/msbuild.)

```powershell
# Clone and navigate
git clone <repository-url>
cd memory-line/windows-native/src

# Full app build (Visual Studio MSBuild), Release | x64:
msbuild MemoryTimeline.sln /t:Restore,Build /p:Configuration=Release /p:Platform=x64

# Or open MemoryTimeline.sln in Visual Studio 2022 and press F5 (debug) / Ctrl+F5 (run).
```

### First Run

- The app creates its SQLite database at `%LOCALAPPDATA%\MemoryTimeline\memory-timeline.db` (WAL mode).
- The first transcription downloads the Whisper `ggml-base.bin` model (~140 MB) to
  `%LOCALAPPDATA%\MemoryTimeline\Models\` — one time, then fully offline.
- Add your API key(s) in **Settings** before processing the queue (see [API Keys & Configuration](#api-keys--configuration)).

### Data & model locations

```
%LOCALAPPDATA%\MemoryTimeline\
├── memory-timeline.db          SQLite database (WAL)
├── AudioRecordings\            recorded 16 kHz mono WAV files
├── Models\ggml-base.bin        Whisper model (downloaded on first use)
└── error.log                   startup / diagnostic log
```

---

## Project Structure

```
windows-native/
├── src/
│   ├── MemoryTimeline/              # Main WinUI 3 application
│   │   ├── Views/                   # XAML pages
│   │   │   ├── TimelinePage.xaml
│   │   │   ├── QueuePage.xaml
│   │   │   ├── ReviewPage.xaml
│   │   │   ├── ConnectionsPage.xaml
│   │   │   └── SettingsPage.xaml
│   │   ├── ViewModels/              # MVVM view models
│   │   ├── Controls/                # Custom controls
│   │   ├── Services/                # Platform services (audio, Whisper STT,
│   │   │                            #   notifications, JumpList, navigation, theme)
│   │   ├── Assets/                  # Images, icons, resources
│   │   ├── App.xaml(.cs)            # Application entry point & DI composition root
│   │   └── MainWindow.xaml          # Main window
│   │
│   ├── MemoryTimeline.Core/         # Business logic layer
│   │   ├── Services/                # Service interfaces & implementations
│   │   │   ├── IEventService.cs / ISettingsService.cs / ITimelineService.cs
│   │   │   ├── ISpeechToTextService.cs
│   │   │   ├── ILlmService.cs (AnthropicLlmService) / IEventExtractionService.cs
│   │   │   ├── IEmbeddingService.cs / IRagService.cs
│   │   │   ├── IExportService.cs / IImportService.cs
│   │   │   ├── INotificationService.cs
│   │   │   ├── IQueueService.cs
│   │   │   └── SettingKeys.cs       # unified snake_case setting keys
│   │   └── Models/                  # Business models / DTOs
│   │
│   ├── MemoryTimeline.Data/         # Data access layer
│   │   ├── Models/                  # EF Core entities
│   │   ├── Repositories/            # Repository pattern (stateless over the factory)
│   │   ├── AppDbContext.cs          # EF Core database context
│   │   └── SchemaUpgrader.cs        # EnsureCreated + idempotent schema-drift repair
│   │
│   ├── MemoryTimeline.Tests/        # Unit & integration tests
│   │   ├── UnitTests/
│   │   ├── Integration/
│   │   └── Performance/
│   │
│   ├── global.json                  # pins the .NET SDK (8.0.100, rollForward major)
│   └── MemoryTimeline.sln           # Visual Studio solution file
│
├── scripts/                         # PowerShell automation scripts
├── packaging/                       # MSIX packaging configuration
├── README.md                        # This file
├── FEATURE-AUDIT.md                 # Feature-by-feature audit & root causes
├── HARDENING-FOLLOWUPS.md           # Deferred hardening items
├── DEVELOPMENT-STATUS.md            # Current development status
├── DEVELOPMENT-HISTORY.md           # Consolidated phase reports
├── TESTING.md                       # Testing guide
└── DEPLOYMENT.md                    # Deployment guide
```

> **Note:** there is intentionally **no** `MemoryTimeline.Data/Migrations/` folder — the stale EF
> Migrations baseline was deleted, and schema is currently created/repaired by `SchemaUpgrader`.
> Regenerating a proper EF migration baseline is a tracked follow-up.

---

## Architecture

### Clean Architecture Layers

```
┌─────────────────────────────────────────┐
│  Presentation Layer (WinUI 3 + XAML)     │
│  ┌──────────────────────────────────┐    │
│  │ Views (XAML)                     │    │
│  │ ViewModels (CommunityToolkit.Mvvm)│   │
│  │ Converters, platform services    │    │
│  └──────────────────────────────────┘    │
│              ↕  WeakReferenceMessenger    │
│  ┌──────────────────────────────────┐    │
│  │ Application / Business Layer      │   │
│  │ - Services (LLM, STT, RAG, queue)│    │
│  │ - DTOs & timeline math           │    │
│  └──────────────────────────────────┘    │
│              ↕                            │
│  ┌──────────────────────────────────┐    │
│  │ Data Access Layer                │    │
│  │ - EF Core 8 + SQLite (WAL)       │    │
│  │ - IDbContextFactory per-operation│    │
│  │ - Repositories + SchemaUpgrader  │    │
│  └──────────────────────────────────┘    │
└─────────────────────────────────────────┘
```

### Key architectural decisions

- **Per-operation `DbContext` via `IDbContextFactory<AppDbContext>`.** A desktop app has no
  request scope, so every repository and service opens a short-lived context per operation
  (`await using var ctx = await factory.CreateDbContextAsync()`). This replaced a single
  app-lifetime context shared across all features — the root cause of intermittent "second
  operation on this context" failures. Because the factory is stateless and thread-safe,
  repositories and most Core services are registered as **Singletons**.
- **`SchemaUpgrader` instead of raw `EnsureCreated`.** On startup, `App.OnLaunched` calls
  `MemoryTimeline.Data.SchemaUpgrader.EnsureSchemaAsync`, which runs `EnsureCreated` and then
  idempotently repairs schema drift (missing tables/columns) on databases created by older builds
  — a stopgap until a full EF migration baseline is regenerated.
- **MVVM with a message bus.** Cross-feature updates (e.g. "event created" → refresh the timeline)
  flow through `WeakReferenceMessenger` rather than tight coupling between view models.
- **Errors are surfaced, not swallowed.** Failures propagate to visible `InfoBar`/status
  affordances instead of disappearing into logs.

### MVVM Pattern

Using `CommunityToolkit.Mvvm` for `ObservableObject`, `[RelayCommand]`, `x:Bind` compiled
bindings, and `WeakReferenceMessenger`.

- **Model**: entities in `MemoryTimeline.Data/Models/`
- **View**: XAML pages in `MemoryTimeline/Views/`
- **ViewModel**: view logic in `MemoryTimeline/ViewModels/`

### Dependency Injection

Services are registered in `App.xaml.cs` (the composition root). The build uses an
`IDbContextFactory` (not a shared scoped `AddDbContext`), and repositories/services are
Singletons over that factory:

```csharp
// Per-operation contexts via a factory (no app-wide shared DbContext).
services.AddDbContextFactory<AppDbContext>();

// Repositories are stateless over the factory (Singleton-safe).
services.AddSingleton<IEventRepository, EventRepository>();
services.AddSingleton<IAppSettingRepository, AppSettingRepository>();
// ... other repositories

// Core services (stateless over the factory/repositories).
services.AddSingleton<ISettingsService, SettingsService>();
services.AddSingleton<IEventService, EventService>();
services.AddSingleton<ITimelineService, TimelineService>();

// Audio & queue. QueueService is an explicit Singleton.
services.AddSingleton<IAudioRecordingService, AudioRecordingService>();
services.AddSingleton<IQueueService, QueueService>();

// Local Whisper is the registered STT engine. The Windows SpeechRecognizer
// stub cannot transcribe files (mic-only) and now fails fast instead.
services.AddSingleton<ISpeechToTextService, WhisperSpeechToTextService>();

// LLM & extraction.
services.AddSingleton<ILlmService, AnthropicLlmService>();
services.AddSingleton<IEventExtractionService, EventExtractionService>();

// RAG & embeddings (RagService no longer depends on ILlmService).
services.AddHttpClient<IEmbeddingService, OpenAIEmbeddingService>();
services.AddSingleton<IRagService, RagService>();

// The Core INotificationService is the single registered interface
// (the duplicate app-project interface was deleted).
services.AddSingleton<INotificationService, Services.NotificationService>();
```

> Cleanup from the audit: the old `AnthropicClaudeService` and the `IAudioService` stub were
> deleted (the real services are `AnthropicLlmService` and `IAudioRecordingService` /
> `IAudioPlaybackService`), and the duplicate app-project `INotificationService` interface was
> removed in favor of the Core one.

---

## Database

### SQLite with Entity Framework Core 8

**Database location:**
```
%LOCALAPPDATA%\MemoryTimeline\memory-timeline.db   (WAL mode)
```

Access goes through `IDbContextFactory<AppDbContext>` — a short-lived context per operation.
Connection configuration lives in `AppDbContext.OnConfiguring`.

### Schema Overview

**Core tables:** `events`, `eras`, `tags`, `people`, `locations`
**Junction tables:** `event_tags`, `event_people`, `event_locations`
**Processing tables:** `recording_queue` (audio awaiting processing), `pending_events` (extracted events awaiting review)
**RAG tables:** `event_embeddings` (vector embeddings), `cross_references` (event relationships)
**Settings:** `app_settings` (key/value; keys defined in `SettingKeys`)

### Schema creation & drift repair

The schema is created and repaired at startup by `SchemaUpgrader.EnsureSchemaAsync`
(`EnsureCreated` + idempotent DDL repairs). There is **no EF Migrations folder** — regenerating a
proper EF migration history is a tracked follow-up (see [Roadmap](#roadmap--known-follow-ups)).

---

## API Keys & Configuration

### API Keys

| Provider | Purpose | Required? |
|----------|---------|-----------|
| **Anthropic** ([console](https://console.anthropic.com/)) | Claude — event extraction | **Yes**, to process recordings into events |
| **OpenAI** ([keys](https://platform.openai.com/api-keys)) | Embeddings — Connections / similarity | Optional; Connections degrades gracefully without it |

### Settings storage

Settings and API keys are configured from the **Settings** page and persisted to the local
**`app_settings`** database table. Keys are unified behind a **`SettingKeys`** constants class
(snake_case keys) so writers and readers never drift; the LLM client **re-reads configuration
live**, so provider/model/key changes apply **without a restart**.

> **Security note:** API keys are stored in the `app_settings` table **today**. Encrypting them at
> rest with **Windows DPAPI** (`ProtectedData`) is a tracked hardening follow-up. (They are **not**
> stored in Windows Credential Manager.)

Embeddings (OpenAI) are optional: `RagService` / the embedding service report availability via
`IsAvailableAsync`, and the Connections feature shows a clear call-to-action instead of failing
when no embedding key is set.

---

## Recent hardening

The core pipeline was recently rebuilt and hardened through a structured, **multi-agent feature
audit and fix pass**. Highlights:

- **Made the voice pipeline real** — replaced a "speech recognition" path that transcribed the
  **live microphone** (not the recorded file) with local file-based **Whisper**
  (`WhisperSpeechToTextService`); the old `WindowsSpeechRecognitionService` now fails fast without
  opening the mic. Fixed the unpackaged storage-path crash, persisted transcripts, and made
  **approve** atomic (event + tags/people/locations in one transaction).
- **Fixed DbContext concurrency** — moved from a single app-wide `AddDbContext` to per-operation
  contexts via `IDbContextFactory`, resolving intermittent "second operation on this context"
  failures behind add-event and search.
- **Unified settings** — a `SettingKeys` constants class ended the writer/reader key mismatch that
  made settings appear to "revert"; the LLM client re-reads config live.
- **DI cleanup** — deleted `AnthropicClaudeService` and the `IAudioService` stub, removed the
  duplicate app-project `INotificationService`, and decoupled `RagService` from `ILlmService`.
- **Schema resilience** — startup uses `SchemaUpgrader` (EnsureCreated + drift repair) after the
  stale EF Migrations baseline was deleted.

Full details: [`FEATURE-AUDIT.md`](./FEATURE-AUDIT.md) (findings + root causes) and
[`HARDENING-FOLLOWUPS.md`](./HARDENING-FOLLOWUPS.md) (deferred items).

---

## Testing

The test project references the WinUI app, so tests are built and run **Release | x64**.

```powershell
cd windows-native\src

# Build the solution first via Visual Studio MSBuild (see Build and Run), then run tests
# against the built assembly:
dotnet vstest MemoryTimeline.Tests\bin\x64\Release\<tfm>\MemoryTimeline.Tests.dll

# Filter by category:
dotnet vstest ... --TestCaseFilter:"FullyQualifiedName~UnitTests"
```

Tests cover timeline math, services, repository/integration behavior, and performance. See
[`TESTING.md`](./TESTING.md) for comprehensive testing documentation.

### Continuous Integration

`.github/workflows/windows-native-build.yml` builds the full solution **Release | x64** on
`windows-latest`, driving the build with **Visual Studio MSBuild** (`microsoft/setup-msbuild`)
because the WinUI PRI task cannot run under `dotnet build`. Tests run best-effort via
`dotnet vstest`. Builds are currently **green** in CI.

---

## Deployment

### MSIX Package

**Visual Studio:**
1. Right-click the `MemoryTimeline` project.
2. Select `Publish > Create App Packages`.
3. Follow the wizard to create the MSIX.

**Command Line (Visual Studio MSBuild):**
```powershell
msbuild MemoryTimeline.sln /t:Publish /p:Configuration=Release /p:Platform=x64
```

### Microsoft Store

See [`DEPLOYMENT.md`](./DEPLOYMENT.md) for complete deployment instructions including MSIX
packaging, code signing, Microsoft Store submission, and side-loading. Packaging and Store
submission are part of Phase 7 (in progress).

---

## Performance Targets

These are **design targets**, not verified/measured results — runtime validation is ongoing:

| Metric | Target |
|--------|--------|
| Timeline FPS (5000 events) | 60 FPS |
| Memory usage (5000 events) | < 100 MB |
| Cold start time | < 2 seconds |
| Database query time | < 50 ms |

---

## Development Status

**Current focus:** Phase 7 (Testing, MSIX packaging & Microsoft Store) — in progress. The core
pipeline was recently rebuilt and hardened; builds are green in CI and end-to-end runtime
validation is ongoing.

| Phase | Status |
|-------|--------|
| Phase 0: Preparation | ✅ Complete |
| Phase 1: Core Infrastructure | ✅ Complete |
| Phase 2: Timeline Visualization | ✅ Complete |
| Phase 3: Audio & Processing | ✅ Complete |
| Phase 4: LLM Integration | ✅ Complete |
| Phase 5: RAG & Embeddings | ✅ Complete |
| Phase 6: Polish & Integration | ✅ Complete |
| Phase 7: Testing & Deployment | 🔄 In progress |

See [`DEVELOPMENT-STATUS.md`](./DEVELOPMENT-STATUS.md) for detailed status and
[`DEVELOPMENT-HISTORY.md`](./DEVELOPMENT-HISTORY.md) for consolidated phase reports.

---

## Roadmap / known follow-ups

- **End-to-end runtime validation** of the recently reworked pipeline on Windows.
- **Encrypt API keys at rest** (Windows DPAPI / `ProtectedData`).
- **Regenerate EF Core migrations** to replace the `SchemaUpgrader` stopgap with a proper
  migration history.
- **Whisper model options** (larger models for accuracy; language selection UI).
- **MSIX packaging and Microsoft Store submission** (Phase 7).

---

## Troubleshooting

### Build Errors

**`error MSB4062` / PRI generation fails under `dotnet build`**
- Expected: the WinUI 3 PRI task does not load under the `dotnet` CLI. Build the app with
  **Visual Studio (F5)** or the Visual Studio **`msbuild.exe`** instead.

**"Windows App SDK not found"**
- Install the **Windows App SDK** workload, or update Visual Studio to 17.8+.

**"SDK version not found" / wrong SDK selected**
- The build is pinned to **.NET 8** (`global.json`, `8.0.100`, `rollForward: major`). Install the
  .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0). .NET 9 works via roll-forward;
  .NET 10 is incompatible with the PRI build task.

### Runtime Errors

**"Database file not found"**
- The first run creates the database automatically. Check `%LOCALAPPDATA%\MemoryTimeline\` exists;
  see `error.log` there for startup diagnostics.

**"MediaCapture not available"**
- Check microphone permissions in Windows Settings.

**Transcription produces nothing / hangs on first use**
- The first transcription downloads `ggml-base.bin` (~140 MB) to
  `%LOCALAPPDATA%\MemoryTimeline\Models\`; ensure network access for that one-time download.
  Afterwards Whisper runs fully offline.

---

## Documentation

| Document | Description |
|----------|-------------|
| [`FEATURE-AUDIT.md`](./FEATURE-AUDIT.md) | Feature-by-feature audit & root-cause analysis |
| [`HARDENING-FOLLOWUPS.md`](./HARDENING-FOLLOWUPS.md) | Deferred hardening items |
| [`DEVELOPMENT-STATUS.md`](./DEVELOPMENT-STATUS.md) | Current development status and next steps |
| [`DEVELOPMENT-HISTORY.md`](./DEVELOPMENT-HISTORY.md) | Consolidated phase completion reports |
| [`TESTING.md`](./TESTING.md) | Testing guide and best practices |
| [`DEPLOYMENT.md`](./DEPLOYMENT.md) | Deployment and distribution guide |
| [`scripts/README.md`](./scripts/README.md) | Setup scripts documentation |
| [`../README.md`](../README.md) | Repository overview (root README) |

---

## Contributing

- Branch from the active development branch.
- Follow WinUI 3 design guidelines and use `CommunityToolkit.Mvvm`.
- Keep `DbContext` usage **per-operation via the factory**, and surface errors in the UI rather
  than swallowing them.
- Ensure the solution builds (Visual Studio / CI) and update docs as needed.

---

## License

MIT License — see the main repository `LICENSE` file.

---

**Last Updated:** 2026-07-10
**Status:** Active development (Windows Native is the primary product)
