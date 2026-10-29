# ThermalPilot build status

Updated: 30 October 2026, Africa/Lagos.

## Resume point

**Next checkpoint: TP-00 — approve the plan, prepare the toolchain and settle integration boundaries.**

Planning and repository preparation are complete. No ThermalPilot application features are implemented. No application build, live sensor sampling, benchmark, profile change, schedule execution or cloud-agent invocation has been performed.

## Completed preparation

- Created the renamed fork [THEA1GUY/ThermalPilot](https://github.com/THEA1GUY/ThermalPilot), with seerge/g-helper as its parent.
- Downloaded upstream source at 0a1538ac4fb0337f6cf3d64a907acde10c91a3d9.
- Configured local origin to the owner's fork and upstream to seerge/g-helper.
- Created codex/implementation-plan.
- Inspected relevant sensor, profile, ACPI, lifecycle, configuration and build paths.
- Identified the laptop/CPU/GPU through read-only inventory.
- Recorded the .NET SDK gap, build/launch lifecycle effects and unresolved hardware capabilities.
- Wrote the implementation plan, source audit and project instructions.

## Checkpoints

| ID | Status | Evidence / next gate |
|---|---|---|
| TP-00 | Not started; preparation available | Approve design proposals; settle dedicated development location, SDK/build isolation and bridge boundaries |
| TP-01 | Not started | Persistent local host and shared contracts |
| TP-02 | Not started | Read-only bridge and reliable telemetry |
| TP-03 | Not started | Desktop/paired-phone dashboard |
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

- Source targets .NET 10; installed SDK observed is .NET 9.0.301. No SDK installation was performed.
- Hardware identity is known, but sensor support, available controls, existing controller/service versions and actual baseline performance are not yet verified.
- Prefer a narrow G-Helper IPC bridge; this remains subject to a feasibility spike.
- Windows/personal use/SQLite, precise stack, LAN-first phone access, retention and automation bounds require confirmation of the proposed plan. Phone access and minimal-telemetry DeepSeek cloud use were explicitly requested.
- DeepSeek key, model and spending limit are not configured.
- No fixed dates or delivery-duration promises; re-estimate after TP-00.
- No application source changes or hardware-setting changes are included in this planning checkpoint.

## Checkout

The current checkout is in this chat's durable planning area, outside Quickone. It is not nested in the Quickone repository. Before sustained implementation, choose a permanent dedicated project folder and use a separate Codex project/workspace there.

## Completion record for future phases

For each phase append: scope and decision IDs; branch/commit/PR; checks with results; persisted-behaviour evidence; hardware observations and limits; rollback/recovery result; unresolved items; owner review status; next resume point.

Do not mark a hardware-dependent requirement complete based only on simulation. Do not mark a source commit as pushed until the remote branch is verified.
