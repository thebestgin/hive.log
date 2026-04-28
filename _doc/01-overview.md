# 01 -- Overview

## Purpose

hive.log is the **central Log-Intelligence System** for the Hive ecosystem. It collects structured logs from all backend services, the frontend client, and Dev/QA/Coach agents. It is not a monitoring tool (no Grafana/Prometheus replacement) -- it is built for AI agents that read, correlate, and derive decisions from logs.

## Tech Stack

| Component | Technology |
|---|---|
| Runtime | ASP.NET Core, .NET 8 |
| Database | PostgreSQL + TimescaleDB extension (EF Core 8, Npgsql 9.x) |
| Auth | API-Key (`X-Api-Key` header) |
| Time-series | TimescaleDB hypertable (`log_entries`) |
| Bulk ingest | Npgsql COPY (binary format) |
| Streaming | SSE (Server-Sent Events) |
| Swagger | Swashbuckle 6.x |
| Health | AspNetCore.HealthChecks.NpgSql |

## Git Repository

Part of the JobDate monorepo at `Repos/hive.log/`.

## Base Path

```
/api/hivelog/v1
```

Swagger UI: `http://localhost:5099/swagger/index.html`

## Ports

| Profile | URL |
|---|---|
| Dev default | `http://localhost:5099` |

## Role in the Platform

```
Backend Services (talents-api, engagement-api, ...)
  |  POST /api/hivelog/v1/ingest (batch, API-Key)
  v
HiveLog.Api  <-- validates, buffers, bulk-inserts to TimescaleDB
  |  SSE /api/hivelog/v1/stream
  v
Dev-Agents / QA-Agents / Coach-Agents (realtime log forwarding)

AI-Agents / Humans
  |  POST /api/hivelog/v1/query  (structured filter)
  |  POST /api/hivelog/v1/query/natural  (NL-to-SQL)
  v
HiveLog.Api  <-- queries TimescaleDB, returns paginated results
```

HiveLog is a **write-once, query-many** system. All services push logs; agents and humans pull via query APIs.

## What it is NOT

- Not a metrics system (use Prometheus/OpenTelemetry for counters/gauges)
- Not an alerting system (no PagerDuty/alertmanager integration)
- Not a tracing system (no Jaeger/Zipkin replacement, though trace_id correlation is supported)
- Not part of the SyncBus pipeline (no Changes, no Revisions, no DomainSync)

## Commands

```bash
dotnet restore
dotnet build hive.log/HiveLog.Api
dotnet run --project hive.log/HiveLog.Api --urls "http://localhost:5099"

# EF Core migrations
cd hive.log
dotnet ef migrations add <Name> --project HiveLog.Api
```
