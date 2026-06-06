# 06 -- TimescaleDB

## What is TimescaleDB

TimescaleDB is a PostgreSQL extension for time-series data. It adds hypertables (automatically partitioned by time), continuous aggregates, compression, and retention policies on top of standard PostgreSQL. No separate database or driver is needed -- it's a plugin to the existing PostgreSQL instance.

## Dev Stack Setup

In the Dev Docker stack, the standard PostgreSQL image is replaced with a TimescaleDB-capable image:

```yaml
# docker-compose.dev.yml (jobdate.infrastructure.dev)
image: timescale/timescaledb:latest-pg17
```

The extension is activated per-database:
```sql
CREATE EXTENSION IF NOT EXISTS timescaledb;
```

## EF Core Migration Pattern

The hypertable is created in an EF Core migration via raw SQL. The call is wrapped in a `DO $$ ... $$` block that checks `pg_available_extensions` -- this allows the migration to run even if TimescaleDB is not installed:

```sql
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_available_extensions WHERE name = 'timescaledb'
    ) THEN
        PERFORM create_hypertable(
            'log_entries',
            'timestamp',
            chunk_time_interval => INTERVAL '1 hour',
            if_not_exists => TRUE
        );
    END IF;
END
$$;
```

This pattern ensures the app starts and migrations run even in environments without TimescaleDB (e.g. plain PostgreSQL in CI). The hypertable features are simply inactive.

## Chunk Interval

**Default:** 1 Stunde pro Chunk (`RetentionOptions.ChunkIntervalHours`, default `1`).

`TimescalePolicyInitializer` setzt das Intervall beim Start explizit via:

```sql
SELECT set_chunk_time_interval('log_entries', INTERVAL '<ChunkIntervalHours> hours');
```

**Warum das nötig ist — die 7-Tage-Falle:**
TimescaleDB hat als Default-`chunk_time_interval` **7 Tage**, wenn beim `create_hypertable`-Aufruf kein Intervall gesetzt wird. Retention (`drop_after`) und Compression (`compress_after`) werden aber erst wirksam, wenn ein Chunk **vollständig** älter als die jeweilige Grenze ist. Ein 7-Tage-Chunk, der noch aktiv beschrieben wird, schließt nie → wird nie komprimiert, nie gedroppt → unbegrenztes Wachstum.

Die EF Core Migration ruft `create_hypertable('log_entries', 'timestamp', if_not_exists => true)` **ohne** `chunk_time_interval` auf → die Hypertable erbt den 7-Tage-Default (genau die Falle oben). `TimescalePolicyInitializer` erzwingt das korrekte Intervall **beim Start** via `set_chunk_time_interval` — so greift eine geänderte `ChunkIntervalHours`-Konfiguration sofort und ohne neue Migration, auch für bestehende Deployments. (Das Setzen wirkt auf **neue** Chunks; bestehende Über-Chunks altern natürlich aus.)

**Live-Zahlen (00711):** Chunk-Intervall 7d→1h: DB-Größe 3608 MB → 12 MB nach erstem Drop. Compression-Faktor 11,0× (3952 kB → 360 kB) auf den JSONB-Payloads.

Sizing-Richtwert (Ziel 25–100 MB pro Chunk unkomprimiert):
- 10k Logs/Stunde à ~500 Byte = ~5 MB/Chunk (konservativ, 1h passt)
- 100k Logs/Stunde = ~50 MB/Chunk (gut für 1h)
- 1M+ Logs/Stunde → auf 15 Minuten reduzieren

## Compression

```sql
-- Compress chunks older than 2 hours (data is rarely modified after 2h)
ALTER TABLE log_entries SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'source, stream',
    timescaledb.compress_orderby = 'timestamp DESC'
);

SELECT add_compression_policy('log_entries', INTERVAL '2 hours');
```

**Why `segmentby = 'source, stream'`:** Queries almost always filter by source and/or stream. Segmentation allows TimescaleDB to skip irrelevant segments entirely when reading compressed data.

**Expected compression ratio:** 10–15× für strukturierte Logs (50 MB Chunk → ~4 MB komprimiert). Live gemessen: 11,0× auf JSONB-Payloads (00711).

## Retention Policies

```sql
-- Chunk-level retention (drops entire chunks older than N days)
SELECT add_retention_policy('log_entries', INTERVAL '<RetentionDays> days');
```

**Policies werden beim Start immer entfernt und neu angelegt** (remove + re-add, nicht `if_not_exists => true`). Dadurch greift eine geänderte `RetentionDays`- oder `ChunkIntervalHours`-Konfiguration sofort beim nächsten Start — ohne manuellen DB-Eingriff. `TimescalePolicyInitializer` erledigt das in der Reihenfolge: Chunk-Intervall setzen → Policies entfernen → Policies neu anlegen.

TimescaleDB retention drops whole chunks -- it cannot filter individual rows by level. For fine-grained level-based retention, use a nightly cleanup job:

```sql
-- Nightly cleanup (scheduled via pg_cron or ASP.NET BackgroundService)
DELETE FROM log_entries WHERE stream = 'app' AND level < 2 AND timestamp < NOW() - INTERVAL '7 days';
DELETE FROM log_entries WHERE stream = 'app' AND level = 2 AND timestamp < NOW() - INTERVAL '30 days';
-- Chunks that become empty are dropped automatically by the chunk retention policy
```

### Retention by stream (planned)

| Stream | Retention |
|---|---|
| `app` Debug/Trace | 7 days |
| `app` Info | 30 days |
| `app` Warn/Error/Fatal | 90 days |
| `agent` | 30 days |
| `e2e` | 14 days |
| `audit` | 365 days |

## Continuous Aggregates

Pre-computed 5-minute summaries for the Aggregate API:

```sql
CREATE MATERIALIZED VIEW log_summary_5min
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('5 minutes', timestamp) AS bucket,
    source,
    stream,
    level,
    count(*) AS log_count
FROM log_entries
GROUP BY bucket, source, stream, level
WITH NO DATA;

SELECT add_continuous_aggregate_policy('log_summary_5min',
    start_offset => INTERVAL '1 hour',
    end_offset => INTERVAL '5 minutes',
    schedule_interval => INTERVAL '5 minutes'
);
```

The Aggregate API uses this view for `bucket >= 5m` queries. Smaller buckets fall back to raw table queries.

## Indexes

All indexes use `IF NOT EXISTS` for idempotency (created in EF Core migrations via `MigrationBuilder.Sql()`):

```sql
CREATE INDEX IF NOT EXISTS idx_log_entries_source ON log_entries (source, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_log_entries_level  ON log_entries (level, timestamp DESC) WHERE level >= 3;
CREATE INDEX IF NOT EXISTS idx_log_entries_trace  ON log_entries (trace_id, timestamp DESC) WHERE trace_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_log_entries_stream ON log_entries (stream, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_log_entries_tags       ON log_entries USING GIN (tags);
CREATE INDEX IF NOT EXISTS idx_log_entries_properties ON log_entries USING GIN (properties jsonb_path_ops);
```

The partial index on `level >= 3` (Warn/Error/Fatal) keeps it small -- the most frequent query pattern in alerting scenarios.

## Monitoring

Useful queries for operational checks:

```sql
-- Chunk overview
SELECT * FROM timescaledb_information.chunks
WHERE hypertable_name = 'log_entries'
ORDER BY range_start DESC;

-- Compression status
SELECT * FROM timescaledb_information.compressed_chunk_stats;

-- Ingest rate (last 5 min)
SELECT count(*) / 300.0 AS entries_per_second
FROM log_entries
WHERE timestamp > NOW() - INTERVAL '5 minutes';
```
