# claude.md — AI-Assistant Development Guide (Memory Timeline)

This is the working guide for an AI assistant or new contributor making changes in
this repo. Read it before editing. For the product-level overview, see the root
[`README.md`](./README.md) and [`windows-native/README.md`](./windows-native/README.md).

## TL;DR

- **Primary product = the Windows Native app** under [`windows-native/`](./windows-native)
  (.NET 8 / WinUI 3, clean architecture). **Do all new work here.**
- The **Electron app** under [`src/`](./src) is **legacy / maintenance only** — it still
  exists and builds, but new features and fixes should target Windows Native unless the
  task is explicitly about Electron. See [Legacy Electron](#legacy-electron).
- **You cannot build this repo in the cloud sandbox** — there is no .NET SDK, and the
  WinUI 3 app cannot be built with `dotnet build` anyway (see [Build & run reality](#build--run-reality)).
  Changes are validated by **CI on a Windows runner**, not by a local build. Reason
  carefully and keep changes correct-by-construction.

---

## Windows Native — the app you work on

### Solution layout (clean architecture)

The solution is `windows-native/src/MemoryTimeline.sln`, four projects:

| Project | Role |
|---------|------|
| **MemoryTimeline** | WinUI 3 app — Views (XAML), ViewModels, Controls, Converters, and platform services (audio, STT, notifications, jump list, navigation, theme). DI is wired in `App.xaml.cs`. |
| **MemoryTimeline.Core** | Business logic — services (events, timeline, queue, extraction, RAG, ask/query, narrative, resurfacing, recall prompts, media, backup/revisions, export/import, settings, search, analytics), DTOs, timeline math, `SettingKeys`. No UI, no EF Core internals leaking out. |
| **MemoryTimeline.Data** | Data access — `AppDbContext` (EF Core 8 + SQLite), entity models, repositories, and `SchemaUpgrader`. |
| **MemoryTimeline.Tests** | xUnit unit, integration, and performance tests. |

Dependency direction: `MemoryTimeline` → `Core` → `Data`. Keep it that way; don't make
`Core` depend on WinUI types or `Data` depend on `Core`.

### Data access — the single most important rule

**Every DB operation opens its own short-lived `AppDbContext` from the factory.** There
is no shared or app-lifetime context.

```csharp
await using var ctx = await _contextFactory.CreateDbContextAsync();
// ... one logical operation, then the context is disposed ...
```

- DI registers `services.AddDbContextFactory<AppDbContext>()` (in `App.xaml.cs`).
- Repositories take `IDbContextFactory<AppDbContext>` and create a context per method
  (see `MemoryTimeline.Data/Repositories/EventRepository.cs` for the canonical pattern).
- **Do NOT** reintroduce `services.AddDbContext<AppDbContext>()` (scoped), a cached/shared
  `AppDbContext` field, or a single context passed around. A desktop app has no request
  scope; a shared context across features/threads was the **root cause of prior
  concurrency bugs** ("A second operation was started on this context…", search errors
  after adding an event). Don't recreate it.
- Because repositories/services are **stateless over the factory**, they are registered
  **Singleton**. Follow that when adding a new repository or service.
- Startup uses `SchemaUpgrader.EnsureSchemaAsync` (EnsureCreated + idempotent DDL
  repairs for schema drift on older DBs) — a deliberate stopgap until real EF migrations
  are regenerated. Don't swap it for raw `EnsureCreated`.

### MVVM & UI conventions

- **CommunityToolkit.Mvvm** throughout: `[ObservableProperty]` for bindable state,
  `[RelayCommand]` for commands, `x:Bind` compiled bindings in XAML.
- **Cross-feature updates go through `WeakReferenceMessenger`**, not direct VM-to-VM
  references. E.g. creating/approving an event publishes a message
  (`EventCreatedMessage`) that the timeline subscribes to and refreshes on. Add new
  cross-feature signals the same way rather than coupling view models.
- **Marshal background work back to the UI thread** via the `DispatcherQueue` before
  touching bound state / `ObservableCollection`s.
- **Surface errors in the UI** — `InfoBar`, status text, in-dialog validation — and log.
  **Never swallow an exception into logs only.** A silent catch after a dialog closes is
  exactly the class of bug the audit fixed.
- Naming: Views `PascalCasePage.xaml`; ViewModels `XxxViewModel`; services as interface +
  impl (`IEventService` / `EventService`); commands generated from `[RelayCommand]`.

### Settings

- All setting keys live in **`MemoryTimeline.Core/Services/SettingKeys.cs`** — a
  constants class (snake_case values, e.g. `llm_provider`, `embedding_api_key`, `theme`;
  the Anthropic key is stored under `ApiKey` to preserve existing user rows).
  **Use these constants, never string literals** — a writer/reader key mismatch is what
  used to make settings "revert".
- Settings persist to the `app_settings` table and apply **without a restart**. The LLM
  client re-reads its configuration live, so a changed provider/model/key takes effect on
  the next call.

### External integrations

| Capability | Implementation | Notes |
|------------|----------------|-------|
| **Speech-to-text** | **Local Whisper** via `WhisperSpeechToTextService` (Whisper.net, ggml `base` model) | File-based, fully offline after a one-time ~140 MB model download to `%LOCALAPPDATA%\MemoryTimeline\Models\`. The recorded WAV is transcribed on-device — **not** the old Windows `SpeechRecognizer`, which is mic-only and can't transcribe a file. |
| **LLM** (extraction, Ask, narratives, recall wording) | **Anthropic Claude** (`AnthropicLlmService`) **or** any **OpenAI-compatible endpoint** (`OpenAiCompatibleLlmService` — Ollama, LM Studio, vLLM) | `RoutingLlmService` is the registered `ILlmService`; it re-reads `llm_provider` on **every call**, so switching providers applies live. `ILlmUsageTracker` counts per-session calls/tokens for the Settings UI. |
| **Embeddings (RAG / Ask)** | **Local ONNX** `all-MiniLM-L6-v2` (`OnnxEmbeddingService`, 384-dim, CPU execution provider) **or** **OpenAI** (`OpenAIEmbeddingService`, `AddHttpClient`) | `RoutingEmbeddingService` routes on `embedding_provider` (seeded default: `local`), so Connections/Ask semantic retrieval works with **no API key**. A dimension guard (`EmbeddingDimensionMismatchException`) blocks mixing 384/1536-dim vectors; Settings offers a re-embed flow. Local model (~90 MB + vocab) downloads once to `%LOCALAPPDATA%\MemoryTimeline\Models\all-MiniLM-L6-v2\`. |
| **Geocoding** | Optional **Nominatim** via `NominatimGeocodingService` (`AddHttpClient`) | **Opt-in and OFF by default** (`geocoding_enabled`). When off, location coordinates come only from photo EXIF or manual pin drops — no place name leaves the machine. |

### The memory pipeline

`Record (Windows MediaCapture) → recording_queue → Transcribe (local Whisper) → Extract
(LLM → pending_events) → Review/approve → Timeline`. **Text sources join audio in the
same queue**: pasted/typed text (Ctrl+Shift+V on the Queue page) becomes a
`recording_queue` row with `source_type = Text` and the text stored in its `transcript`
column, skipping transcription; Whisper output is likewise persisted to `transcript` and
reused across retries instead of re-transcribing. Each hop persists state so a failure is
recoverable and visible in the UI. Extraction assigns a **date precision**
(`DatePrecision`: Exact/Day/Month/Season/Year/Decade/Unknown) rather than inventing exact
days, and resolves people **alias-aware** (case-insensitive name → alias lookup). Approve
is an **atomic** transaction writing the event plus its tags/people/locations (and
precision/uncertainty fields) together.

---

## Build & run reality

Critical for an assistant — get this wrong and you'll suggest broken commands:

- **x64 only.** The solution defines x64/x86/ARM64 — **no AnyCPU**. Always pass
  `-p:Platform=x64` (or select x64 in Visual Studio).
- **Build with Visual Studio 2022 (17.8+) or `msbuild.exe`, NOT `dotnet build` / `dotnet run`.**
  WinUI 3 PRI resource generation (`MrtCore.PriGen` → `ExpandPriContent`) uses a .NET
  Framework MSBuild task that loads under VS's `msbuild.exe` but **fails under the
  `dotnet` CLI build engine** (error **MSB4062**). So `dotnet build` / `dotnet run`
  cannot build or launch the app.
- **SDK pin.** `windows-native/src/global.json` pins the **.NET 8** SDK
  (`8.0.100`, `rollForward: "major"`). The `major` roll-forward lets a newer major (e.g.
  .NET 9) drive the C# compile if that's all that's installed; .NET 10 is incompatible
  with the WindowsAppSDK PRI task.
- **CI is the validation gate.** `.github/workflows/windows-native-build.yml` builds
  **Release | x64** on **windows-latest** via VS `msbuild` (`/t:Restore,Build`) and runs
  tests best-effort. Compile success is the gate.
- **No .NET SDK exists in the cloud dev sandbox.** You cannot build or run locally here —
  rely on CI to validate. Make changes that are correct by inspection; don't assume you
  can "just run it".

For local dev on a real Windows machine: open the solution in VS 2022 and press F5, or
`msbuild MemoryTimeline.sln /t:Restore,Build /p:Configuration=Debug /p:Platform=x64`.

---

## Adding a feature in Windows Native

1. Entity in `MemoryTimeline.Data/Models/` (+ `AppDbContext` / `SchemaUpgrader` if the
   schema changes).
2. Repository in `MemoryTimeline.Data/Repositories/` using the `IDbContextFactory`
   per-operation pattern.
3. Service interface in `MemoryTimeline.Core/Services/` + implementation.
4. Register the repository/service **Singleton** in `App.xaml.cs`.
5. ViewModel (CommunityToolkit.Mvvm) — `[ObservableProperty]` / `[RelayCommand]`; publish
   or subscribe to messenger events for cross-feature effects.
6. XAML View with `x:Bind`; marshal background updates via `DispatcherQueue`; surface
   errors in an `InfoBar`/status.
7. New settings → add a constant to `SettingKeys`.
8. Tests in `MemoryTimeline.Tests`.

---

## Data model (shared conceptual schema)

Core: `events` (with `date_precision` + `earliest_possible`/`latest_possible`
uncertainty window and `last_viewed_at`/`view_count` view tracking), `eras`,
`era_categories`, `era_tags`, `milestones`, `tags`, `people` (contact fields plus a
`merged_into_id` merge tombstone), `person_aliases`, `locations` (optional
`latitude`/`longitude`, `place_type`, `canonical_name`, `geocoded_at`).
Junctions: `event_tags`, `event_people`, `event_locations`.
Processing: `recording_queue` (with `source_type`, `source_label`, persisted
`transcript`), `pending_events` (mirrors the events precision columns).
Attachments & history: `event_media` (managed media copies, EXIF, thumbnails, content
hash), `event_revisions` (append-only edit history), `recall_prompts` (guided-recall
questions, deduped forever).
RAG: `event_embeddings` (with per-row `embedding_dimension`), `cross_references`.
UX: `drafts`, `saved_searches`.
System: `app_settings` — 33 seeded keys; **seed parity is three-way** (the
`AppDbContext.SeedDefaultSettings` HasData seed, the `SchemaUpgrader` INSERT OR IGNORE
backfill, and the `SettingKeys` constants must stay in sync — keep all three when adding
a seeded setting).
Identity: the unique name indexes on `people`/`tags`/`locations` are **COLLATE NOCASE**;
`SchemaUpgrader` defensively merges pre-existing case-variant duplicates (backfilling the
keeper's empty contact columns) before rebuilding each index.
The Electron build uses a compatible SQLite schema (plus `events_fts` FTS5,
Electron-only). DB file: `%LOCALAPPDATA%\MemoryTimeline\memory-timeline.db`.

---

## Security & privacy notes

- **Local-first**: all data in local SQLite; audio transcribed on-device (Whisper);
  embeddings default to the **local ONNX** provider, so Connections/Ask retrieval needs no
  cloud. Only the text you choose to process is sent to the configured LLM/embedding
  provider (which can itself be a local OpenAI-compatible endpoint).
- **Geocoding is opt-in and off by default**; when enabled, only the location's name is
  sent to Nominatim. Media/EXIF processing is entirely local.
- **API keys today live in the `app_settings` table** (not Windows Credential Manager —
  that's stale). **Encrypting keys at rest with DPAPI (`ProtectedData`) is a tracked
  follow-up**, not yet implemented. Don't describe key storage as encrypted; don't log keys.
- Use parameterized EF Core queries (default with LINQ) — no string-built SQL.

---

## Key docs (read these for detail)

| Doc | What's in it |
|-----|--------------|
| [`windows-native/README.md`](./windows-native/README.md) | Windows Native overview, setup, tech stack. |
| [`windows-native/FEATURE-AUDIT.md`](./windows-native/FEATURE-AUDIT.md) | Feature-by-feature audit + root-cause analysis of the bugs the rework fixed. |
| [`windows-native/HARDENING-FOLLOWUPS.md`](./windows-native/HARDENING-FOLLOWUPS.md) | Deferred hardening items (DPAPI, EF migrations, etc.). |
| [`windows-native/DEVELOPMENT-STATUS.md`](./windows-native/DEVELOPMENT-STATUS.md) | Phase-level status and next steps. |
| Root [`README.md`](./README.md) | Product overview + "Recent engineering work" summary. |

---

## Legacy Electron

The original cross-platform build (React + Electron + SQLite, `better-sqlite3`, FTS5)
lives under [`src/`](./src). It is **feature-complete for its own scope but no longer the
focus** — treat it as maintenance. Only touch it for an explicitly Electron-scoped task.

- Structure: `src/main/` (main process, IPC handlers, `main.js`/`preload.js`),
  `src/renderer/` (React components, Zustand stores, CSS), `src/database/`
  (`database.js`, `schemas/schema.sql`).
- Run: `npm install`, then `npm run dev` (development) or `npm run package` (production).
- Conventions: components PascalCase `.jsx`, utilities camelCase, one CSS file per
  component, handlers prefixed `handle`. Renderer never uses `require()` directly — it
  goes through the `preload.js` bridge; DB access is centralized in `database.js` and
  exposed over IPC.
- Adding a feature: `schema.sql` → `database.js` → IPC handler in `main.js` → expose in
  `preload.js` → React component → Zustand store.
- Packaging: see [`DEPLOYMENT-INSTALL.md`](./DEPLOYMENT-INSTALL.md); review history under
  [`docs/reviews/`](./docs/reviews).

---

**Last Updated:** 2026-08-06
**Primary target:** Windows Native (.NET 8 / WinUI 3) — `windows-native/`
**Legacy:** Electron — `src/`
