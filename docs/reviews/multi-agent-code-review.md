# Multi-Agent Code Review — Memory Timeline

**Date:** 2026-07-02
**Method:** Three specialized review agents combed through the codebase in parallel — one for the UI/renderer layer, one for the item-retrieval logic (IPC handlers + services), and one for the database layer (service + schema). This report consolidates their findings.

**Scope reviewed:**
- **UI:** `src/renderer/**` — 20 JSX/store/util files + 4 stylesheets
- **Retrieval:** `src/main/main.js`, `src/main/preload.js`, all 10 files in `src/main/services/`
- **Database:** `src/database/database.js`, `src/database/schemas/schema.sql`, plus DB usage in `main.js`

No files were modified during review.

---

## The headline finding: a schema-contract fracture runs through all three layers

The single most important result is that **the codebase contains two mutually incompatible schema conventions**, and the fault line shows up independently in all three reviews:

- The **real schema** (`schema.sql`) and the "correct" code paths use suffixed keys: `event_id`, `tag_id`, `tag_name`, `era_id`, `color_code`, `raw_transcript`, `events_fts`, `cross_references(event_id_1, event_id_2)`, `app_settings(setting_key, setting_value)`.
- A second set of files was written against a **different, ORM-style schema that does not exist**: `id`, `name`, `color`, `transcript`, `event_fts`, `cross_references(source_event_id, target_event_id)`, `app_settings(key, value)`, `locations.latitude/longitude`, `recording_queue.filename/file_path/stt_engine`.

This one root cause produces guaranteed runtime failures in the backend (`searchService`, `visualizationService`, `batchImportService` throw `no such column`), and mirrored data-shape bugs in the frontend (`SearchPanel` reads `event.id`, `AnalyticsDashboard` reads `era.color`). Any fix effort should start here, because it explains a large fraction of the individual issues below.

---

## Severity roll-up

| Layer | Critical | High | Medium | Low |
|---|---|---|---|---|
| UI / Renderer | 2 | 6 | 10 | 12 |
| Item Retrieval | 4 | 4 | 7 | 7 |
| Database | 4 | 5 | 8 | 8 |

---

## 1. UI / Renderer Layer

### Critical
- **C1 — Timeline positioning math produces `NaN`.** `utils/timelineUtils.js` `calculateDatePosition` (16-24) calls `getDateRangeForZoom` (74-77), which returns **ISO strings**, not `Date` objects. `differenceInDays` on strings yields `NaN`, so every `left` position becomes `"NaNpx"`. Breaks `EventBubble`, `EraBackground`, and axis markers — core timeline layout does not render.
- **C2 — Entire feature panels are never mounted.** `App.jsx` (74-88) renders only `timeline`, `recorder`, `queue`, `settings`. `SearchPanel`, `AnalyticsDashboard`, `BatchImportDialog`, `CrossReferencePanel`, `RAGSettings` are imported nowhere and have no nav entry in `Header`/`Sidebar`. Large amounts of built functionality are unreachable.

### High
- **H1 — Event culling ignores pan offset.** `EventBubble` (60) and `EraBackground` (21) cull on raw `position`, but panning is a `translateX(panOffset)` on the parent (`Timeline` 198/211/232). Dragging reveals nothing / hides the wrong bubbles.
- **H2 — Inconsistent API response contract.** Some callers assume `{success, data}` (`timelineStore`, `Sidebar`, `QueuePanel`, `RAGSettings`, `SettingsPanel`); others assume raw values (`BatchImportDialog` eras, `SearchPanel` results/suggestions, `AnalyticsDashboard`). For `eras.getAll()` both cannot be right.
- **H3 — Inconsistent field names** (the frontend face of the schema fracture): `event_id` vs `event.id`; `era_id`/`color_code` vs `era.id`/`era.color`; tags/people as **strings** in `EventDetailsModal` vs **objects** in `SearchPanel`.
- **H4 — QueuePanel review audio never resolves.** Audio URLs keyed by `queue_id` (37) but looked up by `pending_id` (317/319) in the review tab.
- **H5 — AudioRecorder lifecycle bugs.** Cancel resurrects the recording via `onstop` (85-94 → 32-38); Blob mislabeled `audio/wav` (default is WebM/Opus); object-URL leaks on re-record paths; no unmount cleanup (mic stays hot); `reader.onerror` leaves the button stuck on "Submitting…"; 30-min limit never enforced.
- **H6 — SettingsPanel STT state never syncs.** Mount-only `useEffect` (deps `[]`, 18-56) reads settings before they load asynchronously; `selectedEngine`/`engineConfig` never update.

