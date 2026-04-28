# 04 -- API Endpoints

Base path: `/api/hivelog/v1`

Auth: `X-Api-Key: <key>` header on all endpoints except `/health`.

## Ingest -- `POST /ingest`

Accepts a batch of log entries. Returns immediately with `202 Accepted` before any DB write.

**Request:**
```json
POST /api/hivelog/v1/ingest
Content-Type: application/json
X-Api-Key: <key>

{
  "source": "talents-api",
  "sourceType": "backend",
  "instanceId": "jd-talents-dev",
  "entries": [
    {
      "timestamp": "2026-04-28T14:30:00.123Z",
      "level": 2,
      "category": "JobDate.Talents.Api.Features.Keens.KeenChangeProcessor",
      "message": "Processed 42 keen changes in 12ms",
      "messageTemplate": "Processed {Count} keen changes in {ElapsedMs}ms",
      "properties": { "count": 42, "elapsedMs": 12 },
      "traceId": "a1b2c3d4-0000-0000-0000-000000000001",
      "tags": ["sync", "keen"],
      "stream": "app"
    }
  ]
}
```

**Response:**
```
202 Accepted
{ "accepted": 1 }
```

**Error responses:**
- `400 Bad Request` -- missing required fields (`source`, `sourceType`, `entries`)
- `503 Service Unavailable` -- ingest channel full (backpressure); sender should back off

**Design:** One endpoint for all callers. Differentiate via `sourceType` field (`"backend"`, `"frontend"`, `"agent"`), not via separate API paths.

## Query -- `POST /query`

Structured log search with filtering, pagination, and cursor-based navigation.

**Request:**
```json
POST /api/hivelog/v1/query
Content-Type: application/json
X-Api-Key: <key>

{
  "streams": ["app", "agent"],
  "sources": ["talents-api", "engagement-api"],
  "levels": { "min": 3 },
  "timeRange": { "from": "2026-04-28T00:00:00Z", "to": "2026-04-28T23:59:59Z" },
  "traceId": "a1b2c3d4-0000-0000-0000-000000000001",
  "tags": { "any": ["sync", "keen"] },
  "search": "KeenChangeProcessor",
  "properties": { "count": { "$gte": 10 } },
  "orderBy": "timestamp_desc",
  "limit": 100,
  "cursor": "2026-04-28T14:30:00.123Z|a1b2c3d4-0000-0000-0000-000000000001"
}
```

**Response:**
```json
{
  "entries": [ { ... } ],
  "nextCursor": "2026-04-28T14:29:59.000Z|...",
  "hasMore": true,
  "total": 1523
}
```

**Why POST instead of GET:** Filter combinations become complex quickly. POST with JSON body is readable, testable, and has no URL length limits.

## Natural Language Query -- `POST /query/natural`

AI-powered query interface: natural language in, structured results out.

**Request:**
```json
POST /api/hivelog/v1/query/natural
Content-Type: application/json
X-Api-Key: <key>

{
  "question": "How many errors were there today in the Engagement API?",
  "stream": "app",
  "timeRange": { "from": "2026-04-28T00:00:00Z", "to": "2026-04-28T23:59:59Z" }
}
```

**Response:**
```json
{
  "interpretedQuery": {
    "sources": ["engagement-api"],
    "levels": { "min": 4 },
    "timeRange": { "from": "...", "to": "..." }
  },
  "sql": "SELECT count(*) FROM log_entries WHERE source = 'engagement-api' AND level >= 4 AND ...",
  "result": { "count": 7 },
  "entries": [ ... ],
  "confidence": 0.92
}
```

**Transparency principle:** Always return the generated SQL and the interpreted query. The agent can see what happened and correct if needed. See `07-nl-query.md` for the two-stage architecture.

## Stream -- `GET /stream` (SSE)

Realtime log forwarding via Server-Sent Events. Filter by query parameters.

```
GET /api/hivelog/v1/stream?sources=talents-api&levels=3,4,5&stream=app
Accept: text/event-stream
X-Api-Key: <key>
```

**Query parameters:**
- `sources` -- comma-separated source names
- `levels` -- comma-separated level numbers (0-5)
- `stream` -- stream name (`app`, `agent`, `e2e`, `audit`)
- `tags` -- comma-separated tags (any-match)

**SSE Events:**
```
event: log
data: {"timestamp":"2026-04-28T14:30:00.123Z","level":4,"source":"talents-api","message":"..."}

event: heartbeat
data: {}
```

Heartbeat every 15 seconds to keep the connection alive.

## Aggregate -- `POST /aggregate`

Time-bucket aggregation using TimescaleDB Continuous Aggregates.

**Request:**
```json
POST /api/hivelog/v1/aggregate
Content-Type: application/json
X-Api-Key: <key>

{
  "metric": "count",
  "groupBy": ["source", "level"],
  "bucket": "5m",
  "timeRange": { "from": "2026-04-28T00:00:00Z", "to": "2026-04-28T23:59:59Z" },
  "stream": "app"
}
```

**Response:**
```json
{
  "buckets": [
    { "time": "2026-04-28T14:00:00Z", "source": "talents-api", "level": 2, "count": 1523 },
    { "time": "2026-04-28T14:00:00Z", "source": "talents-api", "level": 4, "count": 3 }
  ]
}
```

Supported `bucket` values: `1m`, `5m`, `15m`, `1h`, `1d`.

## Admin endpoints -- `POST|GET /admin/*`

All admin endpoints require an elevated API-Key.

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/admin/retention` | Adjust retention policies (days per stream/level) |
| `POST` | `/admin/flush` | Force-flush the ingest buffer immediately (useful for tests) |
| `GET` | `/admin/stats` | Buffer fill level, ingest rate (entries/s), chunk count/size |
| `POST` | `/admin/reindex` | Trigger reindexing after schema changes |

## Health -- `GET /health`

No auth required.

```
GET /health

200 OK
{
  "status": "Healthy",
  "entries": {
    "database": { "status": "Healthy", "duration": "00:00:00.0031234" }
  }
}
```

Uses `AspNetCore.HealthChecks.NpgSql` for the PostgreSQL connectivity check.
