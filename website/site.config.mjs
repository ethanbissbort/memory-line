/**
 * Site configuration for the Memory Timeline documentation website.
 *
 * Everything the generator needs to know lives here: which markdown files become
 * pages, how they are grouped in the sidebar, and the structured content used to
 * build the hand-authored landing page.
 *
 * To add a new document: drop an entry into the relevant section's `pages` array.
 * `source` is repo-relative; `slug` becomes `<slug>.html`.
 */

export const site = {
  name: 'Memory Timeline',
  tagline: 'Capture memories by voice or text, let AI structure them, and explore your life as an interactive timeline.',
  description:
    'Documentation for Memory Timeline — a local-first Windows desktop app that turns spoken and typed memories into a structured, searchable, interactive personal timeline.',
  repo: 'ethanbissbort/memory-line',
  repoUrl: 'https://github.com/ethanbissbort/memory-line',
  branch: 'main',
};

/**
 * Sidebar sections. Order here is the order on the site.
 */
export const sections = [
  {
    id: 'start',
    title: 'Start here',
    blurb: 'What the project is, and how to find your way around.',
    pages: [
      {
        slug: 'index',
        title: 'Home',
        kind: 'landing',
        description: 'Project overview, the memory pipeline, features, and a guided map of the docs.',
        hideFromCards: true,
      },
      {
        slug: 'documentation-map',
        title: 'All documentation',
        kind: 'map',
        description:
          'Every document in the repository, with size, reading time, and a preview of its contents.',
        hideFromCards: true,
      },
      {
        slug: 'overview',
        source: 'README.md',
        title: 'Project overview',
        shortTitle: 'Project overview',
        description:
          'The repository README: what Memory Timeline is, the memory pipeline, the full feature list, tech stack, architecture, setup, configuration, and privacy model.',
        audience: 'Everyone',
        tags: ['overview', 'features', 'getting started'],
      },
      {
        slug: 'developer-guide',
        source: 'claude.md',
        title: 'Developer orientation guide',
        shortTitle: 'Developer guide',
        description:
          'The condensed contributor briefing: solution layout, the per-operation DbContext rule, MVVM conventions, settings keys, build reality, and how to add a feature.',
        audience: 'Contributors',
        tags: ['conventions', 'architecture', 'how-to'],
      },
    ],
  },
  {
    id: 'windows-native',
    title: 'Windows Native app',
    blurb: 'The primary, actively developed product: .NET 8 + WinUI 3.',
    pages: [
      {
        slug: 'windows-native',
        source: 'windows-native/README.md',
        title: 'Windows Native app',
        description:
          'The main reference for the WinUI 3 app: pipeline, features, quick start, project structure, clean-architecture layers, DI composition, database schema, configuration, and troubleshooting.',
        audience: 'Developers',
        tags: ['WinUI 3', '.NET 8', 'architecture', 'setup'],
        featured: true,
      },
      {
        slug: 'setup-scripts-windows',
        source: 'windows-native/scripts/README.md',
        title: 'PowerShell setup scripts',
        description:
          'Setup-Dependencies.ps1 and Verify-Installation.ps1 — automated toolchain install and environment verification, plus the build reality (msbuild, not dotnet build).',
        audience: 'Developers',
        tags: ['PowerShell', 'setup', 'CI'],
      },
      {
        slug: 'testing',
        source: 'windows-native/TESTING.md',
        title: 'Testing guide',
        description:
          'Test structure, the DbContext factory pattern for test authors, running and filtering tests, coverage goals, performance benchmarks, templates, and CI behaviour.',
        audience: 'Developers',
        tags: ['xUnit', 'testing', 'CI'],
      },
      {
        slug: 'deployment',
        source: 'windows-native/DEPLOYMENT.md',
        title: 'Deployment guide',
        description:
          'Building Release|x64, runtime data layout, MSIX packaging, code signing, Microsoft Store submission, and side-loading.',
        audience: 'Maintainers',
        tags: ['MSIX', 'packaging', 'release'],
      },
    ],
  },
  {
    id: 'status',
    title: 'Status & history',
    blurb: 'Where the project stands, and how it got here.',
    pages: [
      {
        slug: 'development-status',
        source: 'windows-native/DEVELOPMENT-STATUS.md',
        title: 'Development status',
        description:
          'Phase-by-phase status, the 2026-08 F1–F12 feature wave with per-feature detail and deferred items, known limitations, and success criteria.',
        audience: 'Everyone',
        tags: ['status', 'roadmap', 'F1–F12'],
        featured: true,
      },
      {
        slug: 'development-history',
        source: 'windows-native/DEVELOPMENT-HISTORY.md',
        title: 'Development history',
        description:
          'Consolidated phase completion reports (Phase 0 through Phase 6) with deliverables, statistics, and architecture achievements.',
        audience: 'Everyone',
        tags: ['history', 'phases'],
      },
      {
        slug: 'migration-to-native-windows',
        source: 'MIGRATION-TO-NATIVE-WIN.md',
        title: 'Migration to native Windows',
        description:
          'The full migration plan from Electron to .NET 8 + WinUI 3: rationale, stack decision, branch strategy, architecture comparison, all seven phases, feature-parity matrix, and checklists.',
        audience: 'Developers',
        tags: ['migration', 'planning', 'architecture'],
      },
    ],
  },
  {
    id: 'quality',
    title: 'Audits & reviews',
    blurb: 'Multi-agent audits, root-cause analyses, and deferred hardening.',
    pages: [
      {
        slug: 'feature-audit',
        source: 'windows-native/FEATURE-AUDIT.md',
        title: 'Feature audit (2026-07)',
        description:
          'Root causes of three reported bugs, the shared-DbContext foundation defect underneath them, a per-feature audit, and the dependency-ordered master fix plan.',
        audience: 'Developers',
        tags: ['audit', 'root cause', 'bugs'],
        featured: true,
      },
      {
        slug: 'hardening-followups',
        source: 'windows-native/HARDENING-FOLLOWUPS.md',
        title: 'Hardening follow-ups',
        description:
          'Deferred hardening items ranked high / medium / low — captive DbContext scopes, fire-and-forget embeddings, and migration snapshot drift.',
        audience: 'Developers',
        tags: ['follow-ups', 'tech debt'],
      },
      {
        slug: 'repo-audit-2026-07',
        source: 'docs/reviews/exhaustive-audit-2026-07.md',
        title: 'Exhaustive repo audit',
        description:
          'A bug hunt and feature crawl across the whole repository: 27 adversarially verified findings, 61 unverified medium/low findings, and ranked improvement opportunities.',
        audience: 'Developers',
        tags: ['audit', 'bug hunt', 'findings'],
      },
      {
        slug: 'multi-agent-code-review',
        source: 'docs/reviews/multi-agent-code-review.md',
        title: 'Multi-agent code review',
        description:
          'A layered review of the Electron build — UI/renderer, retrieval logic, and database — headlined by a schema-contract fracture running through all three layers.',
        audience: 'Developers',
        tags: ['code review', 'Electron', 'schema'],
      },
    ],
  },
  {
    id: 'design',
    title: 'Design notes',
    blurb: 'Cross-cutting contracts written before implementation.',
    pages: [
      {
        slug: 'people-feature-contracts',
        source: 'docs/design/people-feature-contracts.md',
        title: 'People feature contracts',
        description:
          'The cross-agent interface contract for the People hub: file ownership map, entity columns, DTOs, service signatures, extraction changes, UI conventions, and DI registrations.',
        audience: 'Developers',
        tags: ['design', 'contracts', 'people'],
      },
    ],
  },
  {
    id: 'electron',
    title: 'Legacy Electron build',
    blurb: 'The earlier cross-platform app, now in maintenance.',
    pages: [
      {
        slug: 'electron-deployment',
        source: 'DEPLOYMENT-INSTALL.md',
        title: 'Electron deployment & install',
        description:
          'Development setup, production builds, platform installers, code signing, distribution, updates, and troubleshooting for the Electron app.',
        audience: 'Maintainers',
        tags: ['Electron', 'packaging', 'install'],
      },
      {
        slug: 'electron-setup-scripts',
        source: 'scripts/README.md',
        title: 'Electron setup scripts',
        description:
          'setup-windows.ps1 and setup-ubuntu.sh — cross-platform dependency installation, common issues, manual steps, and verification.',
        audience: 'Developers',
        tags: ['Electron', 'setup', 'scripts'],
      },
      {
        slug: 'electron-setup-logs',
        source: 'scripts/logs/README.md',
        title: 'Setup script logs',
        description:
          'What the setup scripts log, the log format and levels, how to use logs for troubleshooting, and the privacy note about sharing them.',
        audience: 'Developers',
        tags: ['Electron', 'logs', 'troubleshooting'],
      },
      {
        slug: 'electron-tests',
        source: 'tests/README.md',
        title: 'Electron test suite',
        description:
          'Jest test layout, generating test databases and sample data, running suites, coverage, performance benchmarks, and templates.',
        audience: 'Developers',
        tags: ['Electron', 'Jest', 'testing'],
      },
      {
        slug: 'electron-test-databases',
        source: 'tests/fixtures/databases/README.md',
        title: 'Test databases',
        description: 'The generated SQLite fixtures used by the Electron test suite.',
        audience: 'Developers',
        tags: ['Electron', 'fixtures'],
      },
    ],
  },
];