### Medium
- **M1** — `store.error`/`store.isLoading` set but never rendered; IPC failures are silent.
- **M2** — Blocking `alert`/`confirm`/`prompt` used pervasively for all feedback/confirmation.
- **M3** — Modals lack `role="dialog"`, `aria-modal`, focus trap/return, Escape-to-close, `aria-label` on close.
- **M4** — Interactive `<div>`s (EventBubble, SearchPanel items, Timeline pan surface) with no `role`/`tabIndex`/keyboard support.
- **M5** — Global CSS class collisions across "phases" (`.timeline-info`, `.event-bubble`, `.settings-section`, etc.); component CSS leaks generic globals (`.empty-state`, `.loading`, `.tab`).
- **M6** — `error-boundary.css` never imported; error screen renders unstyled.
- **M7** — Dark theme setting is a no-op (no `data-theme` toggle, no dark rules).
- **M8** — Whole-store Zustand subscriptions + per-`mousemove` `panOffset` state re-render the entire timeline; needs selectors, memoization, rAF.
- **M8b** — `EventDetailsModal` sends the whole joined object (incl. `era_name`, `tags`) back through `updateEvent`; never re-syncs on prop change.
- **M9** — Redundant initial event load: `App.jsx` loads current-year range, then `Timeline` immediately overrides it.

### Low
No prop validation anywhere (L1); undefined CSS vars `--font-size-xs`/`--error-color` (L2); deprecated `onKeyPress` (L3); `exhaustive-deps` gaps (L4); chart divide-by-zero for single data point (L5); SVG charts lack accessible text (L6); dead/placeholder controls — SearchPanel click, RAG "Add Tag", RAGSettings stats, Sidebar "+ New Event" (L7); TimelineControls always formats `MMMM yyyy` (L8); BatchImportDialog interval leak (L9); keyboard shortcuts don't exempt contenteditable/select (L10); non-null-safe `parseISO`/`format` in render (L11); StrictMode double-fire compounds AudioRecorder side effects (L12).

### Positive
No `dangerouslySetInnerHTML` anywhere — XSS surface is low. `ErrorBoundary` is a solid class boundary. `Timeline` correctly cleans up its `ResizeObserver` and keyboard listener.

---

## 2. Item Retrieval Logic

### Critical
- **C1 — `searchService.js` is entirely non-functional.** Every method references non-existent columns/tables (`e.id`, `t.name`, `event_fts`, `cross_references.source_event_id`, `app_settings.key`, `e.transcript`). All `search:*` IPC handlers throw. Faceted search, suggestions, and saved searches are 100% broken.
- **C2 — `visualizationService.js` — most analytics queries throw**, and `main.js` handlers (2085-2210) **swallow the errors**, so the UI silently shows empty charts. Broken: `getTagCloud`, `getPeopleNetwork`, `getLocationHeatmap` (locations has no lat/long), `getEraStatistics`, `getSummaryStatistics`, `compareTimePeriods`, `getTagRelationshipMatrix`.
- **C3 — `batchImportService.js` queue insert uses wrong schema.** `_addToQueue` (205-217) inserts `filename/file_path/stt_engine/auto_process` — none exist on `recording_queue`. Every batch-imported file throws and is marked failed. `batchTagImportedEvents` is likewise broken.
- **C4 — `performanceService.getEventsPaginated` count query silently broken.** The `query.replace(...)` (72-74) never matches the multi-line query string, so `total` is `undefined` → `totalPages = NaN`. Pagination metadata is corrupt.

### High
- **H1 — SQL injection via `sortOrder`.** `searchService` line 168 interpolates `sortOrder` with only `.toUpperCase()`, no allowlist. (Currently masked by C1.)
- **H2 — API keys exposed.** `settings:getAll` (652-666) returns `anthropic_api_key`/`embedding_api_key` in cleartext to the renderer; keys stored plaintext despite "encrypted in production" comment.
- **H3 — Unguarded `JSON.parse`** in `events:getRange` (232), `getById` (288), `pending:getAll` (891), `pending:approve` (928), `ragService.getCrossReferences` (229) — malformed JSON fails the whole request.
- **H4 — Raw user input to FTS5 `MATCH`** (`events:search`, 504). Parameterized (no injection) but unbalanced quotes / leading `*` throw `fts5: syntax error`.

### Medium
- **M1** — `GROUP_CONCAT` + `split(',')` corrupts names containing commas ("Cambridge, MA").
- **M2** — Cartesian product across three M2M joins in `events:getRange`/`getById` before `GROUP BY`.
- **M3** — Path traversal: `audio:getFile` (1086-1110) reads arbitrary local files; `audio:save`/`queue:remove` operate on unvalidated paths.
- **M4** — `performanceService` cache has no invalidation on create/update/delete (5-min stale reads).
- **M5** — `ragService.storeCrossReference` inserts duplicate rows (fresh UUID PK, no UNIQUE on the pair).
- **M6** — No IPC input validation; `limit`/`pageSize` uncapped (negative limit disables the limit in SQLite).
- **M7** — N+1 in `searchService._loadEventRelationships` (3 queries per event) vs the batched pattern in `performanceService`.

### Low
`getTimelineDensity` quarter bug uses today's date (L1); `getActivityHeatmap` always hour 0 since dates have no time (L2); numeric SQL interpolation of `clusterDays`/`limit` (L3); `getSuggestions` LIKE wildcards unescaped (L4); `processAllPending` marks queue `completed` prematurely (L5); `embeddingService.findSimilarEvents` full in-memory O(n) scan + divide-by-zero (L6); update handlers leak raw SQLite error text (L7).

