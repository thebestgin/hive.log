# 05 -- Configuration

Configuration is loaded from `.env` (via `DotNetEnv`) and environment variables.

## Required

| Key | Example | Description |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | `Host=localhost;Port=5432;Database=hivelog;Username=jobdate;Password=jobdate123` | PostgreSQL connection string |

## Optional / with defaults

| Key | Default | Description |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Set to `Development` for Swagger UI |
| `ASPNETCORE_URLS` | `http://localhost:5099` | Listening URL |
| `Logging__LogLevel__Default` | `Information` | Root log level for HiveLog.Api itself |
| `Logging__LogLevel__Microsoft.AspNetCore` | `Warning` | ASP.NET Core framework log level |
| `AllowedHosts` | `*` | Allowed host headers |

## Retention / TimescaleDB

| Key | Default | Description |
|---|---|---|
| `Retention__RetentionDays` | `30` | Chunks älter als N Tage werden gedroppt |
| `Retention__ChunkIntervalHours` | `1` | TimescaleDB-Chunk-Intervall in Stunden — wird beim Start via `set_chunk_time_interval` gesetzt. Kleine Chunks (Stunden) sind Voraussetzung dafür, dass Retention und Compression greifen (siehe `06-timescaledb.md`). |

## Ingest pipeline (planned -- not yet implemented)

| Key | Default | Description |
|---|---|---|
| `Ingest__ChannelCapacity` | `10000` | Bounded channel capacity |
| `Ingest__BufferWindow` | `25` | Max wait in ms before forced flush |
| `Ingest__BufferIdleAfter` | `5` | Flush after N ms without new entries |
| `Ingest__BufferMaxSize` | `1000` | Flush when buffer reaches N entries |
| `Ingest__WriteTimeout` | `100` | Max ms to wait for channel space (503 on timeout) |

## API-Key auth (planned -- not yet implemented)

| Key | Example | Description |
|---|---|---|
| `ApiKeys__Ingest` | `<key>` | Key for ingest/query endpoints |
| `ApiKeys__Admin` | `<key>` | Elevated key for admin endpoints |

## `.env-example` (to be created)

```env
ASPNETCORE_ENVIRONMENT=Development
ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=hivelog;Username=jobdate;Password=jobdate123
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning
AllowedHosts=*
```

## Port

Default dev port: **5099**

To override: `dotnet run --project hive.log/HiveLog.Api --urls "http://localhost:5099"`

## TimescaleDB connection

The standard PostgreSQL connection string is used. TimescaleDB is a PostgreSQL extension -- no separate connection string or driver needed. Ensure the `timescaledb` extension is active in the target database.

## Notes

- The project uses `DotNetEnv.Env.Load()` in `Program.cs` -- place your `.env` in the `hive.log/HiveLog.Api/` directory
- Npgsql 9.x does not have a `GssEncryptionMode` property (only Npgsql 10+). GSSAPI is not the default in Npgsql 9.x -- no explicit disable needed
- Configuration keys use `__` as the separator for environment variables (double underscore), which maps to `:` in C# code