/**
 * Structured content for the generated landing page.
 * Facts here are drawn from README.md and windows-native/README.md.
 */
export const landing = {
  status: [
    { label: 'Windows Native', tone: 'primary' },
    { label: '.NET 8 · WinUI 3' },
    { label: 'Local-first' },
    { label: 'Active development' },
  ],

  intro: [
    'Memory Timeline is a **local-first** desktop app for recording, organizing, and rediscovering your personal history. Speak a memory — or paste and type one — and the app transcribes it on-device with Whisper, has an LLM extract a structured event, and lets you review it onto an interactive timeline.',
    'Your data lives in a local SQLite database. Embeddings run locally by default, so nothing leaves your machine except the specific text you choose to send to the providers you configure — and geocoding is off unless you opt in.',
  ],

  // `key` values are filled in by the build from the real corpus.
  stats: [
    { key: 'docs', value: '—', label: 'documents', hint: 'READMEs, guides, audits and design notes' },
    { key: 'words', value: '—', label: 'words', hint: 'all rendered and searchable here' },
    { value: '12', label: 'features shipped', hint: 'the F1–F12 wave, August 2026' },
    { value: '4', label: 'projects', hint: 'App · Core · Data · Tests' },
  ],

  pipeline: {
    title: 'The memory pipeline',
    lead:
      'The heart of the app is a single flow, from voice to timeline. Every hop persists its state, so a failure at any stage is recoverable and visible in the UI rather than silently swallowed.',
    steps: [
      {
        icon: '🎙',
        title: 'Record',
        detail: 'Windows MediaCapture writes a 16 kHz mono WAV.',
        sub: 'Pause · resume · cancel',
      },
      {
        icon: '⏱',
        title: 'Queue',
        detail: 'recording_queue holds the item (pending → done).',
        sub: 'Audio and text share one queue',
      },
      {
        icon: '📝',
        title: 'Transcribe',
        detail: 'Whisper transcribes on-device, fully offline.',
        sub: 'Transcripts persist across retries',
      },
      {
        icon: '🤖',
        title: 'Extract',
        detail: 'An LLM produces a structured event into pending_events.',
        sub: 'Dates with honest precision',
      },
      {
        icon: '👀',
        title: 'Review',
        detail: 'Edit, approve, or reject each extracted event.',
        sub: 'People flagged new / known / update',
      },
      {
        icon: '✅',
        title: 'Approve',
        detail: 'Event + tags + people + locations in one transaction.',
        sub: 'Atomic write',
      },
      {
        icon: '🗓',
        title: 'Timeline',
        detail: 'The event appears and connections update.',
        sub: 'Auto-refresh via the message bus',
      },
    ],
    note:
      'Text joins audio in the same queue: paste or type a memory (Ctrl+Shift+V) and it enters as a text source whose content is stored as its transcript, skipping transcription entirely.',
  },

  featureGroups: [
    {
      icon: '🏠',
      title: 'Home & rediscovery',
      items: [
        '**On This Day** anniversary cards that only surface memories with a real day anchor',
        '**Guided recall prompts** mined from gaps in the archive — never asked twice',
        'An optional **daily toast**, JumpList entries and Windows Timeline deep links',
      ],
    },
    {
      icon: '🗓',
      title: 'Timeline',
      items: [
        'Year / Month / Week / Day zoom with smooth pan and keyboard navigation',
        '**Duration spans**, collapsible **swimlanes**, and **uncertainty bands** for vague dates',
        '**Honest date precision** — "Summer 2003", never an invented day',
        'Coalesced scroll reloads, so fast panning no longer piles up queries',
      ],
    },
    {
      icon: '🎙',
      title: 'Capture → events',
      items: [
        'Recording with pause / resume / cancel via Windows `MediaCapture`',
        'Text & paste capture (Ctrl+Shift+V) through the same queue',
        'Local **Whisper** speech-to-text — offline after a one-time model download',
        '**Media attachments** with EXIF, thumbnails, content-hash dedupe and a lightbox',
      ],
    },
    {
      icon: '💬',
      title: 'Ask & narrate',
      items: [
        '**Ask your timeline** — hybrid keyword + semantic retrieval with citations to real events',
        'Honest refusal when the archive has nothing relevant',
        '**Narrative stories** from a range, era, or person, exported as Markdown or HTML',
      ],
    },
    {
      icon: '🔍',
      title: 'Discover',
      items: [
        'Faceted **search** across events, tags, people, locations and eras',
        '**Connections** powered by local ONNX embeddings — no API key required',
        'A fully **offline map** with EXIF GPS backfill and manual pin drops',
        '**Analytics**: category distribution, density, tag cloud, people network',
      ],
    },
    {
      icon: '🗂',
      title: 'Organize & manage',
      items: [
        '**People hub** with aliases, case-insensitive identity and merge tombstones',
        '**Eras**, **drafts**, and append-only **revision history**',
        '`.mtbak` **backup & restore** with preview, confirmation and scheduling',
        'JSON / CSV / Markdown **export**, JSON import with duplicate handling',
      ],
    },
  ],

  architecture: {
    title: 'Architecture',
    lead: 'Clean, layered separation across four projects, with a message bus between features.',
    layers: [
      {
        name: 'MemoryTimeline',
        role: 'Presentation — WinUI 3 + XAML',
        detail: 'Views, ViewModels, Controls, Converters, and platform services (audio, STT, notifications, JumpList, navigation, theme).',
      },
      {
        name: 'MemoryTimeline.Core',
        role: 'Business logic',
        detail: 'Services for events, timeline, queue, extraction, RAG, ask/query, narrative, resurfacing, recall prompts, media, backup/revisions, export/import and settings — plus DTOs and timeline math.',
      },
      {
        name: 'MemoryTimeline.Data',
        role: 'Data access',
        detail: 'EF Core 8 DbContext over SQLite in WAL mode, entity models, repositories, and the SchemaUpgrader that repairs schema drift at startup.',
      },
      {
        name: 'MemoryTimeline.Tests',
        role: 'Verification',
        detail: 'xUnit unit, integration and performance tests.',
      },
    ],
    decisions: [
      {
        title: 'Per-operation `DbContext` via `IDbContextFactory`',
        body: 'A desktop app has no request scope, so every repository and service opens a short-lived context per operation. This replaced a single app-lifetime context — the root cause of intermittent "second operation on this context" failures.',
      },
      {
        title: '`SchemaUpgrader` instead of raw `EnsureCreated`',
        body: 'Startup creates the database from the current model and idempotently repairs drift (missing tables and columns) on pre-existing databases — a stopgap until EF migrations are regenerated.',
      },
      {
        title: 'MVVM with a message bus',
        body: 'Cross-feature updates ("event created" → refresh the timeline) flow through `WeakReferenceMessenger` rather than tight coupling between view models.',
      },
      {
        title: 'Errors are surfaced, not swallowed',
        body: 'Failures propagate to visible `InfoBar` / status affordances instead of disappearing into logs.',
      },
    ],
  },

  stack: [
    ['UI', 'WinUI 3 + XAML, `x:Bind` compiled bindings'],
    ['MVVM', 'CommunityToolkit.Mvvm — `ObservableObject`, `[RelayCommand]`, `WeakReferenceMessenger`'],
    ['Runtime', '.NET 8 (`net8.0-windows10.0.22621.0`)'],
    ['Data', 'SQLite via EF Core 8, WAL mode, `IDbContextFactory` per-operation contexts'],
    ['Audio', '`Windows.Media.Capture` and `Windows.Media.Playback`'],
    ['Speech-to-text', 'Whisper.net (ggml `base` model, local and offline)'],
    ['LLM', 'Anthropic Claude, or any OpenAI-compatible endpoint (Ollama, LM Studio, vLLM), routed per call'],
    ['Embeddings', 'Local ONNX `all-MiniLM-L6-v2` (384-dim, the default) or OpenAI embeddings'],
    ['Media metadata', 'MetadataExtractor (EXIF taken-date and GPS) + Windows thumbnails'],
    ['Geocoding', 'Optional Nominatim — opt-in, off by default'],
  ],

  paths: [
    {
      icon: '🧭',
      title: 'New here',
      body: 'Start with what the app does and why it is built the way it is.',
      links: ['overview', 'windows-native', 'documentation-map'],
    },
    {
      icon: '🛠',
      title: 'Building & running',
      body: 'Get a working toolchain, build Release|x64, and understand the build reality.',
      links: ['developer-guide', 'setup-scripts-windows', 'deployment'],
    },
    {
      icon: '🧪',
      title: 'Testing & shipping',
      body: 'Test structure, CI behaviour, packaging and Store submission.',
      links: ['testing', 'deployment', 'development-status'],
    },
    {
      icon: '🔬',
      title: 'Quality & history',
      body: 'What broke, why, and what is still outstanding.',
      links: ['feature-audit', 'repo-audit-2026-07', 'hardening-followups'],
    },
    {
      icon: '📐',
      title: 'Design & planning',
      body: 'The migration plan and the pre-implementation contracts.',
      links: ['migration-to-native-windows', 'people-feature-contracts'],
    },
    {
      icon: '📦',
      title: 'Legacy Electron',
      body: 'The earlier cross-platform build, kept for reference.',
      links: ['electron-deployment', 'electron-tests', 'multi-agent-code-review'],
    },
  ],

  timeline: [
    {
      when: 'Phases 0–6',
      title: 'Native rebuild',
      body: 'Core infrastructure, timeline visualization, audio and processing, LLM integration, RAG and embeddings, then polish and Windows integration — all complete.',
      state: 'done',
      link: 'development-history',
    },
    {
      when: '2026-07',
      title: 'Multi-agent audit & fix pass',
      body: 'Five agents traced every feature end-to-end. Fixed the add-event bug, the search error, the DbContext concurrency underneath both, and made the voice pipeline real with local Whisper.',
      state: 'done',
      link: 'feature-audit',
    },
    {
      when: '2026-08',
      title: 'Feature wave F1–F12',
      body: 'Honest dates, media attachments, text capture, ask-your-timeline, narratives, guided recall, On This Day, spans and swimlanes, the people hub, the offline map, pluggable AI, and backup/restore/revisions.',
      state: 'done',
      link: 'development-status',
    },
    {
      when: 'Phase 7',
      title: 'Testing & deployment',
      body: 'End-to-end runtime validation on real hardware, MSIX packaging, and Microsoft Store submission.',
      state: 'active',
      link: 'deployment',
    },
  ],

  roadmap: [
    'End-to-end **runtime validation** of the pipeline and the F1–F12 wave on a real Windows machine',
    '**Encrypt API keys at rest** with Windows DPAPI (`ProtectedData`)',
    '**Regenerate EF Core migrations** to replace the `SchemaUpgrader` stopgap',
    'Deferred F1–F12 items — photo-import wizard, map tile basemaps, LLM token streaming, PDF narrative export',
    '**Whisper model options** — larger models for accuracy, and a language selection UI',
    '**MSIX packaging** and Microsoft Store submission',
  ],

  privacy: [
    ['Local-first', 'All data is stored on your device in SQLite; media is copied into a local managed folder.'],
    ['On-device transcription', 'Whisper runs locally — recorded audio is never uploaded.'],
    ['Local embeddings by default', 'Connections and Ask retrieval can run entirely on-device with no API key.'],
    ['Selective cloud calls', 'Only the transcript text you process reaches your configured LLM provider — which can itself be local.'],
    ['Geocoding is opt-in', 'Off by default; coordinates otherwise come only from photo EXIF or manual pin drops.'],
    ['Known gap', 'API keys are currently stored in the local settings database; encrypting them at rest is a tracked follow-up.'],
  ],
};
