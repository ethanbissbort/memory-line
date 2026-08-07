# Memory Line Sync Service (`MemoryTimeline.SyncApi`)

The self-hosted sync service for the iOS roadtrip companion — deployment
**Mode A** from the system design
([`docs/design/IOS-ROADTRIP-COMPANION-SYSTEM-DESIGN.md`](../docs/design/IOS-ROADTRIP-COMPANION-SYSTEM-DESIGN.md),
§4.1/§4.2): an ASP.NET Core (net8.0) service that runs on your own machine or
home-lab and brokers captures between mobile devices and the Windows
Memory Timeline app. Single owner, SQLite metadata, filesystem artifact store.

Unlike the WinUI solution, this is a **plain cross-platform .NET project**: it
builds, tests, and runs with the ordinary `dotnet` CLI on any OS (no Visual
Studio `msbuild.exe` required). CI builds it on Linux
([`.github/workflows/sync-api-build.yml`](../.github/workflows/sync-api-build.yml)).

Phase 1 scope (design §19):

- device pairing/registration, self-issued JWT access tokens with refresh
  rotation and revocation;
- idempotent capture metadata ingestion;
- chunked artifact upload with byte-length/part-count/SHA-256 validation, and
  artifact download;
- an append-only change log behind cursor-based `/sync/pull`, per-device
  idempotent `/sync/push`, and `/sync/ack`.

The assistant, trips, and detours endpoints exist in the API contract
([`shared-contracts/openapi/memory-line-sync-v1.yaml`](../shared-contracts/openapi/memory-line-sync-v1.yaml))
but are later phases — this service does not implement them yet.

## Layout

| Path | Contents |
|------|----------|
| `MemoryTimeline.Services.sln` | Solution: the service, its tests, and the shared contracts library. |
| `MemoryTimeline.SyncApi/` | The service — `Domain/` (EF entities), `Application/` (services, options, error codes), `Infrastructure/` (DbContext, artifact store, bootstrapper). |
| `MemoryTimeline.SyncApi.Tests/` | xUnit tests, including `WebApplicationFactory`-based integration tests of the full HTTP surface. |
| `../shared-contracts/dotnet/MemoryTimeline.SyncContracts/` | Wire DTOs shared with the Windows client (referenced by project, camelCase JSON). |

## Run locally

Requires the .NET 8 SDK.

```bash
cd services
dotnet build MemoryTimeline.Services.sln
dotnet test  MemoryTimeline.SyncApi.Tests/MemoryTimeline.SyncApi.Tests.csproj
dotnet run --project MemoryTimeline.SyncApi
```

No `launchSettings.json` is checked in, so Kestrel uses its defaults
(`http://localhost:5000`); pass `--urls` to bind elsewhere, e.g.

```bash
dotnet run --project MemoryTimeline.SyncApi --urls http://0.0.0.0:5210
```

On first run the service creates the data directory, generates a token
signing key and a pairing code, and creates the SQLite schema. The pairing
code appears in the startup log and in `{DataDir}/pairing-code.txt`.

### Configuration

All settings live in the `SyncApi` configuration section and can be supplied
via `appsettings.json` or environment variables (`SyncApi__<Name>`); every
value has a working default, so an empty configuration runs.

| Setting | Environment variable | Default | Purpose |
|---------|----------------------|---------|---------|
| `DataDir` | `SyncApi__DataDir` | `data` (relative to the working directory) | Directory holding the SQLite DB, artifact blobs, signing key, and pairing code. |
| `PairingCode` | `SyncApi__PairingCode` | generated on first run | Owner pairing code required by device registration. When configured, it overwrites `{DataDir}/pairing-code.txt`. |
| `AccessTokenLifetimeMinutes` | `SyncApi__AccessTokenLifetimeMinutes` | `60` | JWT access token lifetime. |
| `RefreshTokenLifetimeDays` | `SyncApi__RefreshTokenLifetimeDays` | `30` | Refresh token lifetime. |
| `RefreshTokenGraceSeconds` | `SyncApi__RefreshTokenGraceSeconds` | `300` | How long the previous refresh token stays usable after a rotation, so a client that never received the rotation response can recover. `0` = strict rotation. |
| `MaxPartSizeBytes` | `SyncApi__MaxPartSizeBytes` | `8388608` (8 MiB) | Maximum accepted bytes per uploaded artifact part. |
| `TokenSigningKeyBase64` | `SyncApi__TokenSigningKeyBase64` | generated on first run, persisted to `{DataDir}/signing.key` | Base64 HMAC-SHA256 signing key, at least 32 bytes decoded. |

> **Note:** raising `SyncApi:MaxPartSizeBytes` above ~28.6 MB (Kestrel's
> 30,000,000-byte default `MaxRequestBodySize`) additionally requires raising
> Kestrel's request body size limit, or part uploads fail with 413 before the
> service sees them.

## Docker

> **The build context must be the repository root**, not `services/`: the
> image compiles `shared-contracts/dotnet/MemoryTimeline.SyncContracts`
> alongside the service, and that directory lives outside `services/`.

```bash
# From the repository root:
docker build -f services/MemoryTimeline.SyncApi/Dockerfile -t memory-line-sync .

docker run -d --name memory-line-sync \
  -p 8080:8080 \
  -v memory-line-sync-data:/data \
  memory-line-sync
```

