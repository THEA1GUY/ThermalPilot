# TP-01 local host

Historical TP-01 record. TP-02 removes the fake adapter and simulation producers; current commands, build checks and API behaviour are documented in [READ_ONLY_TELEMETRY.md](READ_ONLY_TELEMETRY.md). Do not use this archived API walkthrough against the current host.

Updated: 31 October 2026, Africa/Lagos.

This checkpoint is a fake-only local backend. It stores profiles, simulated commands, synthetic reports and audit events. It does not control or monitor hardware, run schedules, call a model, serve a dashboard or accept phone connections.

## Build and verify

Use PowerShell 7.4+ in the repository root with the pinned .NET 10.0.403 SDK. The helper prefers %LOCALAPPDATA%/ThermalPilot/tools/dotnet/dotnet.exe, or accepts -DotnetPath.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $env:LOCALAPPDATA 'ThermalPilot/tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $env:LOCALAPPDATA 'ThermalPilot/tools/nuget-packages'
./scripts/Build-Hub.ps1
```

This performs locked restore, builds/tests the separate ThermalPilot.Hub.slnx solution, publishes the host into artifacts/hub, and runs separate-process persistence checks. Test output is synthetic. The script starts hidden fake-host children on temporary loopback ports and terminates only those owned processes to exercise crash recovery. It never launches app/GHelper.csproj.

Generated binaries, TRX results and .private/persistence-* databases/logs are ignored by Git. Private proof folders are retained locally; no blanket Temp cleanup is performed.

## Manual foreground host

Generate a local token without printing or committing it, then start the published host:

```powershell
$taskDotnet = Join-Path $env:LOCALAPPDATA 'ThermalPilot/tools/dotnet/dotnet.exe'
$env:THERMALPILOT_API_TOKEN = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
& $taskDotnet ./artifacts/hub/ThermalPilot.Host.dll serve
```

The default listener is 127.0.0.1:5127, with data at %LOCALAPPDATA%/ThermalPilot/hub. The token must contain 32-256 printable non-space characters. A local API client must receive it through a private local channel and send Authorization: Bearer <token>. The host does not print or persist the token. Restarting with a new token changes access credentials without changing the database.

Optional serve flags: --data-dir <absolute-directory> and --port <1024-65535>. Unknown/duplicate flags are rejected. CLI/environment settings cannot switch this checkpoint to a real adapter. Default web configuration sources are cleared, and the program explicitly binds IPv4 loopback rather than honouring a wildcard ASPNETCORE_URLS/Kestrel setting.

Only /health/live is public. Other endpoints require the token. Host/remote-address checks reject non-local requests and Origin-bearing browser requests are rejected. No CORS or phone/browser authentication is provided yet. This HTTP loopback API is for local tools; it is not the TP-03 phone transport.

## API v1

All responses include X-ThermalPilot-Adapter: fake and Cache-Control: no-store. Typed status/capability/command/report payloads also identify synthetic operation. Missing sensor values are null/unavailable; there are no fabricated temperature readings.

| Method/path | Behaviour |
|---|---|
| GET /health/live | Process liveness, explicitly fake |
| GET /health/ready | Storage readiness/counts; authenticated |
| GET /v1/status | Contract/schema version; fake mode; cloud, collection and phone disabled |
| GET /v1/capabilities | No hardware connection/writes/live sensors; simulation command only |
| GET /v1/telemetry | Unavailable readings with explicit quality |
| GET /v1/policy | Stored immutable checkpoint policy |
| GET/POST /v1/profiles | List up to 100 newest / create a named base-mode profile |
| GET /v1/profiles/{id} | Retrieve the persisted profile |
| GET /v1/commands and /v1/commands/{id} | Read durable simulation results |
| POST /v1/commands/simulate | Validate/store one fake activation intent; does not activate hardware |
| GET /v1/reports and /v1/reports/{id} | Read explicitly synthetic reports |
| POST /v1/reports/synthetic | Save a labelled synthetic report |
| GET /v1/events | Latest 100 ordered, caller-attributed audit events |

JSON uses camelCase string enums. Required constructor fields must be present, unknown fields are rejected and enum integers are not accepted. In particular, mode_command and arbitrary shell/settings fields cannot be submitted.

Profile input: name (1-80 characters, no control characters), baseMode (silent, balanced or turbo). Profile versions start at 1; tuning/edit/version workflows remain TP-04.

Simulation input:
- profileId and expectedProfileVersion.
- idempotencyKey, 8-100 letters/digits/underscores/hyphens.
- nonempty correlationId.
- expiresAtUtc in the future for a new command.

A retry must reuse the complete request. Its idempotency key is bound to a SHA-256 of that typed request. Identical retries, including after restart/restore, return the original command with replayed=true. A changed request under that key returns 409. A stale profile version returns 409, missing profile 404, expired new command 410. Commands have outcome=simulated, isSynthetic=true and hardwareChanged=false; no applied/verified hardware result is claimed.

Report title is limited to 160 characters; body to 16000 UTF-8 bytes. The overall HTTP request cap is 32768 bytes. Storage errors return 503 with a stable code rather than a success or silent in-memory fallback. Application logs use record/correlation IDs and error codes, not tokens or report bodies.

## Storage, migrations and recovery

The host owns thermalpilot.db plus SQLite WAL/SHM sidecars under its selected data directory. A held host.lock file prevents another host or offline maintenance process using the same directory. The file may remain after exit; the exclusive open handle, not file existence, determines ownership.

Schema 1 includes the migration ledger, profiles, commands, reports, events and preferences. Initialization validates the application ID, schema/migration version and integrity. It rejects unrelated, corrupt or future databases rather than adopting/replacing them. Data plus audit writes use transactions; concurrent idempotency checks use an immediate SQLite write transaction.

The recorded policy starts with collection/cloud disabled, zero cloud spend, 72-hour raw / 30-day aggregate / 90-day report retention proposals and a 256 MiB page budget. No background collector or retention sweeper exists in TP-01. Time-based pruning must be implemented/verified before enabling the corresponding collection/reporting features. The SQLite main-database page budget is enforced on connections; WAL, logs, backup files and filesystem capacity are separate, so this is not a hard total-disk quota.

Stop the host before maintenance:

```powershell
& $taskDotnet ./artifacts/hub/ThermalPilot.Host.dll backup --data-dir C:/path/to/original --destination C:/path/to/new-backup.db
& $taskDotnet ./artifacts/hub/ThermalPilot.Host.dll restore --data-dir C:/path/to/new-empty-restored-directory --source C:/path/to/new-backup.db
```

Backup refuses an existing destination. Restore validates the source before publishing it and requires a fresh target directory, permitting only a stale host.lock file so a failed attempt can be retried. It never overwrites the original database. Start the host using the restored data directory, inspect it, and keep the original until satisfied. Do not copy only a live .db file and ignore its WAL.

A failed operation exits nonzero; no backup/restore HTTP route exposes arbitrary filesystem paths.

## Remaining gates

TP-02 supplies real read-only telemetry under ADR-001. TP-03 supplies the dashboard and authenticated phone access. TP-04 adds real profile application and single-writer coordination. Schedules, MCP and both DeepSeek roles remain later checkpoints. No migration or automatic startup changes were made to the owner's G-Helper.

Technical references: [SQLite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions), [backup API](https://learn.microsoft.com/dotnet/standard/data/sqlite/backup), [synchronous I/O limitation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).
