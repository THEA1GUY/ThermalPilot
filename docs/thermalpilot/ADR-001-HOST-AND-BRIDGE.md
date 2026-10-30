# ADR-001: Windows host, bridge and lifecycle

Date: 30 October 2026, Africa/Lagos.
Status: accepted direction for TP-00 following owner approval of the plan. Runtime feasibility remains gated in TP-02 and TP-04.

## Context and decision

Keep the G-Helper fork as the hardware backend. Add a per-user .NET 10 host with SQLite and an ASP.NET Core local API; a React/strict TypeScript dashboard serves desktop and paired phone clients. Codex MCP and both DeepSeek roles call the same application services.

Start the host and controller in the interactive owner's session after sign-in. Do not put the current WinForms/UI-bound backend into a session-0 Windows service. Do not promise schedules before sign-in, while asleep/off, or without the required bridge. Pre-login operation would need a separate design decision.

Transport between the host and controller will be a versioned Windows named pipe, restricted to the owner SID and required administrators. Network-facing API and any elevated hardware process remain separate. The host checks caller permissions; the controller separately checks capabilities, safe ranges and allowed typed operations. No raw ACPI IDs, arbitrary shell, arbitrary executable launch or mode_command forwarding.

Local same-user malicious software is outside the authentication guarantee of a user-owned named pipe. Pairing/authentication of remote clients is a separate boundary and must not be substituted with pipe permissions.

## Identities and coexistence

| Identity | TP-00 | Future runtime target |
|---|---|---|
| Executable | ThermalPilot.Baseline.exe, inert | ThermalPilot.Host.exe and ThermalPilot.Controller.exe |
| Entry point | ThermalPilot.BaselineEntryPoint.Main returns immediately | New explicit bootstrap; mode chosen before legacy initialization |
| Data | No application data created by baseline | %LOCALAPPDATA%/ThermalPilot; private DB, evidence and controller config separated |
| Singleton/event | None used by baseline | Owner-scoped Local/ThermalPilot.Controller identity; never Global/GHelperApp-Exit |
| Pipe | None implemented | ThermalPilot owner-scoped Bridge.v1 endpoint with explicit ACL/version handshake |
| Startup task | None created/changed | ThermalPilot-specific owner task; no takeover of GHelper tasks |
| Update channel | Inherited release job disabled for fork | Fork-owned verified update path before TP-12 release |

A future runtime must not search for and terminate same-name GHelper processes or signal upstream's exit event. It must not read/write the live GHelper config as its own config. Config import is an explicit validated copy into ThermalPilot storage, with shell hooks removed.

During telemetry-only validation, the owner's current G-Helper can remain the writer. Before enabling ThermalPilot writes, perform an explicit owner-reviewed handover with a saved baseline. Do not silently disable ASUS services, upstream startup tasks or drivers. If another controller becomes active later, suspend ThermalPilot automation and report the conflict; do not claim this prevents all writes by external software.

No executable was launched or runtime handover attempted in TP-00.

## Required read-only bootstrap extraction

Source inspection establishes that simply reusing Program.Main or HardwareControl.ReadSensors is not a read-only bootstrap:

- Program static fields reference AppConfig and Windows state; AppConfig initializes directories/config and fallback state.
- Program.Main creates UI/controllers, checks competing processes, initializes display/lighting and calls SetAutoModes.
- ModeControl construction can initialize SMU access.
- GPUModeControl.InitGPUMode can perform a GPU fix and lighting/config updates.
- Existing timer/visibility settings do not establish continuous background collection.

TP-02 must introduce a bootstrap with no reference to legacy Program state on its telemetry-only path. Extract the required read-only sampling boundary from upstream methods, injecting the ACPI reader, read-only configuration/model information and sensor providers explicitly. Do not copy the whole hardware stack or assume a static call is side-effect-free.

Begin with allowlisted read operations and native battery queries. Do not call settings application, DeviceInit/watchdog/calibration/SMU-module loading as discovery. Optional provider initialization must be separately checked for side effects and overhead.

Proof required: no settings/event/task/config writes during startup/sampling, no access to legacy control bootstrap, correct unavailable values, measured GPU wake impact, and successful disconnect/reconnect/sleep handling. TP-00 defines this boundary; it does not claim that proof has happened.

## Settings ownership and writer map

| Entry/source | Current code area | Required routing before TP-04 passes |
|---|---|---|
| Tray/UI and custom profiles | Settings.cs, Modes.cs, ModeControl.SetPerformanceMode | Controller coordinator; versioned command and manual ownership |
| Hotkeys | Input/InputDispatcher.cs and mode cycling | Same coordinator, source=manual/hotkey |
| AC/battery/charger/resume | Program.SetAutoModes, OnPowerSettled; Settings.WndProc | Explicit policy input; cannot override a newer manual lease silently |
| Reapply/reset timers | ModeControl reapply, AutoFans/AutoPower, sleep/shutdown reset | Ownership check and same ordered executor |
| GPU transitions | GPUModeControl and GPU backend | Serialized transition; explicit restart/unsupported/partial result |
| Charging/display/Windows power | BatteryControl, display helpers, PowerNative | Typed setting operations and consistent ownership |
| Lighting/calibration/advanced tools | USB/Aura, fan calibration, Pawn paths | Not remotely exposed by default; later scoped capability decisions |
| Hub/scheduler/agents | New modules | No direct device handles; submit typed operations |

The hardware operation executor belongs inside the controller. Use a single ordered queue for allowed write transitions, cancellation boundaries and readback, rather than waiting on a Task or adding a lock outside the existing writers. ModeControl currently mixes asynchronous fan/power work with synchronous Windows power changes, so wrapping its public call without refactoring is insufficient.

An active manual override outranks schedules/rules/AI within local policy and expires only as configured. Temporary activations restore only settings still owned by that activation. Partial hardware operations are not database transactions; report partial/unknown state and reconcile.

TP-04 must instrument/audit the affected write call sites and demonstrate UI/hotkey/power/timer races before enabling automation. Unsupported or uncoordinated writers block that gate.

## Persistence, disclosure and initial policy

Owner approval of TP-00 accepts the Windows/personal/SQLite stack, LAN-first paired-phone design and user-session lifecycle. No host/database/dashboard is implemented at this checkpoint.

- Current collection: disabled. Only targeted private inventory and build evidence exist.
- Initial measurement proposal for TP-01/02: temperature/fan samples about once per second on AC and slower on battery; expensive providers opt-in. Measure overhead before choosing the final rates.
- Initial bounded retention proposal: raw measurements up to 72 hours, minute aggregates 30 days and reports 90 days, with a 256 MiB telemetry budget and explicit disk-full behaviour. TP-01/02 must implement/verify it before enabling collection; the owner may adjust it in settings.
- Cloud: disabled with zero permitted spend until TP-08 configures the selected model, protected API key, explicit spending cap and inspected payload.
- Phone: no listener, certificate, firewall rule or device pairing created now. TP-03 settles encrypted onboarding and tests an actual phone.
- No automatic tuning/process termination/restart/undervolting/calibration is enabled by plan approval.

## Consequences and review gates

Reuse retains the tested upstream hardware knowledge, while narrow refactoring is necessary around initialization and coordination. Runtime feasibility is not proved by a successful build. If TP-02 cannot isolate read-only initialization without wider extraction, return a scoped change for review instead of silently expanding the phase.

TP-01 can implement fake-backed host/contracts/storage. It cannot enable hardware writes, live cloud agents or phone networking. TP-02 and TP-04 retain their real-device acceptance gates.
