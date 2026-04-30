# 07 -- Natural Language Query (NL-to-SQL)

## Purpose

The `/api/hivelog/v1/query/natural` endpoint accepts natural language questions and returns structured log results. It is the primary interface for AI agents that want to query logs without constructing structured filter objects.

## Two-Stage Architecture

### Stage 1: Template matcher (local, no AI, no external dependency)

A regex/slot-filling pattern matcher that handles 70-80% of typical agent queries:

```
"Errors today"               → SELECT * FROM log_entries WHERE level >= 4 AND timestamp > now() - interval '1 day'
"How many logs from talents-api?" → SELECT count(*) FROM log_entries WHERE source = 'talents-api'
"Trace a1b2c3d4"             → SELECT * FROM log_entries WHERE trace_id = 'a1b2c3d4-...'
"Warn/Error in last 10 min"  → SELECT * FROM log_entries WHERE level >= 3 AND timestamp > now() - interval '10 minutes'
```

No privacy risk, no latency, no external service.

### Stage 2: LLM-based (for complex questions)

For questions the template matcher cannot handle, an LLM generates SQL from natural language. The LLM only receives:
1. The database schema (table structure, column names, types)
2. The user's question

**The LLM never sees log data.** Only schema + question go to the LLM. SQL executes locally against TimescaleDB. Results stay local.

```
Question: "Show all errors from yesterday in the Engagement API"
    ↓ [to LLM: schema + question, NO log data]
SQL: "SELECT * FROM log_entries WHERE source = 'engagement-api' AND level >= 4 AND timestamp BETWEEN ..."
    ↓ [executed locally against TimescaleDB]
Result: [{...}, {...}]
    ↓ [stays local, returned to caller]
```

## SQL Safety

Generated SQL is never executed directly. Before execution:
1. Parse via Npgsql for syntax validation
2. Whitelist check: only `log_entries` and `log_summary_5min` are allowed tables
3. Read-only transaction (no INSERT/UPDATE/DELETE/DDL)
4. Result size limit enforced (max N rows)

This prevents SQL injection and unintended data modification.

## Transparency

Every response includes the interpreted query and generated SQL:

```json
{
  "interpretedQuery": { "sources": ["engagement-api"], "levels": { "min": 4 }, ... },
  "sql": "SELECT * FROM log_entries WHERE source = 'engagement-api' AND level >= 4 ...",
  "result": { "count": 7 },
  "entries": [ ... ],
  "confidence": 0.92
}
```

The agent can inspect the SQL and correct if the interpretation is wrong.

## LLM Configuration

Stage 2 uses OpenAI Chat Completions. Config via `HiveLog:NlQuery` (see `.env-example`):

| Key | Default | Description |
|---|---|---|
| `Enabled` | `true` | Set to `false` to disable Stage 2 entirely (template matcher only) |
| `ApiKey` | — | OpenAI API key (required, set via secrets) |
| `Model` | `gpt-4.1` | Model name — gpt-4.1 is sufficient for this well-defined task |
| `BaseUrl` | `https://api.openai.com` | Override for Azure OpenAI or other compatible endpoints |
| `TimeoutSeconds` | `30` | Request timeout |

**Privacy guarantee:** The LLM never sees log data. Only schema + question are sent.
Log data contains potentially personal data (user IDs, request paths, error details) —
sending it to external services is prohibited. The system prompt contains only table
structure, column definitions, and few-shot examples.

**Usage pattern:** Admin-only feature, ~3–4 calls/day. Cost is negligible.

## Confidence Score

The response includes a `confidence` field (0.0-1.0):
- Template matcher: always `1.0` (deterministic)
- LLM-based: estimated by the LLM (typically 0.7-0.95 for well-formed SQL)
- Low confidence (<0.7): the endpoint still returns the result but the caller should treat it with caution
