# Read-only telemetry

Updated: 31 October 2026, Africa/Lagos.

TP-02 replaces the simulated runtime with actual Windows/ASUS telemetry. No fake adapter, simulated-command producer or synthetic-report producer remains in the host. Historical enum/record types remain solely so TP-01 records can still be read. Saving a profile does not activate it.

## Build and run

Use the pinned SDK and PowerShell 7.4+. Build-Hub.ps1 runs SQLite/API/protocol checks and an actual Windows native-sensor/reconnect/recovery check. It requires no ASUS hardware in CI; CI measurements describe its Windows runner, not the owner's laptop.

The acceptance script refuses to run if a controller already exists. Stop only a controller you started before invoking it. It never terminates G-Helper or takes ownership of an existing controller.

Host and controller are separate foreground applications, normally started in two terminals:

```powershell
$taskDotnet = Join-Path $env:LOCALAPPDATA 'ThermalPilot/tools/dotnet/dotnet.exe'
& $taskDotnet ./artifacts/controller/ThermalPilot.Controller.dll
```

```powershell
$env:THERMALPILOT_API_TOKEN = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
& $taskDotnet ./artifacts/hub/ThermalPilot.Host.dll serve --collect true
```

The controller polls only while running. The host persists only when explicitly started with --collect true. Without it the API returns 503 for live telemetry and does not invent readings. No sign-in task, Windows service, firewall rule, phone listener or cloud connection is installed.

Keep the API token private and supply it as a bearer token. The host binds IPv4 loopback, rejects Origin headers and preserves TP-01's local request checks. Collection is local; cloud spending remains zero.

## Isolation and protocol

The separately compiled controller links only app/Bridge/ReadOnly/*.cs and the contracts project. It never references the G-Helper application assembly, Program, AppConfig, ModeControl, logger, GPU vendor libraries or Pawn modules. The inherited baseline stays inert; its project excludes the extracted controller sources.

ASUS reads reuse the DSTS packet, endpoint knowledge, presence flag, temperature validation and fan scaling from upstream AsusACPI.DeviceGet/GetFan and HardwareControl. Windows battery/system reads reuse the native-query approach. This is a narrow extraction with new result/error handling, rather than starting the inherited controller.

The native handle uses the driver's existing shared-open convention. Its only DeviceIoControl payload method is DSTS; there is no DEVS, INIT, watchdog, calibration, fan-curve or raw-device command. Driver failure and unsupported values remain unavailable/unsupported, never zeroed.

The version-1 named pipe is scoped to the Windows user SID and interactive session. Its explicit protected ACL permits that user and denies NETWORK identities. A local singleton prevents duplicate controllers. Frames are length-prefixed and limited to 32768 bytes, strict JSON, a four-second request deadline and only handshake/snapshot operations. Every response declares readOnly and hardwareWritesEnabled=false. Local same-user malicious software remains outside this boundary.

The host validates the protocol/capabilities/sensor identities and rejects synthetic or invalid readings. It reconnects with bounded timeouts/backoff without launching, killing or taking over a controller.

## Sources and meaning

| Sensor | Source | Unit and interpretation |
|---|---|---|
| CPU temperature | ASUS DSTS 0x00120094 | Celsius; no fallback to an unrelated thermal zone |
| GPU temperature | ASUS DSTS 0x00120097 | Celsius when firmware supplies it; no NVAPI initialization |
| CPU/GPU/mid fan | ASUS DSTS 0x00110013/14/31 | Upstream raw value multiplied by 100 RPM; zero is allowed only on a successful supported read |
| Battery charge / AC | GetSystemPowerStatus | Percent / boolean; unknown native values remain unavailable |
| Battery rate | CallNtPowerInformation/SystemBatteryState | Signed watts: negative discharge, positive charge; system-reported estimate |
| CPU use | GetSystemTimes deltas | Percent; initial sample and long-gap baseline are unavailable |
| RAM use | GlobalMemoryStatusEx | Percent |

Every reading includes value, unit, source, timestamp, quality and an unavailable reason. Sampling is independent of visible windows: two seconds on AC and ten on battery/unknown power. Three successive actual CPU-use readings below 5% back off to ten seconds on AC or twenty on battery; a higher/unknown CPU reading resets that quiet tier. All-provider failure also backs off to twenty seconds. These are collection intervals, not hardware power profiles. Extra GPU/clock/throttling/process providers are absent. Temperature alone is not proof of thermal throttling.

The host changes retained values to stale after the greater of 15 seconds or three sampling intervals; it preserves their original timestamps. It records controller session changes and long sampling gaps. A sleeping/off PC cannot collect during that gap.

G-Helper's fan percentage is relative to its configured maximum, not an independent measurement of fan PWM. The hub reports RPM and does not load the live G-Helper configuration. Comparisons must normalize units using separately inspected settings and must distinguish configured maxima from measured calibration.

## API and storage

GET /v1/telemetry returns the latest real snapshot, including stale values when applicable; absence is 503. GET /v1/telemetry/health reports bridge/collection/storage state. GET /v1/telemetry/history returns the latest 100 persisted snapshots; GET /v1/telemetry/summary returns counts/time bounds/gaps.

Status contract version is 2; bridge protocol is 1; SQLite schema is 2. Schema-1 data is migrated transactionally. Profiles and historical command/report/event reads remain. Simulation and synthetic-report POST routes were removed.

GET /v1/policy distinguishes stored disabled defaults from this process's effective collection flag. Raw records are pruned on append, history/summary reads and periodic maintenance even without an incoming bridge sample. They expire after 72 hours and are capped at 20000 rows, with an 8192-byte per-record cap and the existing 256 MiB main-database page limit. WAL/backup/logs are outside that page limit. Gaps are capped at 1000 entries. No minute aggregates or automatic report producer exists yet; their proposed retention values do not imply active collection.

Disk errors produce explicit storage-failed state/backoff rather than an in-memory persistence success. SQLite WAL and offline snapshot backup are retained. Schema-1 backups validate read-only, migrate in a unique staging copy and publish to a fresh target only after validation; the original remains intact.

## Acceptance and limits

See [TP-02-EVIDENCE.md](TP-02-EVIDENCE.md) and [BUILD_STATUS.md](BUILD_STATUS.md). Hardware observations are separate from CI's actual Windows-native checks. Private raw history, access tokens, controller identities, config checksums and power-event messages are excluded from Git.

Fan/profile/power changes remain TP-04. No thermal improvement, battery saving or comprehensive sensor support is claimed by a successful collector run.

References: [Windows battery structure](https://learn.microsoft.com/windows/win32/api/winnt/ns-winnt-system_battery_state), [device power property](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-powerdata), [named pipe API](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstream?view=net-10.0).
