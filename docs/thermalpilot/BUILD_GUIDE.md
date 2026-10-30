# Baseline build guide

TP-00, 30 October 2026. This builds the inherited application code with an inert ThermalPilot entry point. It is not a functional ThermalPilot controller.

## Requirements

- Windows x64.
- PowerShell 7.4 or later (run pwsh, not Windows PowerShell 5.1).
- Exact .NET SDK 10.0.403; root global.json disables SDK roll-forward/prerelease selection.
- Access to the public NuGet source on first restore. The package graph and hashes are in app/packages.lock.json.
- A dedicated ThermalPilot checkout, with origin set to THEA1GUY/ThermalPilot and upstream set to seerge/g-helper.

The local preparation installed Microsoft's SDK archive into %LOCALAPPDATA%/ThermalPilot/tools/dotnet. It did not change system PATH/registry, remove .NET 9, install a driver or trust a development certificate. The SDK must be kept patched deliberately; update the pin and lockfile together after validation.

SDK source: [Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json).
Archive: dotnet-sdk-10.0.403-win-x64.zip.
SHA-512 verified before extraction:

```text
7a114c318fa7f1d9822991b534b2814d0e78386ceda816b371f5141acefe741d87ccb606ede06cadac8f9ac5070e32b708a67bbd1619ff5e5a7858a27874ef48
```

## Build and inspect without launching

Open pwsh in the repository root:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_HOME = Join-Path $env:LOCALAPPDATA 'ThermalPilot/tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $env:LOCALAPPDATA 'ThermalPilot/tools/nuget-packages'
./scripts/Build-Baseline.ps1
```

The helper prefers the ThermalPilot per-user SDK when present; otherwise it uses dotnet from PATH. Override explicitly with -DotnetPath if needed. It checks the exact SDK, performs locked restore, builds Release/win-x64, verifies entry-point metadata, publishes framework-dependent output and verifies that output too.

Equivalent CLI operations:

```powershell
$taskDotnet = Join-Path $env:LOCALAPPDATA 'ThermalPilot/tools/dotnet/dotnet.exe'
& $taskDotnet restore app/GHelper.csproj --locked-mode --runtime win-x64
& $taskDotnet build app/GHelper.csproj --configuration Release --runtime win-x64 --no-restore
& $taskDotnet publish app/GHelper.csproj --configuration Release --runtime win-x64 --self-contained false --no-build --no-restore
./scripts/Verify-Baseline.ps1 -AssemblyPath app/bin/x64/Release/net10.0-windows/win-x64/publish/ThermalPilot.Baseline.dll
```

The verifier parses PE/metadata/IL. It does not load or execute the target assembly. It rejects the wrong assembly identity, the wrong entry point, module/entry-type initializers and entry-point IL other than nop/ret.

## Outputs

- Build: app/bin/x64/Release/net10.0-windows/win-x64/ThermalPilot.Baseline.dll and .exe.
- Publish: the same directory's publish/ subtree.
- The .exe is a framework-dependent apphost. The .dll contains the managed entry point.
- Original app/Program.cs remains compiled, but is not selected as the baseline startup.
- Baseline startup returns without controller/UI/config/driver initialization. It is intentionally not a working app.
- No GHelper stop hook or automatic ZIP command remains in the project.
- The inherited release/signing workflow is disabled for this fork pending TP-12 packaging and update work.
- No downloaded packages, binaries, private logs or raw inventory are committed.

CI installs the pinned SDK from global.json and invokes the same build helper on Windows. A clean checkout uses the committed package lock.

## Package updates and rollback

Ordinary builds use --locked-mode and must not rewrite the lockfile. An intentional dependency change requires an explicit unlocked restore, inspection of app/packages.lock.json and revalidation. Do not solve an SDK error by silently rolling forward.

To roll back this preparation, revert the TP-00 commit after review. Keep the owner's existing G-Helper installation/config/startup task unchanged. Tooling resides in ThermalPilot's per-user tools folder; do not recursively remove it or caches while another task is using them.

Changing StartupObject back to GHelper.Program is a runtime-control change, not a harmless build fix. Do not do it without the TP-02/04 bootstrap and handover gates.

References: [SDK selection](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json), [Windows .NET installation](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [PackageReference lockfiles](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files).
