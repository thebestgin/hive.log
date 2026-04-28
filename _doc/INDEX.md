# hive.log -- Architektur-Dokumentation

> AI-optimierter Einstiegspunkt. Diese Datei zuerst lesen.

## Dokumente

| # | Datei | Was du findest |
|---|---|---|
| 1 | [01-overview.md](01-overview.md) | Zweck, Tech-Stack, Rolle im System |
| 2 | [02-architecture.md](02-architecture.md) | Ingest-Pipeline, Batch-Pattern, SSE-Stream |
| 3 | [03-data-model.md](03-data-model.md) | `log_entries` Schema, Indexes, Design-Entscheidungen |
| 4 | [04-api-endpoints.md](04-api-endpoints.md) | Alle Endpunkte mit Request/Response-Beispielen |
| 5 | [05-configuration.md](05-configuration.md) | ENV-Vars, Connection Strings, Defaults |
| 6 | [06-timescaledb.md](06-timescaledb.md) | Hypertable-Setup, Retention, Compression |
| 7 | [07-nl-query.md](07-nl-query.md) | NL-to-SQL Architektur, Libraries, Datenschutz |

## Key Design Decisions (Schnellreferenz)

- **Standalone** -- kein SyncBus, kein DomainServer; unabhaengig von anderen JobDate-Services
- **Log-Intelligence, kein Monitoring** -- gebaut fuer AI-Agents, nicht fuer Grafana-Dashboards
- **TimescaleDB** -- PostgreSQL-Extension fuer Zeitreihen; `log_entries` ist eine Hypertable
- **REST/HTTP mit Batching** -- ein Ingest-Endpunkt fuer alle Caller (Backend, Frontend, Agents)
- **Fire-and-forget** -- `202 Accepted` vor DB-Write; zweistufiges Batching (Sender + Empfaenger)
- **API-Key Auth** -- `X-Api-Key` Header; kein Keycloak JWT

## Architektur-Referenz

Vollstaendige Spike-Analyse mit allen 15 Entscheidungspunkten:
`.kanban/_sys/tickets/00183-log-system-architektur-spike-tiefe/03-impl-notes.md`