- The container listens on port `8080` (the ASP.NET Core 8 container
  default); map it to whatever host port you like (`-p 5210:8080`).
- `/data` is the data directory (the image sets `SyncApi__DataDir=/data` and
  declares `VOLUME /data`) — **always mount a volume there** or the
  database, artifacts, signing key, and pairing code vanish with the
  container.
- Read the generated pairing code with
  `docker exec memory-line-sync cat /data/pairing-code.txt`
  (or from `docker logs memory-line-sync`), or pin your own via
  `-e SyncApi__PairingCode=...`.

## Pairing a device

1. Start the service and note the pairing code (startup log, or
   `{DataDir}/pairing-code.txt`).
2. In the Windows app, open **Settings → Sync**, enter the server URL
   (e.g. `http://homeserver:5210`) and the pairing code, and click **Pair**.
   The app registers itself (`POST /api/v1/devices/register`), stores its
   device ID and token pair, and — once sync is enabled — starts the
   background sync worker.
3. The same page can **unpair** this machine, and device revocation
   (`DELETE /api/v1/devices/{deviceId}`) works against *any* device of the
   owner — this is how Windows revokes a lost iPhone. Revoked tokens stop
   working immediately.

The pairing code is compared in constant time; registration is the only
unauthenticated endpoint.

## API surface

Base path `/api/v1`; bearer JWT auth on everything except registration; an
`Idempotency-Key` header on create/mutation calls; camelCase JSON. Full
contract: [`shared-contracts/openapi/memory-line-sync-v1.yaml`](../shared-contracts/openapi/memory-line-sync-v1.yaml).

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/v1/devices/register` | Pair a device (pairing code → device ID + access/refresh tokens). Idempotent by `Idempotency-Key`. |
| POST | `/api/v1/devices/{deviceId}/refresh` | Exchange the refresh token for a new access token; the refresh token rotates. |
| DELETE | `/api/v1/devices/{deviceId}` | Revoke a device; its tokens stop working immediately. |
| GET | `/api/v1/devices` | List the owner's devices (additive v1 extension; powers the Windows revocation UI). |
| POST | `/api/v1/captures` | Create capture metadata; idempotent by client-generated `captureId` (replay returns the existing capture). |
| GET | `/api/v1/captures/{captureId}` | Fetch a capture with its artifact summaries. |
| PATCH | `/api/v1/captures/{captureId}` | Update descriptive fields (captureType, titleHint, userNote, tripId); last writer wins. |
| POST | `/api/v1/captures/{captureId}/complete` | Mark the capture fully uploaded (Phase 1: minimal handler, returns current state). |
| POST | `/api/v1/captures/{captureId}/retry` | Re-run processing for a failed capture (Phase 1: minimal handler). |
| POST | `/api/v1/captures/{captureId}/artifacts/initiate` | Declare an artifact (type, expected byte length, SHA-256) → part size/count plan. |
| PUT | `/api/v1/artifacts/{artifactId}/parts/{partNumber}` | Upload one part; re-uploading a part number replaces it. |
| POST | `/api/v1/artifacts/{artifactId}/complete` | Assemble the parts; validates byte length, part count, and SHA-256 before accepting. |
| GET | `/api/v1/artifacts/{artifactId}/download` | Download the artifact bytes. |
| POST | `/api/v1/sync/push` | Push client outbox entries; idempotent per (device, client sequence) via push receipts. |
| GET | `/api/v1/sync/pull?cursor=&limit=` | Ordered changes after the cursor; the caller's own changes are suppressed. |
| POST | `/api/v1/sync/ack` | Acknowledge the applied cursor (diagnostic — the client's local cursor store is authoritative). |

Errors use the `ApiError` envelope (`code`, `message`, `retryable`,
`correlationId`); `retryable` drives client backoff (design §16.2).

## Security notes (design §14)

- **Auth model:** registration is gated by the owner pairing code; every
  other call requires a short-lived, device-bound, self-issued HMAC-SHA256
  JWT. Refresh tokens rotate on every use — the previous token stays usable
  only for the short `RefreshTokenGraceSeconds` recovery window (`0` for
  strict rotation) — and revocation takes effect immediately.
- **Transport:** the service itself speaks **plain HTTP**. Do not expose it
  to the public internet directly — reach it over a private overlay network
  (**Tailscale/WireGuard recommended**, per the design's Mode A) or put a
  TLS-terminating reverse proxy (Caddy/nginx/Traefik) in front of it.
- **Secrets:** the signing key lives at `{DataDir}/signing.key` unless
  provided via `SyncApi__TokenSigningKeyBase64` (prefer a secret manager for
  real deployments). Deleting/rotating the key invalidates all outstanding
  access tokens. Protect the data directory — it contains personal audio.
- **Logging (§14.5):** logs are structured and carry IDs, counts, and error
  codes only — never transcripts, notes, audio bytes, tokens, or precise
  location.

## Where data lives

```text
{DataDir}/                 # SyncApi__DataDir, default ./data
├── sync.db                # SQLite: owners, devices, captures, capture_artifacts,
│                          #   sync_changes (change log), push_receipts, idempotency_records
├── artifacts/             # filesystem blob store (parts during upload, assembled artifacts after complete)
├── signing.key            # base64 HMAC token signing key (generated if not configured)
└── pairing-code.txt       # current owner pairing code
```

Back up `{DataDir}` as a unit; `sync.db` and `artifacts/` reference each
other.
