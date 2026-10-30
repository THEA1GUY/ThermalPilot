# TP-00 evidence and limits

Date: 30 October 2026, Africa/Lagos.
Branch: codex/tp-00-baseline.
Scope: toolchain, inert baseline build, inventory and integration decisions.

## Preparation

- Owner approved starting TP-00 after reviewing the implementation plan.
- Dedicated destination selected: Documents/ChatGPT/ThermalPilot, separate from Quickone. Its own Git root and fork/upstream remotes are verified during checkout.
- Planning/source baseline remains 0a1538ac4fb0337f6cf3d64a907acde10c91a3d9; no upstream source refresh was silently applied.
- .NET SDK 10.0.403 archive downloaded from Microsoft, SHA-512 checked, extracted into per-user ThermalPilot tools. All three included runtime families report 10.0.14.
- System-wide SDK inventory still reports 9.0.301 after setup. No PATH/registry or driver changes.

## Read-only device inventory

Target system/CPU/GPU identity is recorded in SOURCE_AUDIT.md. Additional observations:

| Item | Observed |
|---|---|
| Running stock controller | G-Helper file version 0.288 |
| ASUS interface | ASUS System Control Interface v3; signed; driver version 3.1.72.0 |
| BIOS metadata | GA503RM.319 |
| Relevant services | Armoury Crate Control Interface and multiple ASUS/ROG services running; left unchanged |
| Startup task | GHelper task exists; read only |
| Optional driver inventory | No Win32_SystemDriver entry with name beginning PawnIO returned; not a comprehensive installation audit |

Private inventory includes the running controller process identity and a SHA-256 of its config, without reading config content into the report. Raw paths, process IDs, timestamps and config checksum remain outside Git.

After baseline compilation, the same controller process/start time remained active and its config checksum matched the before snapshot. This supports no observed controller interruption/config change; it is not sensor or hardware acceptance.

## Checks performed

| Check | Result |
|---|---|
| Exact local SDK selection | 10.0.403 |
| Initial restore and dependency lock | Passed; seven direct and one transitive app packages |
| Locked restore | Passed |
| Release/win-x64 build | Passed, zero warnings and zero errors |
| Framework-dependent publish | Passed |
| Build output entry-point verification | Passed; empty ThermalPilot.BaselineEntryPoint.Main |
| Publish output entry-point verification | Passed |
| Negative verifier check | Native SDK dotnet.exe rejected as not a managed baseline assembly; not executed by the verifier |
| C# formatting | dotnet format restricted to app/BaselineEntryPoint.cs, verify-no-changes: passed |
| PowerShell syntax, project/source XML and SDK JSON | Passed |
| Live controller/config/system SDK comparison | Passed, unchanged |
| Focused build/scripts and C# reviews | No actionable findings |
| Relative doc links/whitespace | Checked before commit |

Repeated the locked restore, Release/win-x64 build, publish and both metadata checks from the permanent dedicated checkout: passed, zero warnings/errors. The live controller/config and relevant service states still matched the before snapshot afterward.

[Remote GitHub Actions](https://github.com/THEA1GUY/ThermalPilot/actions/runs/40988050307) passed for source commit 7bee316c5258df22e36ee47c9863b512d04a48bc. See [BUILD_STATUS](BUILD_STATUS.md) and [draft PR #2](https://github.com/THEA1GUY/ThermalPilot/pull/2) for the review/resume point. Final documentation-only commits do not change the reviewed baseline code.

## Design evidence

ADR-001 maps the current startup side effects, UI coupling, static configuration initialization, asynchronous mode changes and internal writers. It defines owner-scoped identities, a typed pipe boundary and the required read-only extraction/coordination gates.

This is static design evidence. No bridge, SQLite host, scheduler, UI, MCP server or DeepSeek runner was added in TP-00. Do not confuse the inert entry point with a telemetry service.

## Not exercised

No application executable was launched. No ACPI sensor sample, temperature benchmark, driver/module loading, calibration, fan/power/profile change, service/task change, phone listener, cloud request or optimisation experiment occurred. Hardware capabilities and performance remain unverified.

Public release is unavailable: dependency notice follow-up and the fork update/signing path remain release gates.
