# ThermalPilot implementation plan

Date: 30 October 2026, Africa/Lagos.
Status: proposed implementation plan; repository/source preparation completed.
Owner repository: [THEA1GUY/ThermalPilot](https://github.com/THEA1GUY/ThermalPilot).
Source baseline: [seerge/g-helper at 0a1538a](https://github.com/seerge/g-helper/tree/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9).

Read [SOURCE_AUDIT.md](SOURCE_AUDIT.md) for inspected code and evidence limits and [BUILD_STATUS.md](BUILD_STATUS.md) for the resume point.

## 1. Outcome and agreed vision

Build one personal PC hub that the owner can operate from Windows or a paired phone, directly or conversationally. Profiles, schedules, measurements, reports and action history are shared between the interface, Codex MCP and two DeepSeek roles:

- Background observer: reviews evidence, investigates anomalies and writes reports.
- Conversational operator: creates/edits/activates profiles and creates/updates/cancels schedules from the owner's requests.

Continuous sampling, durable scheduling and hardware limits are local responsibilities. AI calls and an open conversation are not needed for already-saved schedules to execute while the required local processes are available.

The owner requested all ten proposed extensions: workload-aware profiles, heat diagnosis, efficiency tuning, quiet meeting mode, battery investigation, flight recorder, cooling trends, reversible experiments, developer integrations and supported lighting/hotkeys.

This plan is a new project. It does not change Quickone/unimeal or authorise implementation of that application's roadmap.

## 2. Repository and portfolio strategy

Use a renamed fork rather than starting with an empty repository. ThermalPilot was chosen and the fork has been created. Keep origin pointed at THEA1GUY/ThermalPilot and upstream at seerge/g-helper.

- Preserve upstream history, app/, original documentation, LICENSE and notices.
- Add ThermalPilot modules and documentation with small, reviewable modifications to upstream modules.
- Root README states exactly what is inherited, planned and implemented.
- No relabelling existing upstream capabilities as original work.
- Each checkpoint uses a codex/ branch, relevant checks, evidence, status update and pushed commit, then stops for owner review.
- Use milestone-oriented PRs with observed results, screenshots where appropriate, limitations and rollback notes.
- Do not merge, release or publish telemetry automatically.
- Before shipping, establish fork-specific executable/config/event/update identities. An upstream auto-update must not silently replace ThermalPilot with stock G-Helper.
- Preserve notices and review dependency distribution requirements before releases. No credentials or machine-specific reports in the public repo.

The local preparation clone is shallow; fetch more history when needed. The GitHub fork relationship/history is preserved.

## 3. Decisions

| ID | Decision | State |
|---|---|---|
| D-01 | Renamed G-Helper fork, project name ThermalPilot | Selected under owner's instruction; fork created |
| D-02 | DeepSeek cloud API with minimal telemetry | Confirmed by owner |
| D-03 | Phone access to the same hub/state | Confirmed by owner |
| D-04 | Windows host, single owner, one laptop initially; local SQLite | Proposed; approve with plan |
| D-05 | .NET 10 C# hub/bridge, ASP.NET Core local API, React + strict TypeScript UI | Proposed to minimise language/runtime mismatch; lock packages in TP-00/01 |
| D-06 | Narrow in-process G-Helper bridge rather than an initial hardware rewrite | Preferred; must pass TP-00/02 feasibility gates |
| D-07 | Paired phone over local network first, authenticated HTTPS; away-from-home access deferred | Proposed; settle certificate/trust onboarding in TP-03 |
| D-08 | Background host and bridge run at user sign-in initially; no pre-login execution guarantee | Proposed; settle lifecycle in TP-00; service extraction only if required |
| D-09 | Start in Observe; enable Recommend and bounded Automatic separately | Proposed; owner selects allowed actions in TP-04/06/08 |
| D-10 | Raw retention, aggregates, report retention, API spending/rate limits | Pending; choose before production collection/cloud calls; no unlimited defaults |
| D-11 | Exact DeepSeek model, credentials and notification channels | Pending TP-08; select from current official docs/account availability |
| D-12 | Permanent standalone project folder and GitHub PR workflow | Pending TP-00; planning checkout is already separate from Quickone |

Hardware target observed: ASUS ROG Zephyrus G15 GA503RM; AMD Ryzen 9 6900HS; NVIDIA RTX 3060 Laptop GPU plus AMD integrated graphics. Do not infer MUX, undervolting, fan, power or telemetry support solely from those names.

## 4. Architecture

Proposed repository layout preserves app/:

- app/Bridge/ — fork-side typed IPC, capability discovery, telemetry and coordinated execution.
- hub/ThermalPilot.Contracts/ — versioned commands, snapshots, capabilities and operation outcomes.
- hub/ThermalPilot.Host/ — per-user background host, local API, persistence, scheduler and agent runner.
- hub/ThermalPilot.Mcp/ — local stdio MCP adapter using the same application services.
- hub/ui/ — responsive desktop/phone interface.
- tests/ — contracts, persistence, scheduling, integration and UI behaviour.
- docs/thermalpilot/ — decisions, plan, build status, architecture and evidence.
- evidence/ — synthetic/redacted acceptance evidence only; private device evidence stored outside Git.

These directories are proposals, not an existing scaffold.

### Flow and ownership

PC/phone UI, Codex MCP, DeepSeek operator and the scheduler submit typed commands to the hub. The hub validates permissions, expected state/profile version and request identity, records the command and forwards an allowed operation to the bridge. The bridge executes through the existing G-Helper backend and reports outcomes.

The bridge is the only hardware-writing authority. Existing G-Helper UI controls, hotkeys, automatic power events and reapply timers must participate in the same ownership scheme. An external queue that ignores those internal writers does not satisfy this requirement.

A feasibility gate must prove this coordination before enabling automation. Do not add a second process that independently writes raw ACPI settings. Use local IPC with explicit Windows account/process access controls; any elevated hardware helper is separate from the network-facing API.

Telemetry comes back through a bounded sampling pipeline. SQLite stores history, commands, profiles, schedules and reports. DeepSeek receives a filtered evidence bundle from the local runner and can request only allowed typed tools.

Codex uses a local stdio MCP connection into the same services. No OpenAI API key is inherently needed just to expose tools to the owner's existing Codex client. Embedded Codex chat is not required for v1; work can happen in Codex with the connected MCP tools.

### Runtime boundaries

- Windows awake, user signed in, background host/bridge available: local monitoring and due schedules can operate with dashboard/chat closed.
- Internet unavailable: local monitoring/schedules continue; cloud work queues within bounded limits.
- PC asleep/off or bridge unavailable: no claim of control. Record gaps and reconcile schedules after availability returns.
- User signed out/pre-login: v1 execution is not promised under D-08.
- A phone cannot wake an arbitrary sleeping/off machine through the dashboard. Wake support would be a separate decision.
- Do not claim the local watchdog can guarantee a reset after a complete crash; prove actual firmware/control behaviour and show uncertainty.

## 5. Persistent model and command semantics

Core entities: DeviceCapabilities, TelemetrySample, TelemetryAggregate, WorkloadSession, Incident, Profile, ProfileVersion, ProfileBinding, Schedule, ScheduleOccurrence, Rule, Command, SettingChange, Experiment, Report, EvidenceReference, AgentRun, CloudUsage, PairedDevice and UserPreference.

- Store instants in UTC and retain the schedule's named timezone (initial display Africa/Lagos); test timezone changes/DST even though Lagos has no current seasonal clock change.
- Separate requested state, device-reported state and verified state.
- Measurements include units, source, collection time, freshness and quality. Sentinels map to unknown/unsupported, not zero.
- Commands have caller identity, idempotency key, correlation ID, expected version, expiry and durable outcome.
- Outcomes include accepted, applying, applied, verified, failed, partial, unsupported, cancelled, expired and unknown as appropriate. Do not pretend every setting has reliable readback.
- Reconcile crashes between device side effects and persistence. Exactly-once hardware effects are not assumed; use idempotent state-setting and readback where available.
- Schedules reference profile versions or explicitly selected latest-version behaviour. Editing a profile must not silently change a pinned future action.
- Expiring profiles use an ownership token/version. Restore only if that activation still owns the relevant settings; never overwrite a later manual choice.
- Proposed priority: model/firmware limits and local guardrails, explicit manual override, scheduled temporary action, workload rule, permitted AI recommendation. Final arbitration must be visible and tested.
- Store local secrets through a Windows-protected secret facility; SQLite holds references, not cloud keys.
- Define migrations, WAL/checkpoint management, bounded retention/downsampling, export/delete, backup/restore and disk-full outcomes.

## 6. Interface scope

Operate-mode dashboard: readable charts, explicit units, restrained status colours with text labels, keyboard access and touch targets. No animated fake sensor data in live screens. Demo mode must be labelled and isolated.

| Surface | Core jobs |
|---|---|
| Overview | Current status, profile, freshness, latest finding; change profile, mark incident, pause automation |
| Live monitor | Aligned charts, time-window selection, process/workload correlation and gaps |
| Profiles and rules | Create/clone/version/compare profiles; enable rules and define precedence |
| Schedules | Upcoming timeline, one-time/recurring actions, duration, edit/cancel and execution history |
| Experiments | Baseline/candidate, workload control, tradeoffs and restoration |
| Reports | Daily/session/incident/trend reports with clickable evidence |
| Assistant | DeepSeek conversation, previewed operations and confirmed execution results |
| Activity | Caller-attributed changes, agent runs, API usage, failures and unknown outcomes |
| Connections/settings | Codex MCP, paired phones, privacy, retention, credentials, budgets and permissions |

States: first run, permission denied, no bridge, unsupported sensor, stale data, no baseline, partial apply, restore failure, offline, expired pairing, low disk, API quota and suspended PC.

Phone controls use explicit device pairing, revocation, authenticated sessions, origin/CSRF checks and network exposure controls. Do not expose an unauthenticated localhost API to the LAN. Choose an HTTPS/certificate onboarding method before claiming installable PWA features.

## 7. Checkpoint contract

Only one approved checkpoint is implemented at a time. At each gate:

1. Review the scope/decisions and state the intended changes.
2. Implement a complete, reviewable slice.
3. Run relevant deterministic tests and real-device checks where required.
4. Record persistence/restart evidence, limitations and rollback results.
5. Review the diff, update BUILD_STATUS and push a commit on a codex/ branch to the fork.
6. Present what passed, what failed/was unsupported and the next gate; pause for owner review.

Do not claim full hardware acceptance from mock tests or UI screenshots. Hardware-sensitive actions require a controlled interactive session, a saved baseline and a recovery path. Do not automatically start the next checkpoint.

### TP-00 — Baseline, environment and integration decisions

Complexity: medium/high. Prerequisite: approve this plan's initial decisions.

Deliver:
- Choose permanent project workspace, verify its own Git root, record fork/upstream remotes and pinned source.
- Confirm D-04 through D-08 and establish initial retention/measurement policy.
- Inventory installed G-Helper/controller/services and driver availability without disabling anything.
- Prepare a .NET 10 SDK and lock the supported SDK/package baseline.
- Make a narrow development-build change to prevent the upstream KillRunningGHelper hook from unexpectedly stopping the live controller; define explicit development process/data/event identities.
- Compile the baseline without launching it. Document the exact build/publish commands and resulting artefact identities.
- Produce a bridge/lifecycle ADR that accounts for UI coupling, all hardware writers, privilege boundaries and read-only startup.

Gate: reproducible build, source/licence inventory, explicit runtime boundaries, and no active-controller interruption or settings changes during preparation. Missing prerequisites remain visible; do not label an unbuilt source tree validated.

Rollback: restore development-only source changes; installed tooling is documented, not removed without a reason.

### TP-01 — Local host, contracts and durable storage

Complexity: medium. Depends on TP-00.

Deliver host/contracts, migrations, structured events, capability/result types, command ledger and health endpoints. Use a fake adapter for deterministic operation; it must never masquerade as real hardware. Decide budgets/retention defaults before ongoing collection. Establish build and contract/persistence CI.

Gate: write a profile, command and synthetic report; restart the host and retrieve them. Test migrations, invalid inputs, disk errors, backup/restore and idempotent command submission. Demonstrate fake versus real mode is unmistakable. No hardware writes.

### TP-02 — Read-only G-Helper bridge and real telemetry

Complexity: high. Depends on TP-01.

Deliver typed local IPC and capability handshake; a genuinely read-only startup path; continuous sampling independent of visible windows; quality/freshness metadata; actual CPU/GPU/fan/battery measurements where supported.

Use existing sensor logic selectively. Measure whether polling wakes the dedicated GPU or increases battery discharge. Add faster/slow sampling tiers and idle backoff based on results, not assumptions. Additional process/clock/throttling providers must be independently validated; upstream temperature readings alone cannot establish thermal throttling.

Gate: controlled live comparison with upstream-visible readings; at least 30 minutes with dashboard hidden; distinguish zero/unsupported/stale. Restart/disconnect the bridge, sleep/resume and verify timestamps/gaps/reconnection. Record sensor/provider matrix and measured collector overhead. No profile/power/fan writes.

### TP-03 — Desktop and authenticated phone dashboard

Complexity: high. Depends on TP-02.

Deliver overview, live charts, incident marking, history, preferences and initial activity. Implement pairing/revocation, authenticated HTTPS and a workable phone trust/onboarding flow. Start with local-network access. Keep background collection independent of the browser.

Gate: actual owner-phone pairing and a live session; rejected unpaired/revoked clients; correct offline/stale states; keyboard/touch checks. Select a historical period after host restart. No private data in caches or public artefacts. Record connection method and supported browsers; no installability claim without testing.

### TP-04 — Profiles, single-writer coordination and supported controls

Complexity: high. Depends on TP-02/03.

Deliver profile create/clone/version/import/export; typed capability validation; activation previews; state snapshots; command queue/ownership; manual override and result/readback reporting. Cover supported performance, fan, boost/power, battery, display and GPU settings from upstream. Disable unsupported controls explicitly.

Route original UI/hotkeys/reapply/power events through arbitration. Reject shell hooks/raw ACPI and agent-created mode_command. Treat restart-requiring GPU transitions as separate explicit actions. No automatic undervolting, calibration or process termination.

Gate: safely activate one known profile and restore the captured baseline on the actual machine; test competing UI/hub/power-event requests, disconnect and partial failure. Verify a status response does not falsely imply all hardware applied. Backup/restore/import must preserve validated profile versions and reject unsafe fields.

### TP-05 — Durable schedules and temporary profiles

Complexity: high. Depends on TP-04.

Deliver one-time and recurring schedules, lead time, start/end/duration, edit/cancel, recurrence timezone, profile-version policy, occurrence ledger, misfire policy and ownership-aware restoration.

Default proposal: skip an expired occurrence; execute a still-relevant missed start only within a configured grace period. Reconcile clock changes, service restarts and device availability. Profile restoration is not a blind copy of an old snapshot.

Gate: save a Meeting profile activation, close the UI/chat, restart the host and demonstrate local activation plus restoration. Test duplicate requests, cancellation races, overlaps, user override, profile edits, crash mid-apply, sleep/resume and expired occurrences. Include timezone/DST test fixtures. Show what is not possible when Windows is off/asleep.

### TP-06 — Workload automation, diagnosis and flight recorder

Complexity: high. Depends on TP-04/05.

Deliver configurable application/workload rules, quiet meeting sessions, battery investigation, incident timelines, rolling telemetry windows, priority resolution, cooldown/hysteresis and protected workloads. Local AI workloads must be distinguished from cloud API usage where evidence permits.

Prefer user-configured process identities and measured activity; do not infer a call or training task simply from a process name. Detect conflicts with existing controllers and show them without disabling software automatically.

Gate: replay synthetic workload transitions deterministically, then demonstrate one real coding/inference or meeting workflow. Mark and retrieve an incident; explain evidence and uncertainty. Verify cooldown avoids profile flapping and protected workloads are never terminated. Keep rules off if required sensors are unavailable.

### TP-07 — Codex MCP parity

Complexity: medium. Depends on TP-05/06.

Deliver a local stdio MCP adapter for get_status, get_capabilities, query_telemetry, list/get/create/update profiles, activate_profile, list/create/update/cancel schedules, list/read reports, mark_incident and permitted rule changes. These are proposed names; freeze versioned schemas before implementation.

Use the same services/validation as the UI. Include dry-run/preview, bounded outputs, durable operation IDs, idempotency, state versions, explicit errors and caller attribution. Credential/config edits are scoped and recorded.

Gate: connect from the owner's actual Codex installation; retrieve live state; create a profile and schedule through Codex; inspect/edit/cancel the same objects in the hub. Confirm reads do not change hardware. Test refused, unsupported, duplicate and timed-out operations. Local scheduler works after Codex closes. Hardware mutations stay within the TP-04 policy.

### TP-08 — DeepSeek observer and evidence-backed reports

Complexity: high. Depends on TP-06; shares TP-07 service contracts.

Deliver local agent runner, current DeepSeek provider adapter, typed evidence bundle/redaction, background event/periodic reviews, daily/session/incident reports, evidence links and usage ledger. Confirm D-10/11 and provide the API key through protected storage at setup, never in chat or Git.

Cloud payload defaults: aggregate sensor readings, profile identifiers/settings and permitted application categories. No file contents, paths, command lines, screenshots, credentials, serial numbers or raw memory. Conversations are also subject to disclosure of cloud transmission.

Observer has read/report tools by default. Local detection continues offline. Use bounded retries, timeouts, queue retention, deduplication and spend/token limits. Notify on actionable change, not every healthy poll.

Gate: fixture reports distinguish facts, hypotheses and missing data; every factual device claim maps to recorded evidence. Inspect an actual outbound payload with redaction checks; make one authorised cloud request and persist a real report. Test no internet, quota/rate limit, invalid output, unsupported claims and exhausted budget. No automatic hardware write through this role.

### TP-09 — DeepSeek conversational operator

Complexity: high. Depends on TP-07/08.

Deliver Assistant UI, a separate operator role/session from the observer, shared tool dispatcher, profile/schedule previews, ambiguity resolution and durable execution feedback. Both roles may use the same provider/model but have distinct instructions, state and permissions.

Scenarios:
- Create a Docker coding profile from a known baseline.
- Activate an existing inference profile.
- Schedule Meeting mode five minutes before a call, with a specified end/restore policy.
- Reschedule or cancel an existing occurrence.
- Explain who changed the current profile and why.

Resolve ambiguous date/noon/duration only when needed; preview absolute date/time/timezone. Never claim success before receiving the service outcome. Retry/reconnect must not create duplicates. Treat process names, logs and retrieved text as data, not executable instructions.

Gate: create a profile with DeepSeek, inspect/edit it with Codex, schedule it via either, and verify one occurrence in the hub. Test denied actions, stale versions, ambiguous requests, prompt injection in telemetry/log text, interrupted conversations and API failures. An AI suggestion cannot bypass the same manual/automatic permissions as the interface.

### TP-10 — Experiments, efficiency tuning and cooling trends

Complexity: high. Depends on TP-06/08/09.

Deliver explicit baseline/candidate experiments, selected repeatable workloads, warmup/rest periods, result confidence, supported clocks/limit indicators, throughput/latency metrics and ownership-aware restoration. Never generate arbitrary tuning ranges from model text.

Compare only sufficiently similar sessions; record ambient conditions as unknown or owner-entered if unmeasured. Separate CPU package/GPU/battery rates from actual wall energy. Fan RPM is a noise proxy, not dB. Long-term trends are not a physical-fault diagnosis.

Gate: complete a controlled A/B comparison and restore settings; disclose noise/confounders and show no improvement when that is the result. Abort safely on configured conditions; test collector/bridge failure and manual interruption. Real cooling-health acceptance requires accumulated comparable history; mark it pending rather than invent a long-term result.

### TP-11 — Developer workflows and supported lighting/hotkeys

Complexity: medium. Depends on TP-05/06; lighting requires TP-04.

Deliver a small local CLI using shared commands, begin/end workload session markers, temporary profile leases and build-completion actions. Support available keyboard RGB/status capabilities; Anime Matrix/Slash features remain capability-dependent and may be unavailable on the target machine.

Gate: script starts a recorded task/profile lease, completes/fails/is interrupted, and restores only its owned settings. Verify timeout/expiry if the script crashes. Demonstrate an available notification/hotkey or mark unsupported. No public unauthenticated control API or arbitrary command execution from remote/AI requests.

### TP-12 — Packaging, recovery, portfolio and full acceptance

Complexity: high. Depends on TP-00 through TP-11; long-term evidence may remain explicitly pending.

Deliver user-sign-in startup, clean shutdown/uninstall, fork-specific update channel, release/installer strategy, config migration/backup, recovery guide, privacy controls/export/delete and documentation. Do not inherit an update flow that silently installs upstream over the fork.

Run end-to-end desktop/phone/Codex/DeepSeek journeys; host/bridge crash, logout, sleep/resume, no internet, full disk, corrupted DB backup, expired/revoked phone session and interrupted update. Measure overhead and database growth against a budget approved from TP-02 baseline. A normal soak must include representative battery/plugged-in workloads and at least 24 hours of uptime before release acceptance.

Publish accurate screenshots/demo with synthetic/redacted data and a portfolio explanation of inherited versus added modules. No false credentials, metrics or claims of universal hardware support.

Gate: owner reviews the acceptance matrix, unresolved items, restore results and release proposal. Packaging/release is not automatically authorised by implementation completion.

## 8. Requirement traceability

| ID | Requirement | Delivery / proof |
|---|---|---|
| R-01 | Actual upstream reuse and provenance | TP-00; pinned audit, retained source/history/notices |
| R-02 | One desktop/phone hub and shared state | TP-01/03; paired phone and restart retrieval |
| R-03 | Persistent history and reports | TP-01/03/08; database restart and recovery |
| R-04 | Reliable live readings and freshness | TP-02; actual capability/sampling evidence |
| R-05 | Profile creation/editing/switching | TP-04; actual activation/readback/restoration |
| R-06 | Single writer, manual override and audit | TP-04; competing-controller/event tests |
| R-07 | Scheduled/recurring/temporary profiles | TP-05; UI-closed local execution and race tests |
| R-08 | Workload-aware automation | TP-06; replay plus real workload |
| R-09 | Heat/slowdown diagnosis | TP-06/08; evidence-linked incident |
| R-10 | Quiet meeting workflow | TP-05/06; bounded duration and restoration |
| R-11 | Battery drain investigation | TP-02/06; measured discharge/activities, uncertainty |
| R-12 | Flight recorder | TP-06; persisted before/after incident window |
| R-13 | Efficiency tuning | TP-10; controlled measurements and no fabricated gain |
| R-14 | Cooling-health trends | TP-10; comparable accumulated sessions |
| R-15 | Reversible experiments | TP-04/10; partial failure and ownership-aware restore |
| R-16 | Developer API/CLI workflows | TP-07/11; script interruption/lease tests |
| R-17 | Supported lighting/hotkeys | TP-11; device demonstration or unsupported record |
| R-18 | Codex profile/schedule/report parity | TP-07/09; cross-client workflow |
| R-19 | Background DeepSeek observer | TP-08; event/periodic report with real API check |
| R-20 | Conversational DeepSeek operator | TP-09; actual shared commands, ambiguity and errors |
| R-21 | Minimal cloud disclosure and spending limits | TP-08/09; payload review, budget/offline tests |
| R-22 | Observe/Recommend/Automatic modes | TP-04/06/08/09; permission checks at service boundary |
| R-23 | Reports, toggles, schedules and activity UX | TP-03 through TP-09; representative owner journeys |
| R-24 | Recovery, startup, update and portfolio evidence | TP-12; soak, recovery and owner review |
| R-25 | Supported battery/display/GPU controls | TP-04; capabilities and explicit restart boundaries |

## 9. Principal risks and gates

| Risk | Control / stop condition |
|---|---|
| UI-coupled upstream cannot support a narrow bridge | TP-00/02 feasibility ADR; stop and scope extraction rather than silently rewrite |
| Existing UI/timers compete with hub actions | Demonstrate all writers are coordinated before TP-04 passes |
| Build or launch interrupts live G-Helper | Isolate build hook and process/data identities before build/runtime acceptance |
| Hardware error treated as success | Explicit native/API error and quality handling; readback where possible; unknown otherwise |
| Firmware resets settings or lacks readback | Advertise capabilities, record reconciliation, avoid unsupported guarantees |
| Collector wakes GPU or adds significant heat | Measure overhead/battery behaviour; adjust sampling/provider choices |
| Phone access exposes powerful operations | Pairing, encrypted/authenticated sessions, revocation, origin checks, scoped commands |
| Duplicate AI/scheduler actions | Idempotency, expected versions, occurrence ledger and crash reconciliation |
| Cloud leak or excessive cost | Typed redacted payloads, protected key, approved limits and local offline operation |
| Misleading optimisation or health claims | Repeatable workloads, evidence links, confounders, explicit pending long-term history |
| Scope becomes multiple-PC SaaS | Keep v1 personal/one host; new architecture decision before fleet/multi-tenant scope |

## 10. Build order and reviewable milestones

Critical sequence:
TP-00 -> TP-01 -> TP-02 -> TP-03 -> TP-04 -> TP-05 -> TP-06 -> TP-07 -> TP-08 -> TP-09 -> TP-10 -> TP-11 -> TP-12.

Some UI/service work can later be parallelised after interfaces stabilise; this plan does not require delegation.

- First technical proof: reproducible safe baseline build and feasible bridge/lifecycle design.
- First usable slice: TP-03, real telemetry/history on desktop and a paired phone.
- First controlled automation: TP-05, profile scheduling with persistent execution and restoration.
- First assistant-enabled slice: TP-09, Codex and DeepSeek operating the same objects.
- Complete release candidate: TP-12, all requirements accounted for and device-dependent/long-term limitations explicit.

Total complexity: large, with hardware integration and recovery dominating uncertainty. Re-estimate after TP-00 and after TP-02 measurements. Do not attach unsupported calendar dates or promise performance gains before controlled tests.

## 11. Initial approval and next step

The requested planning/source/fork preparation is complete. Implementation begins only after owner confirmation of this plan and its initial proposed decisions.

Approve TP-00 first. That phase prepares the development environment and proves the integration approach; it does not authorise all later checkpoints, automatic tuning, live cloud credentials, or a public release.
