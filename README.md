# ThermalPilot

A personal PC monitoring, profiles and automation hub being developed from [G-Helper](https://github.com/seerge/g-helper).

**Status: TP-01 local host implemented; final verification in progress.** A separate fake-only host stores profiles, simulated commands, synthetic reports and audit history in SQLite. The inherited application retains its inert entry point. The dashboard, phone access, live telemetry, hardware controls, MCP server and DeepSeek agents remain later checkpoints. No performance improvement is claimed.

## Intended experience

- One hub on the Windows PC and a paired phone.
- Live measurements, session history, incident investigations and reports.
- Profiles for coding, inference, gaming, meetings and battery use.
- Persistent schedules, workload rules, reversible experiments and manual overrides.
- A conversational DeepSeek assistant and a separate background reporting agent.
- The same profile, scheduling and investigation workflows through Codex MCP.
- Cloud analysis limited to explicitly permitted telemetry.

## Start here

- [Implementation plan and checkpoints](docs/thermalpilot/IMPLEMENTATION_PLAN.md)
- [Upstream source audit](docs/thermalpilot/SOURCE_AUDIT.md)
- [Build status and resume point](docs/thermalpilot/BUILD_STATUS.md)
- [Contributor and agent instructions](AGENTS.md)
- [Build the inert baseline](docs/thermalpilot/BUILD_GUIDE.md)
- [Build and use the fake local host](docs/thermalpilot/LOCAL_HOST.md)
- [TP-01 persistence and recovery evidence](docs/thermalpilot/TP-01-EVIDENCE.md)
- [Host and bridge architecture decision](docs/thermalpilot/ADR-001-HOST-AND-BRIDGE.md)
- [TP-00 evidence and limits](docs/thermalpilot/TP-00-EVIDENCE.md)
- [Dependency/provenance inventory](docs/thermalpilot/DEPENDENCIES.md)

The first target device is an ASUS ROG Zephyrus G15 GA503RM with a Ryzen 9 6900HS and RTX 3060 Laptop GPU. Hardware identity has been observed; available controls and sensor behaviour still require validation.

## Upstream

ThermalPilot is a renamed fork of seerge/g-helper, preserving its history. The planning baseline is commit `0a1538ac4fb0337f6cf3d64a907acde10c91a3d9`. The existing application remains under `app/`; its original [documentation](docs/README.md) and [licence](LICENSE) are retained.

The portfolio should distinguish inherited hardware support from original ThermalPilot work: the hub, command coordination, schedules, evidence-backed reporting, agents, MCP tools and measured experiments.

Use PowerShell 7.4+ and the pinned .NET 10.0.403 SDK. The fork removes upstream's process-stopping build hook and disables the inherited release/signing job. The baseline deliberately returns immediately; it is not a working controller. Do not switch to the inherited startup without the bootstrap and handover checkpoints.
