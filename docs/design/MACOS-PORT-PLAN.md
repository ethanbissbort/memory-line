# macOS Port Plan

**Status:** groundwork landed; app is a walking skeleton
**Target:** a native **SwiftUI** macOS app under [`macos-native/`](../../macos-native), sharing code with the iOS companion
**Last updated:** 2026-08-07

---

## 1. What we are building, and what we are not

Memory Line's primary product is the **Windows Native** app (.NET 10 / WinUI 3) under
`windows-native/`. This plan covers bringing the same product to macOS.

The macOS app is **not** a port of the iOS companion. The companion is a single-purpose
capture device: record a memory in the car, sync it, done. The Mac is a full peer of the
Windows app — timeline, review, people, ask — plus capture.

**Decision: native SwiftUI, not a shared .NET UI.** The alternatives were Avalonia or
.NET MAUI (Mac Catalyst), both of which would have reused `MemoryTimeline.Core` directly.
SwiftUI was chosen for platform fit and because the repository already carries a working
Swift codebase — domain models, sync networking, SQLite persistence, Keychain — that the
Mac app inherits on day one. The cost is that the C# service layer does not come with it;
§3.3 covers what that means in practice.

---

## 2. What already exists

| Piece | Where | State |
|---|---|---|
| Shared Swift core | `ios-companion/MemoryLineCompanion/Shared/` | 11 of 13 files compile on macOS unchanged |
| Sync service | `services/MemoryTimeline.SyncApi` | `net10.0`, runs on macOS/Linux |
| Wire contracts | `shared-contracts/` | OpenAPI + JSON Schema + .NET DTOs, platform-neutral |
| Business logic | `windows-native/src/MemoryTimeline.Core`, `.Data`, `.Sync` | retargeted to plain `net10.0` (see §3.3) |
| macOS app | `macos-native/` | skeleton: pairing + capture library |

### 2.1 The shared Swift layer, file by file

Audited by imports. Everything in `Foundation` / `os` / `SQLite3` / `Security` /
`CryptoKit` is available on macOS unchanged.

| File | Imports | macOS |
|---|---|---|
| `Domain/Interfaces.swift` | Foundation | ✅ shared |
| `Domain/Models.swift` | Foundation | ✅ shared |
| `Networking/DTOs.swift` | Foundation | ✅ shared |
| `Networking/SyncAPIClient.swift` | Foundation, os | ✅ shared |
| `Persistence/SQLiteCaptureStore.swift` | Foundation, os | ✅ shared |
| `Persistence/SQLiteDatabase.swift` | Foundation, SQLite3, os | ✅ shared |
| `Security/KeychainTokenStore.swift` | Foundation, Security, os | ⚠️ shared, see §3.2 |
| `Support/AppLog.swift` | os | ✅ shared |
| `Support/AudioStorage.swift` | Foundation | ✅ shared |
| `Support/CaptureStatusStore.swift` | Foundation | ✅ shared |
| `Support/FileHasher.swift` | CryptoKit, Foundation | ✅ shared |
| `Support/ConfirmationFeedback.swift` | AVFAudio, **UIKit** | ❌ iOS-only, excluded |
| `Support/WidgetStatusPublisher.swift` | Foundation, **WidgetKit** | ❌ iOS widget bridge, excluded |

The two exclusions are declared in the macOS project's
`PBXFileSystemSynchronizedBuildFileExceptionSet`; everything else is compiled straight
into the Mac app target.

---

## 3. Decisions and their consequences

### 3.1 Code sharing: synchronized folder, not a Swift package

The Mac target references `../ios-companion/MemoryLineCompanion/Shared` as an Xcode
**synchronized folder group**, compiling those sources into the app target directly.

A local Swift package (`MemoryLineKit`) consumed by both apps would be the tidier end
state. It was **deliberately deferred**, for one concrete reason: every type in `Shared/`
is `internal`. Extracting a module means adding `public` to roughly 50 declarations —
protocols, structs, enums, and their members — across a codebase that is shipping and
that just received a large feature (Phase 3 capture status). That is a wide, risky diff
whose only immediate benefit is tidiness.

The synchronized folder gets real sharing today with **zero changes to the iOS app**.

Revisit the package extraction when: a third consumer appears, or the two apps start
needing different behaviour from the same file (at which point `#if os(macOS)` in shared
code is the smell that says "extract now").

**Known wrinkle:** a synchronized folder whose path escapes the project directory is
unusual. If Xcode does not resolve it on first open, the group appears empty or red — fix
by deleting the group and dragging `ios-companion/MemoryLineCompanion/Shared` back in with
"create folder reference". Nothing else in the repository depends on this working.

### 3.2 Keychain: the Mac needs its own item and a modern keychain — **done**

`KeychainTokenStore` hardcoded the service name `ca.fluxology.memoryline.ios.tokens` and
passed no `kSecUseDataProtectionKeychain`. Both are fixed:

