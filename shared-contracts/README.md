# Shared Contracts

Device-neutral API and data contracts shared by the Windows Native app, the iOS
companion, and the sync service. Source of truth:
[`docs/design/IOS-ROADTRIP-COMPANION-SYSTEM-DESIGN.md`](../docs/design/IOS-ROADTRIP-COMPANION-SYSTEM-DESIGN.md)
(sections 6, 11, and 12).

## What lives here

| File | Contract |
|------|----------|
| [`openapi/memory-line-sync-v1.yaml`](./openapi/memory-line-sync-v1.yaml) | Memory Line Sync API v1 (OpenAPI 3.0.3): devices, captures, artifacts, sync push/pull/ack, assistant, trips, detours. |
| [`json-schema/capture-envelope.v1.json`](./json-schema/capture-envelope.v1.json) | JSON Schema (draft 2020-12) for the remote capture ingestion envelope — the payload handed to Windows ingestion (`RemoteCaptureEnvelope` in `MemoryTimeline.Core`). |

## Versioning rules

- **v1 evolves only additively.** New optional fields, new endpoints, and new
  enum values may be added to the existing `*-v1` files; existing fields,
  endpoints, required-ness, and enum values are never renamed, removed, or
  repurposed.
- **Breaking changes mean a new versioned file** (e.g.
  `memory-line-sync-v2.yaml`, `capture-envelope.v2.json`) served alongside v1
  during a migration window — never an in-place edit of v1.
- **Clients must tolerate unknown fields.** Deserializers on every platform
  ignore properties they do not recognize, so an older client keeps working
  against a newer additive server.

## Client generation

Swift (iOS companion) and C# (Windows Native / sync service) client code is
**generated from these files** — they are the contract skeleton, not
documentation of hand-written clients. Generation tooling and the contract
compatibility CI gate arrive with Phase 1 (see the design doc's migration plan
and Epic A). Until then, hand-written types (e.g. `RemoteCaptureEnvelope`) must
mirror these files exactly; change the contract here first, then the code.
