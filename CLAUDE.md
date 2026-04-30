# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet restore
dotnet build hive.log/HiveLog.Api

# Unit-Tests (Pflicht nach Aenderungen an getesteten Klassen — siehe unten)
cd hive.log
dotnet test HiveLog.Api.Tests

# Start (Dev-Default Port 5099)
dotnet run --project hive.log/HiveLog.Api --urls "http://localhost:5099"

# EF Core migrations
cd hive.log
dotnet ef migrations add <Name> --project HiveLog.Api
```

## Unit-Tests — Pflicht nach Aenderungen

`HiveLog.Api.Tests` enthaelt 72 Unit-Tests (xUnit + NSubstitute + EF InMemory). **HiveLog.Api ist hauptsaechlich AI-generiert — die Tests sind Guardrails fuer kuenftige Agenten.**

`dotnet test HiveLog.Api.Tests` ist Pflicht nach jeder Aenderung an:

| Klasse | Tests |
|---|---|
| `QueryBuilder` | 18 Tests |
| `TemplateQueryParser` | 15 Tests |
| `RulesEngine` | 19 Tests |
| `IngestBuffer` | 9 Tests |
| `StreamFilter` | 11 Tests |

Faellt auch nur ein Test durch → Fix vor weiterem Code. Tests sind kein optionales CI-Gate.

Migrations run automatically on startup via `db.Database.MigrateAsync()`. Configuration via `.env` (see `.env-example` when created).

Swagger UI: `http://localhost:5099/swagger/index.html`

## Architecture

HiveLog is a **standalone Log-Intelligence System** for the Hive ecosystem. It is **not** a monitoring tool (no Grafana/Prometheus replacement). It collects structured logs from all backend services, the frontend client, and Dev/QA/Coach agents — built for AI agents that read, correlate, and derive decisions from logs.

### Key design decisions

- **No SyncBus / No DomainServer** — HiveLog is fully standalone, no dependency on `JobDate.Sync.Components` or `DomainServer.Core`
- **TimescaleDB** as storage backend — PostgreSQL extension for time-series data; the `log_entries` table is a hypertable partitioned by `timestamp`
- **REST/HTTP** as ingest protocol — batched POST requests; one endpoint for all callers (backend, frontend, agents)
- **Two-stage batching** — Sender-side buffering (10ms window) + Receiver-side buffering (25ms window, Bulk COPY to DB)
- **Fire-and-forget ingest** — `202 Accepted` before DB write; no per-entry ACK
- **API-Key auth** (`X-Api-Key` header) — no Keycloak JWT for ingest/query endpoints

### Ingest pipeline

```
POST /api/hivelog/v1/ingest
  → Validation (sync, fast)
  → Channel.WriteAsync(batch)   -- bounded channel, 10 000 capacity
  → 202 Accepted

BackgroundService:
  → 3-Trigger-OR Flush (Idle / Full / Cap)
  → Bulk COPY via Npgsql
  → SSE-Broadcast to subscribers (after DB write)
```

### TimescaleDB note

The `log_entries` table is a TimescaleDB hypertable. The TimescaleDB extension must be active in the PostgreSQL instance. In the Dev Docker stack, the PostgreSQL image is replaced with `timescale/timescaledb:latest-pg17`. The EF Core migration creates the hypertable via a raw SQL `DO $$ ... $$` block that checks `pg_available_extensions` — the migration works even without TimescaleDB installed, but hypertable features (chunk pruning, compression, retention policies) are only active when the extension is present.

## API endpoints

Base path: `/api/hivelog/v1`

| Method | Path | Purpose | Auth |
|---|---|---|---|
| POST | `/ingest` | Write log batch (202 Accepted) | API-Key |
| POST | `/query` | Structured search with filters + pagination | API-Key |
| POST | `/query/natural` | Natural language to SQL query | API-Key |
| GET | `/stream` | Realtime SSE log forwarding | API-Key |
| POST | `/aggregate` | Time-bucket aggregation (TimescaleDB) | API-Key |
| POST | `/admin/retention` | Adjust retention policies | API-Key (elevated) |
| POST | `/admin/flush` | Force-flush ingest buffer | API-Key (elevated) |
| GET | `/admin/stats` | Buffer fill level, ingest rate, chunk info | API-Key (elevated) |
| POST | `/admin/reindex` | Trigger reindexing after schema changes | API-Key (elevated) |
| GET | `/health` | Liveness/Readiness (PostgreSQL check) | None |

Auth header: `X-Api-Key: <key>`

## Ports

| Profile | URL |
|---|---|
| Dev default | `http://localhost:5099` |

## Key files

| File | Purpose |
|---|---|
| `HiveLog.Api/Program.cs` | Startup, DB, middleware |
| `HiveLog.Api/Persistence/HiveLogDbContext.cs` | EF Core DbContext, `log_entries` table |
| `HiveLog.Api/Features/Logs/Models/LogEntry.cs` | Log entry entity (TimescaleDB hypertable row) |
| `HiveLog.Api/Migrations/` | EF Core migrations (auto-applied on startup) |

## Architecture documentation

Full architecture documentation is in `_doc/`:

| File | Content |
|---|---|
| `_doc/INDEX.md` | Overview and document map |
| `_doc/01-overview.md` | Purpose, tech stack, role in the platform |
| `_doc/02-architecture.md` | Ingest pipeline, batch pattern, SSE stream |
| `_doc/03-data-model.md` | `log_entries` schema, indexes, design decisions |
| `_doc/04-api-endpoints.md` | All endpoints with request/response examples |
| `_doc/05-configuration.md` | ENV vars, connection strings, defaults |
| `_doc/06-timescaledb.md` | Hypertable setup, retention, compression |
| `_doc/07-nl-query.md` | NL-to-SQL architecture, libraries, privacy |

Spike notes (full architecture decision log): `.kanban/_sys/tickets/00183-log-system-architektur-spike-tiefe/03-impl-notes.md`

## Non-obvious things

- `log_entries` uses a composite PK `(timestamp, id)` — TimescaleDB requires the hypertable dimension in the PK
- Level is `SMALLINT` (0=Trace, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Fatal), compatible with .NET `LogLevel`
- `Properties` and `Exception` columns are stored as `jsonb` strings — JSONB in PostgreSQL, `string?` in C#
- Npgsql 9.x is used (`net8.0`) — `GssEncryptionMode` property does not exist in Npgsql 9.x (only Npgsql 10+)
- The project does NOT depend on `JobDate.Sync.Components` or `DomainServer.Core`