1. **Service name now derives from `Bundle.main.bundleIdentifier`.** On iOS that evaluates
   to `ca.fluxology.memoryline.ios.tokens` — byte for byte the literal it replaced — so
   existing installs keep reading their stored tokens and **no migration is needed**. The
   Mac gets `ca.fluxology.memoryline.mac.tokens` and therefore its own credentials, which
   is correct: each device pairs independently and holds its own device-bound token pair.
2. **`kSecUseDataProtectionKeychain` is set on macOS only.** Without it, `SecItem*` calls
   land in the legacy file-based keychain where `kSecAttrAccessible` is not honoured the
   way it is on iOS. It is `#if os(macOS)`-guarded rather than unconditional: the key is
   documented as ignored on iOS, but this store holds the credentials a paired phone needs
   and a lookup that silently stopped matching would unpair every existing install — not
   worth the risk for a flag that does nothing there. It requires the
   `keychain-access-groups` entitlement, already declared in
   `macos-native/Config/MemoryLineMac.entitlements`.

### 3.3 The C# business layer does not come to macOS — but it stayed portable anyway

`MemoryTimeline.Core`, `.Data` and `.Sync` were decoupled from WinUI and retargeted from
`net10.0-windows10.0.26100.0` to plain `net10.0`. That work is **done and in the tree**.

The SwiftUI decision means the Mac app does not consume those assemblies. The retarget was
still worth doing, and stays worth maintaining, for three reasons:

- It enforces the layering rule the project already had on paper — Core must not know
  about brushes, `Visibility`, or `Windows.Storage`.
- It keeps a headless option open: the extraction/RAG/narrative services can run on macOS
  or Linux (a sync-side worker, a batch tool) without a Windows machine.
- It makes Core testable off Windows.

The rule is written up in [`claude.md`](../../claude.md) under "Keeping Core portable".

**The real consequence for macOS:** every service in `MemoryTimeline.Core` — extraction,
RAG, ask, narrative, resurfacing, recall prompts, export/import, timeline math — has no
Swift equivalent. §5 is mostly about that.

---

## 4. Platform capabilities the Mac needs

Ordered by how much they block everything else.

### 4.1 Capture (recording)

iOS uses `AudioRecorderService` with an `AVAudioSession` lifecycle that **does not exist
on macOS**. The Mac needs its own recorder on `AVAudioEngine` / `AVAudioRecorder`, plus:

- Microphone permission via `NSMicrophoneUsageDescription` (declared) and the
  `com.apple.security.device.audio-input` entitlement (declared).
- Device selection — Macs routinely have several inputs; the iPhone does not. This is new
  UI with no iOS counterpart.
- No background-audio equivalent: on macOS the app is simply running or not.

### 4.2 Upload and status sync — pull side **done**

iOS `UploadCoordinator` and `StatusSyncCoordinator` are built on `BGTaskScheduler`, which
macOS does not have. `MacSyncCoordinator` is the Mac equivalent for the **pull** side: a
foreground `Task` loop on a 120s interval plus an explicit *Sync Now*, since the app is
either running or not. The `SyncAPIClient` calls underneath are shared unchanged.

It is a reimplementation, not a shared type, for two reasons: the iOS coordinator depends
on `BackgroundTasks`, and it reconciles remote status against capture records the *phone*
created, which the Mac does not have yet. The parts that are a contract with the server
rather than a local choice are mirrored exactly and covered by tests — page until
`hasMore`, persist the cursor *before* acking, hold the cursor when applying fails so the
page replays, never ack a cursor that did not advance, and last-write-wins on the
Windows-authored `updatedAtUtc` when a page is redelivered.

**Known gap:** the Mac applies `capture_status` changes but ignores `capture` and
`capture_artifact` ones, so the Library shows status for captures it does not yet hold.
Materialising capture rows from the feed (or recording its own) is Phase 2.

### 4.3 Storage locations

Shared `AppDatabase.defaultURL()` uses `.applicationSupportDirectory`, which is correct on
both platforms. Under the App Sandbox this resolves inside the container; that is intended.

