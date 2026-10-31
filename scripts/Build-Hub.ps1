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
    & $DotnetPath test ThermalPilot.Hub.slnx --configuration Release --no-restore --logger trx --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Hub tests failed.' }
    & $DotnetPath publish hub/ThermalPilot.Host/ThermalPilot.Host.csproj --configuration Release --no-build --no-restore --output artifacts/hub
    if ($LASTEXITCODE -ne 0) { throw 'Hub publish failed.' }
    & (Join-Path $PSScriptRoot 'Verify-HubPersistence.ps1') -DotnetPath $DotnetPath -HostAssembly (Join-Path $repoRoot 'artifacts/hub/ThermalPilot.Host.dll')
}
finally { Pop-Location }
