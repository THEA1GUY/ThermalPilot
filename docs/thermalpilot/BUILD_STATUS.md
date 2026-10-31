# ThermalPilot build status

Updated: 31 October 2026, Africa/Lagos.

## Resume point

**TP-03 implementation is in progress; Android device acceptance is pending.** TP-00 through TP-02 remain completed and awaiting owner review. No TP-04 work has started.

The owner accepted TP-01 and authorized TP-02 with an explicit no-simulation requirement. A separate read-only controller and host have sampled and persisted actual laptop measurements; runtime simulation producers are removed. The inherited application entry point remains inert. No benchmark, hardware profile change, schedule execution or cloud-agent invocation has been run.

## Completed preparation

- Created the renamed fork [THEA1GUY/ThermalPilot](https://github.com/THEA1GUY/ThermalPilot), with seerge/g-helper as its parent.
- Downloaded upstream source at 0a1538ac4fb0337f6cf3d64a907acde10c91a3d9.
- Configured local origin to the owner's fork and upstream to seerge/g-helper.
- Created codex/implementation-plan.
- Inspected relevant sensor, profile, ACPI, lifecycle, configuration and build paths.
- Identified the laptop/CPU/GPU through read-only inventory.
- Recorded the .NET SDK gap, build/launch lifecycle effects and unresolved hardware capabilities.
- Wrote the implementation plan, source audit and project instructions.
- Pushed the planning documents on codex/implementation-plan; verified the remote branch at initial planning commit de982e5d5774636757179323356afce6c18b03c1.
- Opened [draft planning PR #1](https://github.com/THEA1GUY/ThermalPilot/pull/1) for owner review. No merge or application implementation has been performed.
- Validation passed: relative Markdown links, 13 checkpoint definitions, 25 traced requirements and git diff whitespace checks. These are documentation checks, not application acceptance.

## Checkpoints

| ID | Status | Evidence / next gate |
|---|---|---|
| TP-00 | Completed; owner authorized continuation | Local and dedicated-checkout locked builds/publishes, metadata guards, focused reviews and remote CI passed; draft PR #2 |
| TP-01 | Completed; owner authorized continuation | Historical storage foundation; 29 tests and recovery checks passed; draft PR #3 |
| TP-02 | Completed; owner review pending | Actual ASUS/Windows readings; 30-minute final-version laptop run; 27 tests; ASUS-required recovery checks and CI passed; draft PR #4 |
| TP-03 | In progress; implementation and PC checks passed, owner Android acceptance pending | Dashboard, owner collection toggle, incident/history views, LAN HTTPS pairing/revocation; see [DASHBOARD.md](DASHBOARD.md) |
| TP-04 | Not started | Coordinated profiles and supported controls |
| TP-05 | Not started | Persistent schedules and temporary profiles |
| TP-06 | Not started | Workload rules, diagnosis and flight recorder |
| TP-07 | Not started | Codex MCP workflows |
| TP-08 | Not started | DeepSeek monitoring/reporting agent |
| TP-09 | Not started | DeepSeek conversational operator |
| TP-10 | Not started | Controlled experiments, efficiency and long-term health |
| TP-11 | Not started | Supported lighting/hotkeys and developer workflows |
| TP-12 | Not started | Packaging, recovery and full acceptance |

Every checkpoint requires owner review before the next one. See IMPLEMENTATION_PLAN.md for deliverables, dependencies, checks and rollback requirements.

## Known gaps and pending decisions

- SDK 10.0.403 is installed in ThermalPilot's per-user tools directory and pinned by root global.json. System-wide .NET 9.0.301 remains unchanged.
- Hardware identity and the read-only sensor subset are observed. Available controls, additional providers and actual baseline performance still require their respective checkpoints.
- ADR-001 accepts a per-user host and narrow controller bridge, with required read-only extraction and writer-coordination proofs still gated in TP-02/04.
- Windows/personal use/SQLite, .NET 10/React strict TypeScript, LAN-first phone design and user-session lifecycle are accepted with plan approval. Collection and cloud remain off; retention proposals and action permissions must be implemented/verified before enabling them.
- DeepSeek key, model and spending limit are not configured.
- No fixed dates or delivery-duration promises; re-estimate after TP-00.
- TP-00 adds a compile-only entry point and build/tooling configuration. The original control routines remain unmodified and unexecuted. Hardware settings, live controller/config, services/tasks and system SDK were not changed.

## TP-00 evidence

See [TP-00-EVIDENCE.md](TP-00-EVIDENCE.md), [BUILD_GUIDE.md](BUILD_GUIDE.md), [ADR-001](ADR-001-HOST-AND-BRIDGE.md) and [dependency inventory](DEPENDENCIES.md).

Passed locally: SDK archive verification; locked restore; Release/win-x64 build with zero warnings/errors; framework-dependent publish; inert-entry-point inspection of build and publish outputs; negative native-file rejection; targeted C# formatting; PowerShell/XML/JSON parsing; links/whitespace; two focused reviews. The live G-Helper process and config checksum matched the before snapshot.

Repeated the locked build/publish and both metadata checks in the permanent dedicated checkout: passed with zero warnings/errors. Final controller/config and relevant ASUS service states still matched the initial inventory.

Source checkpoint commit: 7bee316c5258df22e36ee47c9863b512d04a48bc, pushed to origin/codex/tp-00-baseline. [GitHub Actions build](https://github.com/THEA1GUY/ThermalPilot/actions/runs/40988050307) passed for that commit. [Draft TP-00 PR #2](https://github.com/THEA1GUY/ThermalPilot/pull/2) is stacked on [planning PR #1](https://github.com/THEA1GUY/ThermalPilot/pull/1). Neither PR has been merged and no release has been created.

Dependency metadata gaps for HidSharpCore/NvAPIWrapper and embedded-module provenance remain release follow-up tasks, not false clearance claims.

## Checkout

Permanent dedicated checkout: C:/Users/Asus/OneDrive/Documents/ChatGPT/ThermalPilot. Its own root and fork/upstream remotes are verified. The pushed codex/tp-02-readonly-telemetry branch is checked out there; its full ASUS-required build/test/publish and process recovery checks passed. Quickone remains untouched.

## TP-01 evidence

Scope: D-04/D-05 local host and shared contracts, fake adapter only. R-02/R-03 receive durable-state foundations; their phone/history/reporting journeys remain later gates. See [LOCAL_HOST.md](LOCAL_HOST.md) and [TP-01-EVIDENCE.md](TP-01-EVIDENCE.md).

Passed in the implementation checkout: locked restore, Release build/publish, 29 unit/API/native-SQLite tests with no skips, C# formatting, PowerShell parsing and focused C#/API/build-script reviews. A malformed-backup JSON error path was fixed and regression-tested.

Separate-process proof passed: a profile, simulated command and synthetic report survived abrupt owned-host termination, restart, backup and restoration to a fresh directory. Replaying the command preserved its ID and three audit events. Storage-full/read-only failures roll back transactions; no in-memory fallback reports success.

The host binds loopback, requires a bearer token except for liveness, rejects browser Origin headers and accepts only typed simulation inputs. Hardware application/bridge, phone networking, schedules and AI are absent. Collection/cloud remain disabled. Retention values are recorded proposals without a sweeper; the SQLite main-file page budget is not a total-disk quota.

Repeated the locked restore, Release tests/publish and separate-process recovery checks in the permanent checkout: passed, 29 tests, no failures or skips. One profile/command/report and three audit events survived restart and fresh-directory restore; retries preserved the original command identity. The live G-Helper process identity/start time and configuration checksum matched the private before snapshot.

Source checkpoint commit: 91125ce8ea3237cf130e04d1ec07f03d125e96fd, verified on origin/codex/tp-01-local-host. [GitHub Actions build](https://github.com/THEA1GUY/ThermalPilot/actions/runs/41148488625) passed for that commit, including both inert-baseline and hub checks. [Draft TP-01 PR #3](https://github.com/THEA1GUY/ThermalPilot/pull/3) is stacked on TP-00 PR #2. No PR was merged or release created. Subsequent phase-record-only commits do not change the tested source.

## TP-03 evidence

Scope: D-03/D-05/D-07 dashboard and authenticated phone access; R-04, R-07, R-08, R-09, R-11, R-12 and R-13 views/preferences. The dashboard shows actual samples, persisted history, incidents, current reports/events and host policy. Owner collection changes are persisted and audited; paired phones cannot change them. Browser tokens remain in memory only. Long history windows use representative actual samples across the selected period. See [DASHBOARD.md](DASHBOARD.md).

The locked React/Vite build passed. The pinned .NET 10 Release build passed with zero warnings/errors. All 35 API/storage tests passed, including served-dashboard headers/content, owner-only collection control, collection preference persistence, incident persistence, pairing permissions and migrations. `Build-Hub.ps1 -RequireAsusSensors` passed on the actual ASUS laptop: real Windows/ASUS sensor sample, malformed-frame continuity, stale state after disconnect, controller reconnect, recorded gap, and backup/restore of actual telemetry. Its owned host/controller processes were stopped by the check. Private raw evidence remains under the ignored `.private/live-check-*` directory.

Owner acceptance is still required on the actual Android phone: install and trust the generated local CA, pair over the PC's private Wi-Fi address, confirm live or explicitly unavailable sensor state, confirm history after host restart, verify phone revocation rejects requests, and exercise keyboard/touch interactions. The LAN certificate files are not generated and no firewall rule is installed. TP-03 is not complete until those observations are recorded. No sensor values or phone results have been simulated.

Pushed checkpoint: `e7c6550ab04fb1685f0b49b3bed6ac0e6ffe71b9` on `origin/codex/tp-03-dashboard`, stacked on `codex/tp-02-readonly-telemetry`. The connected GitHub integration initially returned HTTP 403 for PR creation; after refreshing the GitHub CLI login, [draft PR #5](https://github.com/THEA1GUY/ThermalPilot/pull/5) was opened against TP-02.

The permanent checkout was synced to this branch. Its dashboard build passed, but the .NET build stopped before tests/sensor checks because Windows denied writes to ignored `obj` cache files (`MSB3491`/`UnauthorizedAccessException`), including on retry with elevated execution. The full ASUS-required build/test/recovery run passed in the isolated implementation checkout above; no permanent-checkout pass is claimed.

## Next owner gate

Review TP-02/[PR #4](https://github.com/THEA1GUY/ThermalPilot/pull/4) and TP-03/[draft PR #5](https://github.com/THEA1GUY/ThermalPilot/pull/5), then complete the Android phone acceptance before approving TP-03. No TP-04 work may start until TP-03 is accepted. No simulated measurement, simulated actuation or synthetic report may stand in for actual device evidence.

## TP-02 evidence

Scope: D-06 read-only extraction/pipe and D-10 bounded local raw storage; R-04 sensor quality/freshness foundation and R-11 battery observations. See [READ_ONLY_TELEMETRY.md](READ_ONLY_TELEMETRY.md) and [TP-02-EVIDENCE.md](TP-02-EVIDENCE.md).

The controller selectively reuses upstream DSTS/native Windows sensor knowledge without linking the application/control bootstrap. It has an owner/session-scoped pipe, explicit owner ACL with NETWORK deny, bounded typed protocol and no write operation. The real G-Helper remains the writer.

Actual supported readings were sampled and persisted; unsupported/unavailable GPU temperature and mid-fan queries stayed explicit. Owner-performed sleep/resume produced a 66.5-second gap, corroborated by Windows power-event IDs. Both AC and battery sampling tiers were observed. Owner-provided G-Helper readings and private unit-normalization evidence were recorded.

27 SQLite/API/protocol tests passed; focused native/C# and host/persistence/script reviews are clear after fixes. The final controller session spans 30.517 minutes/462 stored snapshots, with a 30.09-minute host observation window. The complete capture contains 756 actual snapshots, zero synthetic snapshots and two recorded gaps; all rows/gaps matched after fresh-directory restore.

The permanent checkout passed Build-Hub.ps1 -RequireAsusSensors: zero-warning/error build, all 27 tests, actual ASUS CPU reading, malformed-frame continuity, stale quality on disconnect, reconnect, recorded gap and actual-data backup/restore. Its inherited baseline build/publish/metadata guards also passed. All owned validation processes are stopped.

Final-session native query time averaged 2.946 ms (maximum 27.975 ms). Controller/host CPU averages were 0.184%/0.879% of one core; peak working sets were 39.9/77.4 MiB. These observations do not establish battery savings or a causal GPU-wake penalty.

Limits: GPU temperature and mid-fan query unavailable; GPU state changed from early D3 to later D0 around power/activity changes, so wake/energy attribution is inconclusive. Low-load backoff was not observed under this workload. The existing G-Helper process/services continued, while its config checksum changed during the test sequence; do not claim globally unchanged settings.

Pushed checkpoint: 3446caa70d170652a771d2700782f7786dbf497f on origin/codex/tp-02-readonly-telemetry, following implementation 40a2b09 and clean-build fix 87ad94f. [Push CI](https://github.com/THEA1GUY/ThermalPilot/actions/runs/41152917563) and [PR CI](https://github.com/THEA1GUY/ThermalPilot/actions/runs/41152922494) passed. [Draft PR #4](https://github.com/THEA1GUY/ThermalPilot/pull/4) is stacked on TP-01 PR #3. Subsequent phase-record-only commits preserve the tested implementation. No PR merged or release created.

## Completion record for future phases

For each phase append: scope and decision IDs; branch/commit/PR; checks with results; persisted-behaviour evidence; hardware observations and limits; rollback/recovery result; unresolved items; owner review status; next resume point.

Do not mark a hardware-dependent requirement complete based only on simulation. Do not mark a source commit as pushed until the remote branch is verified.
