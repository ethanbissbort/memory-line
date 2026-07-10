# Memory Timeline

Capture your memories by voice, let AI turn them into structured events, and explore your life as an interactive timeline that surfaces the connections between moments.

**Primary implementation:** Windows Native (.NET 8 · WinUI 3)
**Status:** Active development — core pipeline recently rebuilt and hardened; builds green in CI, runtime validation ongoing.
**Also in the repo:** an earlier cross-platform Electron build, now in maintenance (see [Legacy Electron app](#legacy-electron-app)).

---

## What it is

Memory Timeline is a **local-first** desktop app for recording, organizing, and rediscovering your personal history:

1. **Speak** a memory into the recorder.
2. The app **transcribes** it locally with Whisper, then a **Claude** model **extracts structured events** — title, dates, description, category, and the people, places, and tags involved.
3. You **review and approve** the extracted events, which land on an **interactive timeline**.
4. Optional **embeddings** let the app surface **connections** between related memories, and **search + analytics** help you navigate everything you've captured.

Your data lives in a local SQLite database. Nothing leaves your machine except the specific text you choose to send to the LLM/embedding providers you configure.

> ### 🧭 Going forward, the **Windows Native** app is the primary and actively developed product.
> The rest of this README focuses on it. The Electron version remains in the tree for reference and is documented briefly at the end.

---

## The memory pipeline

The heart of the app is a single flow, from voice to timeline:

```
🎙  Record            →  ⏱  Queue           →  📝  Transcribe        →  🤖  Extract
 Windows MediaCapture     recording_queue       Whisper (local)          Claude → pending events
 (16 kHz mono WAV)        (pending → done)      offline, on-device       structured, reviewable
                                                                                  │
                                                                                  ▼
🗓  Timeline          ←  ✅  Approve          ←  👀  Review
 event appears,          atomic write:            edit / approve / reject
 connections update      event + tags + people    per extracted event
                         + locations, in one txn
```

Every hop persists its state, so a failure at any stage is recoverable and visible in the UI rather than silently swallowed.

---

## Features (Windows Native)

### Timeline
- Interactive canvas with **Year / Month / Week / Day** zoom levels, smooth pan, and keyboard navigation.
- Event bubbles with category icons; era backgrounds for life phases.
- Newly created or approved events refresh onto the timeline automatically (via an in-app messenger), and the view jumps to an event's date if it lands outside the current window.

### Voice → events
- **Recording** with pause/resume/cancel using Windows `MediaCapture`; recordings are written under `%LOCALAPPDATA%\MemoryTimeline\AudioRecordings`.
- **Local speech-to-text** via **Whisper** ([Whisper.net](https://github.com/sandrohanea/whisper.net)) — file-based, fully **offline** after a one-time model download. The recorded WAV is transcribed on-device; no audio is sent to the cloud.
- **LLM event extraction** with Anthropic **Claude**, producing structured events with tags, people, and locations, held in a **review queue** with approve / edit / reject.

### Discover
- **Search** — full-text and **faceted** search across events, tags, people, locations, and eras, with debounced suggestions and saved searches.
- **Connections (RAG)** — optional **OpenAI embeddings** power semantic similarity and heuristic cross-references between memories; degrades gracefully with a clear call-to-action when no embedding key is configured.
- **Analytics** — category distribution, timeline density, tag cloud, people network, and activity summaries, with real empty-states.

### Organize & manage
- **Eras** — define and color life periods that frame the timeline.
- **Export / Import** — JSON, CSV, and Markdown export; JSON import with duplicate handling and an optional pre-import database backup.
- **Settings** — API keys, LLM provider/model, default zoom, and theme, all persisted to the local database and applied without a restart.

### Windows 11 integration
- Toast **notifications** (e.g. "processing complete").
- **JumpList** quick actions and recent events.
- **Windows Timeline** activity publishing (Adaptive Cards).
- Light / Dark / System **theming**; touch and pen friendly.

---

## Tech stack

| Layer | Technology |
|-------|------------|
| UI | **WinUI 3** + XAML, `x:Bind` compiled bindings |
| MVVM | **CommunityToolkit.Mvvm** (`ObservableObject`, `[RelayCommand]`, `WeakReferenceMessenger`) |
| Runtime | **.NET 8** (`net8.0-windows10.0.22621.0`) |
| Data | **SQLite** via **EF Core 8**, WAL mode, `IDbContextFactory` per-operation contexts |
| Audio | `Windows.Media.Capture` (recording), `Windows.Media.Playback` |
| Speech-to-text | **Whisper.net** (ggml `base` model, local/offline) |
| LLM | **Anthropic Claude** (`Anthropic.SDK`) |
| Embeddings | **OpenAI** embeddings (optional, for Connections) |
| Resilience | `Polly` (referenced), structured `ILogger` logging |

---

## Architecture

Clean, layered separation across four projects:

```
MemoryTimeline            WinUI 3 app  — Views, ViewModels, Controls, Converters,
                                         platform services (audio, STT, notifications,
                                         jump list, navigation, theme)
MemoryTimeline.Core       Business logic — services (events, timeline, queue, extraction,
                                         RAG, export/import, settings), DTOs, timeline math
MemoryTimeline.Data       Data access  — EF Core DbContext, entity models, repositories,
                                         SchemaUpgrader
MemoryTimeline.Tests      xUnit unit, integration, and performance tests
```

Key architectural decisions (recently reworked — see [Recent engineering](#recent-engineering-work)):

- **Per-operation `DbContext` via `IDbContextFactory`.** A desktop app has no request scope, so every repository and service opens a short-lived context per operation (`await using var ctx = await factory.CreateDbContextAsync()`). This replaced a single app-lifetime context that was shared across all features and was the root cause of intermittent "second operation on this context" failures.
- **`SchemaUpgrader` instead of raw `EnsureCreated`.** On startup the app creates the database from the current model and idempotently repairs schema drift (missing tables/columns) on pre-existing databases — a stopgap for full EF migrations.
- **MVVM with a message bus.** Cross-feature updates (e.g. "event created" → refresh the timeline) flow through `WeakReferenceMessenger` rather than tight coupling between view models.
- **Errors are surfaced, not swallowed.** Failures propagate to visible `InfoBar`/status affordances instead of disappearing into logs.

---

## Getting started

### Prerequisites
- **Windows 11** (22H2 or later).
- **Visual Studio 2022** (17.8+) with the **.NET Desktop Development** and **Windows App SDK** workloads.
- A **.NET SDK**: the repo pins the build to the **.NET 8** SDK via `windows-native/src/global.json`. If you only have a newer major installed (e.g. .NET 9), the pin's `rollForward: "major"` lets the build use it — it just won't select the .NET 10 SDK, which is incompatible with the WindowsAppSDK PRI build task.

### Build & run
```powershell
git clone <repository-url>
cd memory-line/windows-native/src

# Open the solution in Visual Studio 2022 and press F5,
# or build from the command line for x64 (the solution has no AnyCPU config):
dotnet build MemoryTimeline.sln -c Debug -p:Platform=x64
```

> **Note on `dotnet build` vs. Visual Studio:** WinUI 3 PRI resource generation uses a .NET Framework MSBuild task that loads under **Visual Studio's `msbuild.exe`** but not under the `dotnet` CLI's build engine. Build from **Visual Studio** (or `msbuild`) for a full app build; the CI pipeline does the same on a Windows runner.

### First run
- The app creates its SQLite database at `%LOCALAPPDATA%\MemoryTimeline\memory-timeline.db`.
- The first transcription downloads the Whisper model (~140 MB `ggml-base.bin`) to `%LOCALAPPDATA%\MemoryTimeline\Models\` — one time, then fully offline.
- Add your API key(s) in **Settings** before processing the queue (see below).

---

## Configuration

### API keys
| Provider | Purpose | Required? |
|----------|---------|-----------|
| **Anthropic** ([console](https://console.anthropic.com/)) | Claude — event extraction | **Yes**, to process recordings into events |
| **OpenAI** ([keys](https://platform.openai.com/api-keys)) | Embeddings — Connections / similarity | Optional; Connections is disabled cleanly without it |

Keys and preferences are stored in the local `app_settings` table and can be changed at any time from **Settings** (no restart needed). Encrypting API keys at rest (Windows DPAPI) is a tracked hardening item — see [Roadmap](#roadmap--known-follow-ups).

### Data & model locations
```
%LOCALAPPDATA%\MemoryTimeline\
├── memory-timeline.db          SQLite database (WAL)
├── AudioRecordings\            recorded WAV files
├── Models\ggml-base.bin        Whisper model (downloaded on first use)
└── error.log                   startup/diagnostic log
```

---

## Recent engineering work

The Windows Native app recently went through a structured, multi-agent **feature audit and fix pass**. Highlights of what changed:

- **Fixed the core "add event" bug** — a category-casing mismatch was rejecting new events before they were saved, with the error swallowed after the dialog closed. Save now validates case-insensitively, surfaces errors in-dialog, and refreshes the timeline.
- **Fixed "search error after adding an event"** — the shared-`DbContext` concurrency described above, plus un-debounced per-keystroke autocomplete, were colliding. Resolved by the `IDbContextFactory` rework, debounced search, and honest error messages.
- **Made the voice pipeline real** — replaced a speech API that transcribed the *live microphone* (not the recorded file) with local file-based **Whisper**; fixed the unpackaged storage-path crash; persisted transcripts; made **approve** atomic and metadata-complete (tags/people/locations in one transaction).
- **Repaired RAG & settings** — embeddings now read the correct settings key and set provider/dimension; RAG queries use mapped columns and persist cross-references; the settings writer/reader key mismatch that made settings "revert" was unified behind a `SettingKeys` constants class.
- **Export/Import, notifications, navigation, and Windows integration** wiring corrected.

A **CI workflow** (`.github/workflows/windows-native-build.yml`) compiles the full solution and runs tests on a Windows runner for every push to the development branch.

Full details: [`windows-native/FEATURE-AUDIT.md`](./windows-native/FEATURE-AUDIT.md) (findings + root causes) and [`windows-native/HARDENING-FOLLOWUPS.md`](./windows-native/HARDENING-FOLLOWUPS.md) (deferred items).

---

## Roadmap / known follow-ups

- **Runtime validation** of the recently reworked pipeline end-to-end on Windows.
- **Encrypt API keys at rest** (Windows DPAPI / `ProtectedData`).
- **Regenerate EF Core migrations** to replace the `SchemaUpgrader` stopgap with a proper migration history.
- **Whisper model options** (larger models for accuracy; language selection UI).
- **Analytics export** and a few remaining UI polish items.
- MSIX packaging and Microsoft Store submission (Phase 7).

See [`windows-native/DEVELOPMENT-STATUS.md`](./windows-native/DEVELOPMENT-STATUS.md) for phase-level status.

---

## Project structure

```
memory-line/
├── windows-native/                 ★ Primary: Windows Native app
│   ├── src/
│   │   ├── MemoryTimeline/          WinUI 3 app (Views, ViewModels, Controls, Services)
│   │   ├── MemoryTimeline.Core/     Business logic & services
│   │   ├── MemoryTimeline.Data/     EF Core context, models, repositories, SchemaUpgrader
│   │   ├── MemoryTimeline.Tests/    xUnit tests
│   │   ├── MemoryTimeline.sln
│   │   └── global.json              pins the .NET SDK for the build
│   ├── README.md                    Windows Native overview
│   ├── FEATURE-AUDIT.md             feature-by-feature audit & root causes
│   ├── HARDENING-FOLLOWUPS.md       deferred hardening items
│   ├── DEVELOPMENT-STATUS.md        phase status
│   ├── DEVELOPMENT-HISTORY.md       consolidated phase reports
│   ├── TESTING.md · DEPLOYMENT.md
│
├── src/                            Legacy: Electron app (React + Electron + SQLite)
├── docs/reviews/                   multi-agent code-review reports
├── .github/workflows/              CI (Windows Native build + test)
└── README.md                       this file
```

---

## Testing

```powershell
cd windows-native/src
dotnet test MemoryTimeline.sln -c Debug -p:Platform=x64
```

Tests cover timeline math, services, repository/integration behavior, and performance. See [`windows-native/TESTING.md`](./windows-native/TESTING.md).

---

## Privacy & security

- **Local-first** — all data is stored on your device in SQLite.
- **On-device transcription** — Whisper runs locally; recorded audio is never uploaded.
- **Selective cloud calls** — only the transcript text you process is sent to your configured LLM provider, and only the event text you embed is sent to your embedding provider.
- **You own your recordings** — original audio is never automatically deleted.
- **Note:** API keys are currently stored in the local settings database; encrypting them at rest is a tracked follow-up.

---

## Legacy Electron app

An earlier cross-platform build (React + Electron + SQLite) lives under [`src/`](./src) and is feature-complete for its own scope, but it is **no longer the focus** of development. It shares the same conceptual model and a compatible SQLite schema.

```bash
npm install
npm run dev        # development
npm run package    # production build
```

See [`docs/reviews/`](./docs/reviews) for its code-review history and [`DEPLOYMENT-INSTALL.md`](./DEPLOYMENT-INSTALL.md) for packaging.

---

## Documentation

| Document | Description |
|----------|-------------|
| [`windows-native/README.md`](./windows-native/README.md) | Windows Native overview & setup |
| [`windows-native/FEATURE-AUDIT.md`](./windows-native/FEATURE-AUDIT.md) | Feature-by-feature audit & root-cause analysis |
| [`windows-native/HARDENING-FOLLOWUPS.md`](./windows-native/HARDENING-FOLLOWUPS.md) | Deferred hardening items |
| [`windows-native/DEVELOPMENT-STATUS.md`](./windows-native/DEVELOPMENT-STATUS.md) | Phase-level development status |
| [`windows-native/DEVELOPMENT-HISTORY.md`](./windows-native/DEVELOPMENT-HISTORY.md) | Consolidated phase reports |
| [`windows-native/TESTING.md`](./windows-native/TESTING.md) | Testing guide |
| [`windows-native/DEPLOYMENT.md`](./windows-native/DEPLOYMENT.md) | Packaging & deployment |
| [`claude.md`](./claude.md) | AI-assistant development guide |

---

## Contributing

1. Branch from the active development branch.
2. Make focused changes with tests where practical.
3. Ensure the solution builds (Visual Studio / CI) and update docs as needed.
4. Open a pull request.

For Windows Native work: follow WinUI 3 guidelines, use CommunityToolkit.Mvvm, keep `DbContext` usage per-operation via the factory, and surface errors in the UI rather than swallowing them.

---

## License

MIT License — see [`LICENSE`](./LICENSE).
