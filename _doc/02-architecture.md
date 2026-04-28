# 02 -- Architecture

## High-Level Overview

```
Sender (Backend / Frontend / Agent)
  [Sender-side buffer: 10ms window, max 200 entries]
  |
  POST /api/hivelog/v1/ingest  (batch JSON)
  |
HiveLog.Api
  |
  Validation (sync, fast)
  |
  Channel<LogBatch>  (bounded, 10 000 capacity)
  |  202 Accepted returned immediately
  |
  BackgroundService (IngestWorker)
  |  3-Trigger-OR Flush: Idle(5ms) / Full(1000) / Cap(25ms)
  |
  Npgsql COPY (binary bulk insert)
  |
  log_entries (TimescaleDB Hypertable)
  |
  SSE Broadcast --> /api/hivelog/v1/stream subscribers
```

## Ingest Pipeline

### Sender-side batching

Each sender (backend service, frontend client, agent) buffers log entries locally before sending. The 3-Trigger-OR pattern from HiveCache:

| Trigger | Condition | Action |
|---|---|---|
| Full | Buffer reached `maxSize` | Immediate flush |
| Idle | No new entry for `idleAfter` | Flush |
| Cap | `bufferWindow` elapsed | Flush regardless |

**.NET adapter defaults:**
- `BufferWindow`: 10ms
- `BufferIdleAfter`: 3ms
- `BufferMaxSize`: 200

**Frontend adapter defaults:**
- `bufferWindow`: 50ms (fewer network requests)
- `bufferIdleAfter`: 15ms
- `bufferMaxSize`: 50 (less memory pressure)

**Agent adapter defaults:**
- `bufferWindow`: 5ms (fast visibility)
- `bufferIdleAfter`: 2ms
- `bufferMaxSize`: 100

### Receiver-side batching (HiveLog.Api)

The ingest endpoint returns `202 Accepted` immediately -- before any DB write. A background worker drains the channel:

```
IngestController.Ingest()
  1. Validate request (source, entries)
  2. Channel.WriteAsync(batch)  -- up to 100ms timeout
  3. Return 202 Accepted

IngestWorker (BackgroundService):
  1. Channel.ReadAsync / DrainTo
  2. 3-Trigger-OR Flush (BufferWindow=25ms, IdleAfter=5ms, MaxSize=1000)
  3. Npgsql COPY binary bulk insert into log_entries
  4. SSE broadcast to active stream subscribers
```

**Receiver defaults:**
- `BufferWindow`: 25ms
- `BufferIdleAfter`: 5ms
- `BufferMaxSize`: 1000 (large batches for COPY efficiency)
- `ChannelCapacity`: 10 000 (bounded with backpressure)

### Backpressure behavior

When the channel is full (10 000 items), `Channel.WriteAsync` waits until space is available. The HTTP handler has a 100ms timeout:
- If the timeout fires: batch is dropped, `dropped_batches` counter incremented, `503 Service Unavailable` returned
- The sender should implement exponential backoff on 503

### Why Npgsql COPY instead of INSERT

COPY is 5-10x faster than individual INSERTs for bulk writes. At 1000 log entries:
- `INSERT INTO ... VALUES (...)`: ~15-20ms
- `COPY ... FROM STDIN (FORMAT binary)`: ~2-3ms

TimescaleDB automatically optimizes COPY for hypertables.

## SSE Stream

The `/api/hivelog/v1/stream` endpoint forwards log entries in realtime to subscribers using Server-Sent Events.

```
log_entries written to DB
  |
IngestWorker triggers SSE broadcast
  |
StreamController (active SSE connections)
  |  Filter by: source, level, stream, tags
  |
SSE event: { "timestamp": "...", "level": 4, "source": "...", "message": "..." }
```

Filter parameters: `sources`, `levels`, `stream`, `tags` (query string).

Events:
- `event: log` -- log entry matching filters
- `event: heartbeat` -- keep-alive every 15 seconds (empty data)

Bounded channels per subscriber with `DropOldest` -- a slow consumer does not block the pipeline.

## Key components

| Component | File | Purpose |
|---|---|---|
| `HiveLogDbContext` | `Persistence/HiveLogDbContext.cs` | EF Core DbContext, `log_entries` table |
| `LogEntry` | `Features/Logs/Models/LogEntry.cs` | Hypertable entity |
| `Program.cs` | `Program.cs` | Startup, DI registration, middleware pipeline |

## Pattern origins

| Pattern | Source | What was changed |
|---|---|---|
| 3-Trigger-OR flush | `hivecache/HiveBatchBuffer` | Bounded channel with Backpressure instead of DropOldest |
| Metrics instrumentation | `jobdate.sync.api` batching | Adapted Counter/Histogram names |
| Options pattern with validation | `jobdate.sync.api` IngestBatchingOptions | Simplified for log use case |
| SSE per-subscriber channels | `jobdate.sync.api` ChangeRevisionNotificationBroadcaster | Added level/source/tag filters; bounded channels with DropOldest |
