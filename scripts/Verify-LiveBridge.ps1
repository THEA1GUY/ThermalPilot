#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DotnetPath,
    [Parameter(Mandatory)][string]$HostAssembly,
    [Parameter(Mandatory)][string]$ControllerAssembly,
    [switch]$RequireAsusSensors
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$runDirectory = Join-Path $repoRoot ('.private/live-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$token = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$headers = @{ Authorization = 'Bearer ' + $token }
$controllerChild = $null
$hostChild = $null
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$probe.Start()
$port = $probe.LocalEndpoint.Port
$probe.Stop()
$base = "http://127.0.0.1:$port"
$dataDirectory = Join-Path $runDirectory 'data'
$pipeName = 'ThermalPilot.Bridge.v1.' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value + '.' + [Diagnostics.Process]::GetCurrentProcess().SessionId

function Start-ControllerChild {
    param([string]$Label)
    Start-Process -FilePath $DotnetPath -ArgumentList @(('"{0}"' -f $ControllerAssembly)) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runDirectory "$Label.out.log") -RedirectStandardError (Join-Path $runDirectory "$Label.err.log")
}

function Start-HostChild {
    param([string]$Directory, [string]$Label)
    Start-Process -FilePath $DotnetPath -ArgumentList @(('"{0}"' -f $HostAssembly), 'serve', '--data-dir', ('"{0}"' -f $Directory), '--port', "$port", '--collect', 'true') -WindowStyle Hidden -PassThru -Environment @{THERMALPILOT_API_TOKEN=$token; DOTNET_CLI_TELEMETRY_OPTOUT='1'} -RedirectStandardOutput (Join-Path $runDirectory "$Label.out.log") -RedirectStandardError (Join-Path $runDirectory "$Label.err.log")
}

function Get-LiveSample {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(25)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($hostChild.HasExited) { throw 'Owned host exited; inspect private logs.' }
        try {
            $snapshot = Invoke-RestMethod -Uri ($base + '/v1/telemetry') -Headers $headers -NoProxy -TimeoutSec 2
            if (-not $snapshot.isSynthetic -and $snapshot.adapter -eq 'readOnlyBridge' -and @($snapshot.sensors | Where-Object quality -eq 'fresh').Count -gt 0) { return $snapshot }
        } catch [Microsoft.PowerShell.Commands.HttpResponseException] { }
          catch [System.Net.Http.HttpRequestException] { }
        Start-Sleep -Milliseconds 200
    }
    throw 'No actual fresh Windows telemetry arrived.'
}

try {
    # Refuse to use or stop an existing controller. This script owns every process it terminates.
    $existing = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::InOut)
    $found = $false
    try { $existing.Connect(200); $found = $true } catch [TimeoutException] { } finally { $existing.Dispose() }
    if ($found) { throw 'A controller already exists; leave it running and perform acceptance separately.' }

    $controllerChild = Start-ControllerChild 'controller-first'
    $hostChild = Start-HostChild $dataDirectory 'host-first'
    $first = Get-LiveSample
    if ($controllerChild.HasExited) { throw 'Owned controller failed to start.' }
    if ($RequireAsusSensors -and -not ($first.sensors | Where-Object { $_.id -eq 'cpu.temperature' -and $_.quality -eq 'fresh' })) { throw 'Real ASUS CPU temperature required but unavailable.' }
    $first | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'actual-first.private.json')

    # Exercise the real server with an invalid frame, then require a valid response.
    $malformed = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::InOut)
    try {
        $malformed.Connect(5000)
        $bytes = [BitConverter]::GetBytes([int]32769)
        $malformed.Write($bytes, 0, 4)
        $malformed.Flush()
    } finally { $malformed.Dispose() }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(25)
    do {
        $afterMalformed = Get-LiveSample
        if ($afterMalformed.sequence -gt $first.sequence) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($controllerChild.HasExited -or $afterMalformed.sequence -le $first.sequence -or $afterMalformed.sessionId -ne $first.sessionId) { throw 'Malformed frame interrupted the real controller.' }
    $session = $first.sessionId
    $controllerChild.Kill()
    if (-not $controllerChild.WaitForExit(10000)) { throw 'Owned controller did not stop.' }
    $controllerChild.Dispose()
    $controllerChild = $null
    $staleDeadline = [DateTimeOffset]::UtcNow.AddSeconds(95)
    do {
        Start-Sleep -Seconds 2
        $stale = Invoke-RestMethod -Uri ($base + '/v1/telemetry') -Headers $headers -NoProxy
        if (@($stale.sensors | Where-Object quality -eq 'fresh').Count -eq 0) { break }
    } while ([DateTimeOffset]::UtcNow -lt $staleDeadline)
    if (@($stale.sensors | Where-Object quality -eq 'fresh').Count -ne 0) { throw 'Disconnected bridge advertised fresh readings.' }

    $controllerChild = Start-ControllerChild 'controller-second'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(25)
    do {
        $reconnected = Get-LiveSample
        if ($reconnected.sessionId -ne $session) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($reconnected.sessionId -eq $session) { throw 'Controller restart did not reconnect.' }
    $summary = Invoke-RestMethod -Uri ($base + '/v1/telemetry/summary') -Headers $headers -NoProxy
    if ($summary.gaps -lt 1) { throw 'Controller interruption was not recorded.' }

    $hostChild.Kill()
    if (-not $hostChild.WaitForExit(10000)) { throw 'Owned host did not stop.' }
    $hostChild.Dispose()
    $hostChild = $null
    $backup = Join-Path $runDirectory 'backup.db'
    $restoredDirectory = Join-Path $runDirectory 'restored'
    & $DotnetPath $HostAssembly backup --data-dir $dataDirectory --destination $backup
    if ($LASTEXITCODE -ne 0) { throw 'Actual-telemetry backup failed.' }
    & $DotnetPath $HostAssembly restore --data-dir $restoredDirectory --source $backup
    if ($LASTEXITCODE -ne 0) { throw 'Actual-telemetry restore failed.' }
    $hostChild = Start-HostChild $restoredDirectory 'host-restored'
    $null = Get-LiveSample
    $history = Invoke-RestMethod -Uri ($base + '/v1/telemetry/history') -Headers $headers -NoProxy
    if (-not ($history | Where-Object { $_.sessionId -eq $session -and $_.sequence -eq $first.sequence })) { throw 'The original actual reading did not survive recovery.' }
    if ($history | Where-Object isSynthetic) { throw 'Synthetic telemetry found.' }
    [pscustomobject]@{ActualWindowsSensors='PASS';AsusRequired=[bool]$RequireAsusSensors;MalformedFrame='PASS';DisconnectFreshness='PASS';Reconnect='PASS';RecordedGap='PASS';ActualTelemetryBackupRestore='PASS';EvidenceDirectory=$runDirectory}
}
finally {
    foreach ($owned in @($hostChild, $controllerChild)) {
        if ($null -ne $owned) {
            if (-not $owned.HasExited) { $owned.Kill(); [void]$owned.WaitForExit(10000) }
            $owned.Dispose()
        }
    }
}
