# 03 -- Data Model

## Primary Table: `log_entries`

```sql
CREATE TABLE log_entries (
    -- Time-Series Key (TimescaleDB Hypertable dimension)
    timestamp       TIMESTAMPTZ     NOT NULL,

    -- Identification
    id              UUID            NOT NULL DEFAULT gen_random_uuid(),
    trace_id        UUID            NULL,        -- Correlation across service boundaries
    span_id         UUID            NULL,        -- Sub-correlation within a trace

    -- Source
    source          TEXT            NOT NULL,    -- e.g. "talents-api", "connect-app", "dev-agent-qa"
    source_type     TEXT            NOT NULL,    -- "backend" | "frontend" | "agent"
    instance_id     TEXT            NULL,        -- Container / Pod / Agent instance

    -- Log data
    level           SMALLINT        NOT NULL,    -- 0=Trace, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Fatal
    category        TEXT            NOT NULL,    -- .NET: logger name; Frontend: component name
    message         TEXT            NOT NULL,    -- Formatted log text
    message_template TEXT           NULL,        -- Structured template (e.g. "User {UserId} logged in")

    -- Structured data
    properties      JSONB           NULL,        -- Key-value pairs from structured logging
    exception       JSONB           NULL,        -- { type, message, stackTrace, inner }

    -- Context
    user_id         UUID            NULL,        -- Keycloak Subject-ID (when available)
    request_id      TEXT            NULL,        -- HTTP Request-ID
    session_id      TEXT            NULL,        -- Agent session or browser session

    -- Tags for filtering
    tags            TEXT[]          NULL,        -- Free tags: ["sync", "keen", "performance"]

    -- Stream (multi-tenancy)
    stream          TEXT            NOT NULL DEFAULT 'app',  -- "app" | "agent" | "e2e" | "audit"

    PRIMARY KEY (timestamp, id)
);
```

## TimescaleDB Hypertable

```sql
-- Created in EF Core migration via raw SQL (guarded by pg_available_extensions check)
SELECT create_hypertable('log_entries', 'timestamp',
    chunk_time_interval => INTERVAL '1 hour'
);
```

### Why 1-hour chunks

At expected Dev volume (10k-100k logs/hour) each chunk is 50-500MB uncompressed -- within the recommended 25-100MB range for optimal TimescaleDB performance. For production (>1M logs/hour), reduce to 15-minute chunks.

## Indexes

```sql
-- Frequent filter patterns
CREATE INDEX IF NOT EXISTS idx_log_entries_source ON log_entries (source, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_log_entries_level  ON log_entries (level, timestamp DESC) WHERE level >= 3;
CREATE INDEX IF NOT EXISTS idx_log_entries_trace  ON log_entries (trace_id, timestamp DESC) WHERE trace_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_log_entries_stream ON log_entries (stream, timestamp DESC);

-- Array/JSONB GIN indexes
CREATE INDEX IF NOT EXISTS idx_log_entries_tags       ON log_entries USING GIN (tags);
CREATE INDEX IF NOT EXISTS idx_log_entries_properties ON log_entries USING GIN (properties jsonb_path_ops);
```

## C# Entity

`HiveLog.Api/Features/Logs/Models/LogEntry.cs` -- EF Core entity mapped to `log_entries`.

Key mapping notes:
- Composite PK: `HasKey(e => new { e.Timestamp, e.Id })`
- `Tags` is `string[]?` in C#, `text[]` in PostgreSQL
- `Properties` and `Exception` are `string?` in C# (JSON serialized manually), `jsonb` in PostgreSQL
- `Stream` has default value `"app"` via `HasDefaultValue()`

## Level enum

| Value | Name | .NET LogLevel |
|---|---|---|
| 0 | Trace | Trace |
| 1 | Debug | Debug |
| 2 | Info | Information |
| 3 | Warn | Warning |
| 4 | Error | Error |
| 5 | Fatal | Critical |

## Streams

| Stream | Purpose |
|---|---|
| `app` | Normal application logs from all backend services |
| `agent` | Logs from Dev/QA/Coach AI agents |
| `e2e` | End-to-end test runs |
| `audit` | Security/compliance events (longer retention) |

## Design decisions

| Decision | Reason |
|---|---|
| `timestamp` as hypertable dimension | TimescaleDB partitions by time -- perfect fit for logs |
| `SMALLINT` for level | More efficient than TEXT; compatible with .NET `LogLevel` enum (0-5) |
| `JSONB` for properties + exception | Flexible, indexable, no schema lock-in |
| `TEXT[]` for tags | Native PostgreSQL support, GIN-indexable, faster than JSONB for simple contains queries |
| Composite PK `(timestamp, id)` | TimescaleDB requires the hypertable dimension in the PK; UUID ensures uniqueness |
| Single table instead of per-stream tables | Simpler to query (no JOIN), stream-based retention via policy filters |
