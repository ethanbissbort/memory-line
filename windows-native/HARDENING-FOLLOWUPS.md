# Windows-Native Hardening — Follow-ups

This file captures the **deferred** items from the Phase-7 multi-agent hardening
pass. The applied fixes are in the commit that adds this file; the items below
were intentionally **not** auto-fixed because they require a compiler to verify a
non-trivial refactor, a public-API/DI change spanning layers, or a team decision.

> Context: the pass was performed statically (no .NET SDK in the environment).
> **Before release, run `dotnet build` on `src/MemoryTimeline.sln` and execute the
> test suite** to validate all applied fixes.

## High priority

### 1. Captive / scope-captured `DbContext`  *(converged flag from 3 agents)*
`TimelineViewModel` is registered `AddSingleton` (App.xaml.cs) but depends on
`ITimelineService`/`IEventService` (Scoped) → `AppDbContext` (Scoped). Pages also
resolve their VMs from the **root** provider (`App.Current.Services.GetRequiredService<…>`),
not from a DI scope. Net effect:
- Production: a single `AppDbContext` lives for the whole app lifetime — not
  thread-safe, and the change-tracker grows unbounded.
- Development (host scope validation): throws *"Cannot consume scoped service
  from singleton."*

**Fix (coordinated, needs compile):** register the context via
`AddDbContextFactory<AppDbContext>()` and create a per-operation context in
services, **or** resolve VMs/pages inside an explicit `IServiceScope`. Touches
App DI + every repository/service ctor, so it was not done blind.

### 2. `EventService.CreateEventAsync` fire-and-forget embedding  *(Core + Tests agents)*
`_ = Task.Run(() => GenerateEmbeddingForEventAsync(createdEvent))` reuses the
request-scoped `AppDbContext` on a background thread → `ObjectDisposedException` /
"second operation on this context" once a real `IEmbeddingService` is registered.
Currently masked by an internal catch (embeddings silently fail).
**Fix:** inject `IServiceScopeFactory`, create a fresh scope+context for the
background work (ctor change → flagged rather than applied).

### 3. Migration + snapshot drift  *(Data agent — Critical for migration-based release)*
`AppDbContextModelSnapshot` is an empty stub and `InitialCreate` is far behind the
model (missing tables `era_categories`, `era_tags`, `milestones`, `saved_searches`;
missing columns on `eras`/`events`/`tags`; missing `cross_references` FKs; missing
seed `InsertData`). Runtime is fine today because startup uses `EnsureCreatedAsync()`
(schema built from the model), but **any migration-based deploy will fail**.
**Fix:** on a machine with the SDK, regenerate the baseline
(`dotnet ef migrations add InitialCreate --force`) before shipping migrations.

## Medium priority

- **Unit-of-work / atomicity** — multi-repository workflows (e.g. approve pending →
  create Event + delete PendingEvent) each call `SaveChangesAsync` independently and
  are not atomic. Introduce an explicit transaction / unit-of-work at the service layer.
- **Polly referenced but unused** — add a bounded, transient-only retry with jitter
  (and a timeout) around the Anthropic and OpenAI network calls (`AddHttpClient` +
  SDK call sites).
- **`CrossReference` delete behavior = `Restrict`** — deleting an `Event` referenced
  by a RAG-generated cross-reference will throw once FK enforcement is on. `Cascade`
  is likely intended but has data-loss implications — needs sign-off.
- **Analytics color bindings** — `CategoryDistribution/TagCloudItem/ActivityHeatmapCell.Color`
  are `string` bound directly to `Brush` properties via `x:Bind` in `AnalyticsPage.xaml`.
  Expose a `Brush`/`SolidColorBrush` on the model (as the timeline DTOs do with
  `ColorBrush`) or add a string→Brush converter.

## Low priority

- **API keys stored plaintext** in `AppSettings` — encrypt at rest via DPAPI /
  `ProtectedData` at the settings/Data layer (keys are not logged or hardcoded today).
- **`CancellationToken` not plumbable** through `IRepository<T>` and specialized repo
  interfaces — add `CancellationToken ct = default` in a coordinated interface change.
- **`DateTime.Kind` round-trip** — SQLite returns `Unspecified`; add a UTC
  `ValueConverter` on `DateTime` properties to avoid local/UTC comparison bugs.
- **Inclusive viewport boundaries** — `EventLayoutEngine.GetVisibleLayouts` /
  `TimelineViewport.IsEventVisible` treat edge-touching (zero-area) events as visible
  on both ends. Internally consistent (tests aligned to it); confirm it's intended.
- **Singleton `AnthropicLlmService` caches `_model`** at first init — a later model
  change in settings needs an app restart.
