#Requires -Version 7.4

[CmdletBinding()]
param(
    [string]$DotnetPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $DotnetPath) {
    $localSdk = Join-Path $env:LOCALAPPDATA 'ThermalPilot/tools/dotnet/dotnet.exe'
    $DotnetPath = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
}
Push-Location $repoRoot
try {
    $expected = (Get-Content -LiteralPath global.json -Raw | ConvertFrom-Json).sdk.version
    $actual = & $DotnetPath --version
    if ($LASTEXITCODE -ne 0 -or "$actual".Trim() -ne $expected) { throw "Expected SDK $expected." }
    & $DotnetPath restore ThermalPilot.Hub.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Hub locked restore failed.' }
    & $DotnetPath build ThermalPilot.Hub.slnx --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Hub/controller build failed.' }
    & $DotnetPath test ThermalPilot.Hub.slnx --configuration Release --no-build --no-restore --logger trx --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Hub tests failed.' }
    & $DotnetPath publish hub/ThermalPilot.Host/ThermalPilot.Host.csproj --configuration Release --no-build --no-restore --output artifacts/hub
    if ($LASTEXITCODE -ne 0) { throw 'Hub publish failed.' }
    & $DotnetPath publish hub/ThermalPilot.Controller/ThermalPilot.Controller.csproj --configuration Release --no-build --no-restore --output artifacts/controller
    if ($LASTEXITCODE -ne 0) { throw 'Controller publish failed.' }
    & (Join-Path $PSScriptRoot 'Verify-LiveBridge.ps1') -DotnetPath $DotnetPath -HostAssembly (Join-Path $repoRoot 'artifacts/hub/ThermalPilot.Host.dll') -ControllerAssembly (Join-Path $repoRoot 'artifacts/controller/ThermalPilot.Controller.dll')
}
finally { Pop-Location }
