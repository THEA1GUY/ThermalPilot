# G-Helper source audit for ThermalPilot

Date: 30 October 2026, Africa/Lagos.

## Identity and evidence boundary

- Upstream: https://github.com/seerge/g-helper
- Fork: https://github.com/THEA1GUY/ThermalPilot
- Baseline: `0a1538ac4fb0337f6cf3d64a907acde10c91a3d9`, upstream commit dated 26 October 2026.
- App project version in the inspected csproj: 0.290. This is source metadata, not verification of the version running on the PC.
- Local reference checkout was downloaded with `git clone --depth 1`; the GitHub fork preserves the upstream repository history. Fetch additional local history if needed for a later comparison.
- Inspection: targeted source reads, filename/symbol searches, workflow inspection and read-only hardware inventory. No decompilation was needed.
- No application was built/launched, no sensors were sampled, no driver was installed, and no hardware settings were changed.

The model, CPU and GPU were read using Windows CIM without collecting serial numbers:

| Component | Observed |
|---|---|
| System | ASUSTeK COMPUTER INC.; ROG Zephyrus G15 GA503RM_GA503RM |
| CPU | AMD Ryzen 9 6900HS with Radeon Graphics |
| Dedicated GPU | NVIDIA GeForce RTX 3060 Laptop GPU |
| Integrated GPU | AMD Radeon Graphics |
| Installed SDK | .NET SDK 9.0.301 |
| Node | v24.16.0 |

This identifies the target; it does not prove which controls work or what temperatures are appropriate. Current BIOS, ASUS interface/driver health, installed G-Helper version, active services and actual sensor capabilities remain to be checked.

## Source map

Links below are pinned to the reviewed commit.

| Area | Evidence | Consequence |
|---|---|---|
| Build | [GHelper.csproj](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/GHelper.csproj), lines 5-17 and 83 onward | .NET 10 Windows Forms, x64. The installed .NET 9 SDK is not sufficient for this target. The pre-build target signals Global\\GHelperApp-Exit and waits for GHelper to exit. Build preparation must isolate this behaviour. |
| Build CI | [.github/workflows/build.yml](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/.github/workflows/build.yml) | Windows workflow installs .NET 10 and builds app/GHelper.sln. Reuse the relevant baseline; add hub/tests/frontend checks separately. |
| App entry/lifecycle | [Program.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/Program.cs), lines 46 onward, 182 onward and 298 onward | Static application state and startup/power automation. CLI actions in the inspected switch mostly open UI or perform special startup actions; this is not an existing complete profiles/scheduling API. |
| Performance | [ModeControl.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/Mode/ModeControl.cs), lines 8-50 and 135 onward | SettingsForm/Program coupling; asynchronous mode application, cancellation, fan/power application and reapply timers. A hub cannot safely treat this as a standalone headless library. |
| Custom profiles | [Modes.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/Mode/Modes.cs), lines 5-38 and 76 onward | Existing custom-mode definitions and cloning. Reuse semantics, but inspect which fields the clone skips rather than assuming every tuning field is copied. |
| ACPI boundary | [AsusACPI.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/AsusACPI.cs), lines 384-503 and 529 onward | Existing DeviceIoControl, DeviceGet/Set, fan curves and support probing. Control does not check the native result in the inspected implementation; bridge outcomes need explicit failure detection and carefully defined readback. Do not expose raw endpoints. |
| Telemetry | [HardwareControl.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/HardwareControl.cs), lines 834-960 | Existing sensor paths and conditional overlay reads. Regular and overlay reads cover different fields. Some readings are absent/cached/sentinel values. Need one validated sampling path independent of UI visibility. |
| UI sampling | [Settings.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/Settings.cs), lines 250, 715 and 1641 onward | Timer behaviour can depend on UI visibility/settings. Do not assume continuous history just by leaving upstream running. |
| Fan model handling | [FanSensorControl.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/Fan/FanSensorControl.cs), line 58 | GA503-specific default fan limits exist. They are upstream defaults, not measurements of this machine. Calibration is a state-changing operation and is not part of discovery. |
| Configuration | [AppConfig.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/AppConfig.cs), lines 27-43 and 85-108 | JSON config, fallback paths, backup/recovery and atomic replacement. Do not edit live config behind the running app as an integration. Keep hub history in a separate database. |
| Logging | [Logger.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/Helpers/Logger.cs), lines 12-41 | Text logs with swallowed I/O errors and trimming. Useful existing diagnostic style, but not a durable command/schedule/report audit ledger. |
| Process ownership | [ProcessHelper.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/Helpers/ProcessHelper.cs), lines 8-100 | Global exit event plus same-name process termination. A development or forked launch can replace another controller. Choose explicit identities and migration/coexistence rules before runtime testing. |
| GPU switching | [GPUModeControl.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/Gpu/GPUModeControl.cs), lines 80 onward | Some transitions are asynchronous or require dialogs/restart. Do not promise seamless or unattended completion. |
| Command hooks | [ModeControl.cs](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/app/Mode/ModeControl.cs), within SetPerformanceMode | mode_command can execute a configured command. Agent-created/imported profiles must not acquire this capability. |
| Licence | [LICENSE](https://github.com/seerge/g-helper/blob/0a1538ac4fb0337f6cf3d64a907acde10c91a3d9/LICENSE) | Repository contains GNU GPL version 3 text. Preserve it and existing notices; inventory dependency notices before distribution. |

## Bounded negative findings

- Searches for NamedPipe, TcpListener, HttpListener, WebApplication, McpServer and JsonRpc in inspected app source/project/workflows did not reveal a reusable hub/MCP server. This is a bounded search, not proof that every communication mechanism is absent.
- Filename searches for test/spec projects did not identify an automated test project in the checked-out tree. The inspected build workflow builds the solution; it does not establish behavioural coverage.
- No standalone public profile/schedule service was established by the inspected entry points.

## Reuse decision

Preferred candidate: preserve the existing hardware backend in a narrowly modified G-Helper fork, add typed local IPC, and build a separate hub host/dashboard in the same repository. Do not initially rewrite drivers, copy only AsusACPI into a new service, or automate the UI with keystrokes.

The first spike must settle user-session versus service boundaries and prove coordinated ownership across existing G-Helper UI actions, hotkeys, automatic events, reapply timers and hub requests. It must also prove that a telemetry-only mode does not execute startup writes.

If a narrow bridge cannot meet those requirements, stop the checkpoint and document the smallest required extraction/refactor before expanding scope.

## Pattern guidance

- Keep upstream naming and conventions in touched C# modules to keep the fork diff reviewable.
- Use typed contracts and explicit operation results in the new hub/bridge boundary. Do not replicate silent catches.
- Preserve existing config recovery; use SQLite transactions/migrations for new persistent hub data.
- Use structured local events with correlation IDs for new commands/reports. Never rely on trimmed text logs as the source of truth.
- Establish contract/persistence/scheduler tests in new test projects; no upstream test convention was found to mirror.
- Test hardware integration separately from deterministic fake-adapter tests.

## Official integration references

- [Codex MCP documentation](https://learn.chatgpt.com/docs/extend/mcp): local MCP configuration and transports. Actual connection/tool availability still needs verification in this installation.
- [DeepSeek tool calls](https://api-docs.deepseek.com/guides/tool_calls/): model requests functions; the host executes them. A cloud model is not a local scheduler or background process.

No DeepSeek model identifier, price, spending budget or credential was selected or verified during planning.