### Positive
`main.js`, `exportService`, `ragService`, `embeddingService` match the real schema. All values parameterized; dynamic column lists whitelisted (only `sortOrder` is a genuine injection vector). Transactions used correctly for multi-write ops. `performanceService._batchLoadRelationships` is the pattern the other paths should adopt.

---

## 3. Database Layer

### Critical
- **C1 — `backup()` uses the wrong library API.** `database.js` (103-118) calls `backup.step(-1)/finish()` (node-`sqlite3` API), but the project uses **better-sqlite3**, where `db.backup()` returns a Promise. `db:backup` throws every time — a data-loss-class defect.
- **C2 — FTS5 external-content index desyncs on `VACUUM`.** `events` has `event_id TEXT PRIMARY KEY` (implicit rowid); FTS is keyed on rowid. `VACUUM` (run by `vacuum()` and `performance:optimize`) renumbers rowids, breaking the FTS↔events join with no rebuild path.
- **C3 — FTS5 update trigger corrupts the index.** `events_fts_update` (schema 46-52) uses a plain `UPDATE`, which cannot remove old tokens from an external-content FTS5 table; must use the `'delete'`/insert command form. Editing an event leaves stale terms matchable.
- **C4 — `processAllPending` passes `undefined` audio path.** `main.js` (1298-1319) selects only `queue_id`, then reads `item.audio_file_path` (undefined). Every item fails.

### High
- **H1 — No real migration mechanism.** `runMigrations()` just re-runs `schema.sql` with `IF NOT EXISTS`; can never `ALTER`/add a column/backfill. `schema_version` is written but never read.
- **H2 — Migrations not wrapped in a transaction** (`db.exec(schema)`, ~30 statements non-atomic).
- **H3 — Secrets plaintext + leaked to renderer** (mirrors retrieval H2). Use Electron `safeStorage`.
- **H4 — `event_tags` insert can violate PK and abort the transaction** when the tags array has a duplicate or case-variant (`tag_name COLLATE NOCASE`). Use `INSERT OR IGNORE`.
- **H5 — Raw user input to FTS `MATCH`** (mirrors retrieval H4).

### Medium
- **M1** — `cross_references` lacks `UNIQUE(event_id_1, event_id_2, relationship_type)`; RAG re-runs duplicate rows.
- **M2** — Multi-step LLM DB mutations in `processQueueItem`/`processAllPending` are not transactional.
- **M3** — `GROUP_CONCAT` fan-out + comma-split fragility.
- **M4** — `updated_at` trigger relies on `recursive_triggers` being OFF (fragile).
- **M5** — Missing resilience pragmas (`busy_timeout`, explicit `synchronous`, `wal_autocheckpoint`).
- **M6** — `close()` only on `before-quit`; no forced WAL checkpoint on shutdown.
- **M7** — `getStats()` DB size ignores `-wal`/`-shm` files.
- **M8** — Embeddings stored as JSON text; similarity search is O(n) in-process with no vector index.

### Low
Over-indexing low-cardinality enums (L1); no date-format/ordering `CHECK` (L2); no `color_code` format constraint (L3); default FTS tokenizer, no `porter` stemming (L4); `category` enum redundantly includes `'era'` (L5); redundant `content_rowid=rowid` (L6); per-startup `INSERT OR IGNORE` churn (L7); `audio:save` assumes a data-URL prefix (L8).

### Positive
Parameterized queries throughout; dynamic `UPDATE` builders whitelist columns (no injection). `foreign_keys = ON` per connection; sensible `ON DELETE CASCADE/SET NULL`. Core mutations use `db.transaction()`. WAL enabled; composite PKs on junction tables; `COLLATE NOCASE` dedups names.

---

## Consolidated remediation priority

1. **Fix the schema fracture first** (retrieval C1–C3, UI H3): rewrite `searchService`, `visualizationService`, `batchImportService`, `SearchPanel`, `AnalyticsDashboard` against the real `schema.sql` column names. This is the largest single win.
2. **Fix the FTS5 + backup + migration foundations** (DB C1–C3, H1–H2): correct the better-sqlite3 `backup()` call, redesign the events rowid/FTS strategy, correct the update trigger, add a post-VACUUM rebuild, and build a real versioned migration runner.
3. **Make the timeline render and pan** (UI C1, H1): fix `calculateDatePosition` string-vs-Date and include `panOffset` in culling.
4. **Wire up or remove the orphaned panels** (UI C2) and unify the IPC response contract (UI H2).
5. **Security hardening**: encrypt/withhold API keys (retrieval H2 / DB H3), validate `sortOrder` (retrieval H1), sanitize FTS input (retrieval H4 / DB H5), constrain file paths (retrieval M3).
6. **Robustness**: `undefined` audio path (retrieval C4, DB C4), pagination count (retrieval C4), unguarded `JSON.parse` (retrieval H3), transactional LLM mutations (DB M2), duplicate cross-refs (retrieval M5 / DB M1).

---

*Generated by a group of specialized review agents (UI, retrieval logic, database) run in parallel. Findings citing better-sqlite3 API and FTS5 external-content semantics should be confirmed against the installed library version before patching, though both behaviors are well-established.*
