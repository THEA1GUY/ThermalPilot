# ThermalPilot project instructions

## Product and current status

ThermalPilot is a personal Windows PC hub built from seerge/g-helper, with a desktop/phone dashboard, persistent local history, profiles, schedules, Codex MCP, and two DeepSeek cloud-agent roles. The agent roles are runtime product features, not an instruction to delegate repository work.

Read README.md and docs/thermalpilot/{IMPLEMENTATION_PLAN,SOURCE_AUDIT,BUILD_STATUS}.md before implementation. The current phase is TP-00 preparation; no ThermalPilot application features have been implemented.

## Scope and ownership

- This project has its own repository, THEA1GUY/ThermalPilot. It is separate from Quickone/unimeal. Never modify that project as part of this work.
- Windows host, personal use and phone access are the design basis. SQLite, the precise stack, LAN-first access, retention, spending limits and unattended action bounds remain proposals until the plan is approved.
- Preserve upstream source/history, LICENSE and notices. Do not represent inherited G-Helper functionality as new ThermalPilot work.
- Do not silently rename the upstream executable, application-data directory or global process event. Specify migration and coexistence behaviour first.
- Use PowerShell on Windows; npx.cmd if needed. No Temp-directory cleanup during a task.
- No credentials, raw personal telemetry, machine identifiers or secret-bearing logs in Git.

## Execution

- State a short plan before editing. Implement only the approved checkpoint.
- Each completed checkpoint needs relevant checks, persisted-behaviour evidence, updated BUILD_STATUS, an owner-authored commit on a codex/ branch pushed to origin, and owner review before continuing.
- Verify git rev-parse --show-toplevel is this checkout before every commit. Never commit to a parent repository or push changes to upstream. Never force push.
- No AI attribution or coauthor trailers in commits, PRs or GitHub content.
- Before building, inspect GHelper.csproj: its KillRunningGHelper target can close the user's active controller. Isolate that development behaviour before the first build; do not set a global CI environment variable to suppress unrelated behaviour.
- Do not run upstream Program.Main as a read-only sensor probe: startup can apply settings and affect other controllers.
- Keep one hardware-writing authority. Route G-Helper UI, hotkeys, reapply timers, automatic events, hub, scheduler and agents through coordinated ownership. An external lock alone does not coordinate existing internal writers.
- Typed commands only for AI and remote controls. No arbitrary shell or raw ACPI endpoints. Reject custom mode_command fields in agent-created/imported hub profiles.
- Distinguish requested, accepted, applied, verified, failed, partial and unknown outcomes. An API acknowledgement is not hardware verification.
- Never promise rollback of hardware settings is atomic. Show and recover from partial failures.
- No automatic process termination, reboot, firmware update, calibration or undervolting. Any later support requires an explicit scoped decision.
- Sensors need units, source, timestamp and quality. Unsupported/stale data is not zero. Reports distinguish observations, hypotheses and unknowns.
- Continuous monitoring and schedules run locally while the PC/required host processes are available. Cloud models and an open Codex conversation are not thermal safety dependencies.
