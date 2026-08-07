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

The C# wire types live in
[`dotnet/MemoryTimeline.SyncContracts`](./dotnet/MemoryTimeline.SyncContracts)
and are used **verbatim** by both the sync service
(`services/MemoryTimeline.SyncApi`) and the Windows sync client
(`windows-native/src/MemoryTimeline.Sync`), serialized camelCase via
System.Text.Json web defaults. The OpenAPI file **mirrors those shipped DTO
wire shapes**, so a client generated from the YAML (e.g. Swift for the iOS
companion) interoperates with the shipped service. A contract-compatibility CI
gate that keeps the YAML and the DTOs from drifting is still planned (design
doc migration plan, Epic A); until it exists, change the DTOs and the YAML
together in the same commit. Hand-written types elsewhere (e.g.
`RemoteCaptureEnvelope`) must likewise mirror these files exactly.

## Changelog

### v1.1 (2026-08-07)

Reconciled `openapi/memory-line-sync-v1.yaml` with the wire protocol
implemented in Phase 1 (the shared DTOs are the implemented truth):

- **Sync**: push carries `entries` of `SyncPushEntry` and returns per-entry
  `SyncPushEntryResult` receipts (`clientSequence`/`accepted`/`duplicate`/
  `serverChangeId`/`error`); pulled `SyncChange` carries `changeId`,
  `revision`, `sourceDeviceId`, and the payload as a JSON **string**
  (`payloadJson`, documented by the `CaptureChangePayload` schema incl.
  `audioArtifact`); every cursor (`cursor` query param, `nextCursor`, ack
  `cursor`, `changeId`) is `integer`/`int64`, not string.
- **Devices**: registration requires `pairingCode`, `platform`, and
  `displayName` (`publicKey` is optional); the response gains `ownerId`;
  refresh takes a `TokenRefreshRequest` body, returns a `TokenRefreshResponse`
  with a rotated refresh token, and answers 401 — never 404 — for unknown or
  revoked devices (anti-enumeration); added `GET /devices` returning
  `DeviceInfo` entries.
- **Artifacts**: initiate response is `{ artifactId, partSizeBytes,
  expectedPartCount }`; complete takes `{ partCount, totalByteLength, sha256 }`
  and returns `ArtifactCompleteResponse`; `ArtifactSummary` replaces the old
  `CaptureArtifact` schema; documented the server artifact lifecycle
  (`pending | uploading | complete | failed`, distinct from the Windows-local
  `upload_state` vocabulary) and the actually-emitted 409/413/422 responses
  with their error codes (`artifact_conflict`, `artifact_incomplete_parts`,
  `artifact_part_too_large`, `artifact_*_mismatch`, `artifact_not_complete`,
  `artifact_not_found`).
- **Captures**: `CaptureCreateRequest` gains optional `titleHint` and
  `userNote`; `POST /captures` documents the 200 idempotent-replay response;
  the `Capture` response schema now matches the `CaptureResponse` DTO
  (embedded `artifacts` summaries).
- **Idempotency-Key** is now documented as optional everywhere — an opt-in
  replay mechanism for authenticated mutations; sync push idempotency is
  carried by `clientSequence` receipts instead of the header.
