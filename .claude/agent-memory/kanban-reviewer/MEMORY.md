# Code-Reviewer Memory — hive.log

## hive.log Patterns & Conventions

- **Auth filter pattern**: `IAsyncActionFilter` registered as singleton via `ServiceFilter(typeof(...))`.
  Sets `HttpContext.Items["Connector"]` for the controller to pick up.
- **Manifest-driven connectors**: All connector config lives in `hivelog-manifest.json`.
  Source/SourceType MUST come from manifest — never from request body.
- **Fail-fast startup**: `ManifestLoader.LoadAndValidate()` throws `InvalidOperationException` on any config issue.
  Config key: `HiveLogManifest:FilePath` (ENV: `HiveLogManifest__FilePath`).
- **ApiKey comparison**: Uses `string ==` (ordinal) — NOT `CryptographicOperations.FixedTimeEquals`.
  This is a known timing-attack surface in the codebase (see 00248 review finding).
- **NpgsqlDataSource**: Npgsql 9.x is used here — `GssEncryptionMode` property does NOT exist in Npgsql 9.x (only 10+).
  Do NOT flag missing GssEncryptionMode as a finding for hive.log.

## Recurring Anti-Patterns Seen

- **Timing-unsafe API key comparison** (`a.ApiKey == keyValue`) instead of `CryptographicOperations.FixedTimeEquals`.
  Affects `ConnectorAuthFilter.cs`. Medium severity — realistic attack surface only in internal-network scenarios.
- **Hardcoded dev keys in committed manifest** (`hivelog-manifest.json`).
  Acceptable for dev-default manifest per ticket design decision; prod keys injected externally via ENV/mount.

## False-Positive Patterns to Avoid

- `hivelog-manifest.json` containing dev API keys is intentional (design decision in 00248).
  Do not flag as a security issue — prod manifest is mounted externally.
- Missing `GssEncryptionMode` on NpgsqlDataSource — not applicable in Npgsql 9.x.
