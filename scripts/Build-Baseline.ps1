#Requires -Version 7.4

[CmdletBinding()]
param(
    [string]$DotnetPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$expectedSdk = (Get-Content -LiteralPath (Join-Path $repoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version

if (-not $DotnetPath) {
    $localSdk = Join-Path $env:LOCALAPPDATA 'ThermalPilot\tools\dotnet\dotnet.exe'
    $DotnetPath = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
}

Push-Location $repoRoot
try {
    $actualSdk = & $DotnetPath --version
    if ($LASTEXITCODE -ne 0 -or "$actualSdk".Trim() -ne $expectedSdk) {
        throw "Expected .NET SDK $expectedSdk. Supply its dotnet.exe with -DotnetPath."
    }

    & $DotnetPath restore app/GHelper.csproj --locked-mode --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }

    & $DotnetPath build app/GHelper.csproj --configuration Release --runtime win-x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Baseline build failed.' }

    $buildDirectory = Join-Path $repoRoot 'app/bin/x64/Release/net10.0-windows/win-x64'
    & (Join-Path $PSScriptRoot 'Verify-Baseline.ps1') -AssemblyPath (Join-Path $buildDirectory 'ThermalPilot.Baseline.dll')

    & $DotnetPath publish app/GHelper.csproj --configuration Release --runtime win-x64 --self-contained false --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Baseline publish failed.' }

    & (Join-Path $PSScriptRoot 'Verify-Baseline.ps1') -AssemblyPath (Join-Path $buildDirectory 'publish/ThermalPilot.Baseline.dll')
}
finally {
    Pop-Location
}
