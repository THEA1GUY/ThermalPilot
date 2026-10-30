# ThermalPilot build status

Updated: 30 October 2026, Africa/Lagos.

## Resume point

**TP-00 completed; awaiting owner review. Next checkpoint: TP-01 — local host, shared contracts and persistent storage.**

The owner approved the plan and TP-00. The inherited application now builds/publishes with an inert entry point. No runtime hub features are implemented, and no application executable, live sensor sampling, benchmark, profile change, schedule execution or cloud-agent invocation has been run.

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
| TP-00 | Completed; owner review pending | Local and dedicated-checkout locked builds/publishes, metadata guards, focused reviews and remote CI passed; draft PR #2 |
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

- SDK 10.0.403 is installed in ThermalPilot's per-user tools directory and pinned by root global.json. System-wide .NET 9.0.301 remains unchanged.
- Hardware identity is known, but sensor support, available controls, existing controller/service versions and actual baseline performance are not yet verified.
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

Permanent dedicated checkout: C:/Users/Asus/OneDrive/Documents/ChatGPT/ThermalPilot. Its own root and fork/upstream remotes are verified. The pushed TP-00 branch is checked out there and its locked build/publish was repeated successfully. Quickone remains untouched.

## Next owner gate

Review TP-00/PR #2 before approving TP-01. TP-01 builds the fake-backed local host/contracts/SQLite slice and persistence evidence; it does not enable hardware writes, phone networking or cloud agents. Do not implement later phases unattended.

## Completion record for future phases

For each phase append: scope and decision IDs; branch/commit/PR; checks with results; persisted-behaviour evidence; hardware observations and limits; rollback/recovery result; unresolved items; owner review status; next resume point.

Do not mark a hardware-dependent requirement complete based only on simulation. Do not mark a source commit as pushed until the remote branch is verified.
