# Memory Line — macOS

A native **SwiftUI** macOS app. It pairs with the sync service, pulls the change feed on a
timer, applies Windows-authored capture status, and browses the local library. Everything
else is in flight — see [`docs/design/MACOS-PORT-PLAN.md`](../docs/design/MACOS-PORT-PLAN.md)
for the plan, the decisions behind it, and the open questions.

**Platform:** macOS 14 (Sonoma) or later
**Toolchain:** Xcode 16+ (the project uses `objectVersion = 77` synchronized folder groups)
**Status:** Phases 0–1 of 6. Builds and tests green in CI; not runtime-validated against a
live sync server.

---

## Opening it

```bash
open macos-native/MemoryLineMac.xcodeproj
```

Build and run the `MemoryLineMac` scheme. There is no `.xcworkspace` and no package
resolution step — the project has no external dependencies.

---

## How code is shared with the iOS companion

The Mac app does not have its own domain, networking, persistence, or Keychain layer. It
compiles the iOS companion's:

```
ios-companion/MemoryLineCompanion/Shared/
├── Domain/         CaptureRecord, lifecycle states, settings keys, protocol seams
├── Networking/     SyncAPIClient + the v1 wire DTOs
├── Persistence/    SQLiteDatabase, SQLiteCaptureStore, SQLiteSettingsStore
├── Security/       KeychainTokenStore
└── Support/        AppLog, AudioStorage, FileHasher, CaptureStatusStore
```

That directory is referenced from `MemoryLineMac.xcodeproj` as an Xcode **synchronized
folder group**, so its `.swift` files compile straight into the Mac app target. Adding a
file there picks it up automatically in both apps — there is no file list to update.

Two files in `Shared/Support/` are iOS-only and explicitly excluded from this target:

| File | Why |
|---|---|
| `ConfirmationFeedback.swift` | UIKit haptics |
| `WidgetStatusPublisher.swift` | WidgetKit bridge for the iOS widget extension |

The exclusions live in the project's `PBXFileSystemSynchronizedBuildFileExceptionSet`.

### Why a synchronized folder and not a Swift package

Every type in `Shared/` is `internal`. A package would mean adding `public` to roughly 50
declarations across a shipping iOS app for no immediate benefit. The synchronized folder
gets real sharing with zero changes to the iOS project. The trade and the conditions for
revisiting it are written up in the port plan, §3.1.

**If Xcode does not resolve the Shared group on first open** (its path escapes the project
directory, which is unusual), delete the group and drag
`ios-companion/MemoryLineCompanion/Shared` back in as a folder reference. Nothing else in
the repository depends on that reference working.

---

## Layout

```
macos-native/
├── MemoryLineMac.xcodeproj/
├── Config/
│   ├── MemoryLineMac-Info.plist       ATS, URL scheme, termination policy
│   └── MemoryLineMac.entitlements     sandbox, network client, mic, Keychain group
├── MemoryLineMac/
│   ├── App/
│   │   ├── MemoryLineMacApp.swift     @main; window + Settings scenes
│   │   ├── MacAppEnvironment.swift    composition root (mirrors the iOS AppEnvironment)
│   │   └── RootView.swift             sidebar navigation
│   └── Features/
│       ├── Library/LibraryView.swift       capture list + Windows-authored status
│       └── Sync/
│           ├── MacSyncCoordinator.swift    pull → apply → ack, on a foreground timer
│           └── SettingsScene.swift         pairing / unpairing / sync status
└── MemoryLineMacTests/
```

`MacAppEnvironment` deliberately omits the iOS app's recorder and upload coordinator:
those are built on `AVAudioSession` and `BGTaskScheduler`, neither of which exists on
macOS. Recording is Phase 2; there is nothing to upload until it lands.

`MacSyncCoordinator` replaces the iOS `StatusSyncCoordinator`. It is a reimplementation
rather than shared code — that one needs `BackgroundTasks` and reconciles against capture
records the phone created — but the pull/ack semantics are a server contract, so they are
mirrored exactly and pinned by tests.

---

## Relationship to the other apps

|  | Role |
|---|---|
| **Windows Native** (`windows-native/`) | The primary product. Owns extraction, review, and approval. |
| **iOS companion** (`ios-companion/`) | Capture device. Records, uploads, shows Windows-authored status. |
| **macOS** (this) | Intended as a full peer of the Windows app, not a companion. Phase 0. |

All three meet at the self-hosted sync service (`services/`), never at a shared filesystem.

---

## Testing

```bash
cd macos-native
xcodebuild test -project MemoryLineMac.xcodeproj -scheme MemoryLineMac -destination 'platform=macOS'
```

`MemoryLineMacTests` covers only what macOS adds or overrides — the shared stores and sync
client are already covered by `ios-companion/MemoryLineCompanionTests`, and running the
same tests against the same sources twice buys nothing.

CI: `.github/workflows/macos-native-build.yml`.