Note the asymmetry with the Windows app, which stores everything under
`%LOCALAPPDATA%\MemoryTimeline\`. Nothing needs to reconcile these — the sync service is
the meeting point, not a shared filesystem.

### 4.4 Windows-only integrations with no macOS analogue

Toast notifications map to `UNUserNotificationCenter`. JumpList and Windows Timeline
activity publishing have no equivalent and should be dropped rather than approximated;
the Mac equivalents worth having instead are Spotlight indexing (`CoreSpotlight`) and a
menu-bar item for quick capture.

---

## 5. Phasing

Each phase is shippable and leaves the app in a usable state.

| Phase | Scope | Notes |
|---|---|---|
| **0 — Skeleton** ✅ | Xcode project, composition root, pairing, capture library | in the tree |
| **1 — Keychain + sync pull** ✅ | §3.2 fixed; `MacSyncCoordinator` pulls/applies/acks the change feed on a foreground timer | in the tree |
| **1b — Upload** | Drain pending uploads | nothing to upload until Phase 2 gives the Mac a recorder; deferred deliberately |
| **2 — Capture** | macOS recorder, input device selection, menu-bar quick capture | §4.1 |
| **3 — Timeline** | The big one: timeline math, zoom levels, spans, swimlanes, uncertainty bands | see §6 |
| **4 — Review & people** | Pending-event review, approve/reject, contact book | needs extraction (§6) |
| **5 — Ask & narrative** | Retrieval + LLM answers with citations | needs embeddings on-device or via service |
| **6 — Packaging** | Notarisation, Sparkle or App Store, hardened runtime | entitlements already set |

### The decision that gates phases 3–5

Everything from Phase 3 on needs the logic that currently lives in C# — extraction
prompts, date-precision inference, hybrid retrieval, narrative grounding, timeline
coordinate math. There are three ways to get it, and **this has not been decided**:

1. **Reimplement in Swift.** Full offline parity, no dependencies. Costs a second
   implementation of a large body of subtle logic, and the two will drift.
2. **Move the logic behind the sync service.** The Mac calls the service; the service runs
   the existing .NET code (already `net10.0`, already runs on macOS/Linux). One
   implementation, but the app stops being local-first and needs the service running.
3. **Embed .NET as a library** via a native AOT-compiled shared library the Swift app
   links. One implementation, stays local-first, but the build and debugging story is
   genuinely unpleasant.

Recommendation: **(1) for timeline math** — it is self-contained, well-tested on the C#
side, and small enough that duplication is cheap — and **defer 2-vs-3 for the AI features**
until Phase 4 makes the cost concrete. Do not decide it now; do not let Phase 1–2 work
assume an answer.

---

## 6. Verification

`.github/workflows/macos-native-build.yml` runs on `macos-latest`:

- **`xcode`** — `xcodebuild build` + `test` for the `MemoryLineMac` scheme.
- **`dotnet`** — `dotnet build` of `MemoryTimeline.Data`, `.Core`, `.Sync` and
  `MemoryTimeline.SyncContracts`.

The `dotnet` job is the guard on §3.3. Those four projects are *declared* platform-neutral;
building them on a Mac is the only automated check that the declaration is true, because
the Windows workflow stays green whether or not a Windows-only type creeps back in. Both
jobs gate — a non-gating check here would repeat exactly the failure the 2026-07 audit
called out (finding #10, the .NET test suite that ran with `continue-on-error`).

> **This job has not run yet.** It is the first execution of `MemoryTimeline.Core` and
> `.Sync` on a non-Windows machine. `Microsoft.ML.OnnxRuntime` and `Anthropic.SDK` are
> expected to restore on macOS, but that is reasoning, not evidence. If the job fails on a
> native-asset restore rather than on a compile error, the fix belongs in the package
> reference, not in a retreat from the `net10.0` target.

---

### 3.4 The Mac registers as `other`, because the contract has no `macos`

`DeviceRegisterRequest.platform` is constrained to `[ios, windows, other]` by
`shared-contracts/openapi/memory-line-sync-v1.yaml`, and the server enforces it —
`DeviceService.AllowedPlatforms` rejects anything else with a 400. So the Mac app sends
`"other"` today.

Two smaller traps in the same call, both already handled in `MacAppEnvironment.pair`:

- The Swift DTO **defaults `platform` to `"ios"`**. Using the memberwise initializer
  without passing it registers a Mac as an iPhone, silently and permanently — the value is
  stored on the device row.
- `displayName` on iOS is `UIDevice.current.name`; the Mac uses the host name with the
  Bonjour `.local` suffix trimmed. This is what the user sees in Windows Settings → Sync,
  so a blank or `studio.local`-shaped name is a real papercut.

Making `macos` first-class means changing the OpenAPI enum, `AllowedPlatforms`, and any UI
that renders a platform label — worth doing before the Mac ships, not worth doing now.

---

## 7. Open questions

- **Add `macos` to the device-platform enum?** See §3.4. Touches the OpenAPI contract, the
  server's `AllowedPlatforms`, and the Swift DTO's default. Until then the Mac is `other`.
- **Distribution:** Mac App Store (sandbox already assumed) or Developer ID + Sparkle?
  Affects whether the sandbox exceptions in §4 stay viable.
- **Does the Mac record at all,** or is it review-only with the iPhone as the capture
  device? Phase 2 is a large chunk of work that a review-only Mac would not need.
- **Minimum macOS version.** Currently 14.0 (Sonoma), chosen to match the iOS 17 baseline
  and because `ContentUnavailableView` and the `@Observable` macro need it.
- **Shared UI vocabulary with Windows.** The Windows app has a settled visual language for
  precision-honest dates, era colours, and category glyphs. The Mac should agree with it;
  nobody has written that down as a cross-platform spec.
