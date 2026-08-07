# Memory Line iOS Roadtrip Companion

Phase 2 capture MVP (design doc `docs/design/IOS-ROADTRIP-COMPANION-SYSTEM-DESIGN.md`,
§19 Phase 2): one-touch recording, durable offline queue, background upload to the
self-hosted sync service, capture history/status, Lock Screen + Home Screen widgets,
and App Intents for Siri / Shortcuts / the Action Button.

No external dependencies — system SQLite, CryptoKit, AVFoundation, URLSession only.
Nothing to resolve on first open.

## Building (Mac mini)

1. Requirements: Xcode 16 or newer (the project uses the Xcode 16
   filesystem-synchronized project format), iOS 17+ deployment target.
2. Open `ios-companion/MemoryLineCompanion.xcodeproj`.
3. Select the `MemoryLineCompanion` target → Signing & Capabilities → choose your
   team (a free personal team works). Xcode will register the bundle IDs and the
   `group.com.memoryline.companion` App Group automatically. Do the same for the
   `MemoryLineWidgets` target.
   - If your team can't use that exact group ID, change it consistently in both
     targets' capabilities AND in `Config/*.entitlements`, plus the
     `WidgetSharedState.appGroupId` constants in
     `MemoryLineCompanion/Shared/Domain/Models.swift` and
     `MemoryLineWidgets/WidgetSharedState.swift`.
4. Build/run the `MemoryLineCompanion` scheme on your iPhone.
   - Free personal teams: apps expire after 7 days and need re-deploying; a paid
     developer account signs for a year.

## Pairing with the sync service

1. Run the service (see `services/README.md`); note the pairing code from its
   startup log or `{DataDir}/pairing-code.txt`.
2. In the app: Settings → enter the server URL (e.g. `http://mac-mini.tailnet:8080`
   or wherever the service listens) and the pairing code → Pair.
3. Record something. It saves locally first, then uploads when connectivity allows;
   the Windows app pulls, ingests, and it shows up in review exactly once.

Plain HTTP is allowed by the app's transport policy because the service is designed
to run on a private overlay network (Tailscale/WireGuard, design §14.2). Put TLS in
front if you expose it any other way.

## Layout

```text
ios-companion/
├── MemoryLineCompanion.xcodeproj      # hand-authored, 3 targets, synchronized folders
├── Config/                            # Info.plists + entitlements (outside synced folders)
├── MemoryLineCompanion/               # app target (folder-synchronized)
│   ├── App/                           # entry point, environment/DI, root view
│   ├── Features/Capture|History|Settings|Sync/
│   ├── Shared/Domain|Persistence|Networking|Security|Support/
│   ├── AppIntents/                    # Siri / Shortcuts / Action Button intents
│   └── Assets.xcassets
├── MemoryLineWidgets/                 # widget extension target
└── MemoryLineCompanionTests/          # unit tests (XCTest, app-hosted)
```

## Not in this phase

Assistant, trips/detours, CarPlay screens (design phases 4–6). Per the resolved
§22 decisions, CarPlay will be simulator-designed later — rendering on a real head
unit needs an Apple-granted entitlement even for sideloaded builds; Siri, the Lock
Screen widget, and the Action Button carry the in-car experience meanwhile.
