# QA Agent Memory — hive.log

## Repo-Struktur

- `.kanban/` ist ein eigenes Git-Repo unter `/Users/thebestgin/Products/JobDate/Repos/.kanban/.git`
- hive.log-Projekt liegt unter `/Users/thebestgin/Products/JobDate/Repos/hive.log/`
- hive.log Dev-Port: 5099 (Docker), 59100+ (lokale QA-Tests)
- hive.log Build: `cd /Users/thebestgin/Products/JobDate/Repos/hive.log/HiveLog.Api && dotnet build`

## hive.log Bekannte Patterns

- Health-Check gibt `Degraded` zurück wenn `HiveLog:NlQuery:ApiKey` fehlt — das ist pre-existing, kein Fehler
- Manifest-Datei liegt unter `HiveLog.Api/_Projects/Manifests/JobDate/hivelog-manifest.json` (relativ zum CWD der App)
- `.env` konfiguriert `HiveLogManifest__FilePath=_Projects/Manifests/JobDate/hivelog-manifest.json`
- Docker-ENV muss `HiveLogManifest__FilePath` in `jobdate.infrastructure.dev/hivelog/hivelog.env` haben — fehlte bei Ticket 00248

## Git-Commit Pattern

- Kanban-Repo: `git -C /Users/thebestgin/Products/JobDate/Repos/.kanban add <files> && git commit`
- hive.log-Repo: `git` im CWD `/Users/thebestgin/Products/JobDate/Repos/hive.log/`
- Commit-Format: `00248: zurück aus Review — <Grund>`
