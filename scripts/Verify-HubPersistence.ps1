#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DotnetPath,
    [Parameter(Mandatory)][string]$HostAssembly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$DotnetPath = (Resolve-Path -LiteralPath $DotnetPath).Path
$HostAssembly = (Resolve-Path -LiteralPath $HostAssembly).Path
$runDirectory = Join-Path $repoRoot ('.private/persistence-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$token = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$headers = @{ Authorization = 'Bearer ' + $token }
$child = $null

function Start-SyntheticHost {
    param([string]$DataDirectory, [string]$Label)
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $probe.Start()
    $port = $probe.LocalEndpoint.Port
    $probe.Stop()
    $arguments = @(('"{0}"' -f $HostAssembly), 'serve', '--data-dir', ('"{0}"' -f $DataDirectory), '--port', "$port")
    $process = Start-Process -FilePath $DotnetPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Environment @{
        THERMALPILOT_API_TOKEN = $token
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    } -RedirectStandardOutput (Join-Path $runDirectory ($Label + '.stdout.log')) -RedirectStandardError (Join-Path $runDirectory ($Label + '.stderr.log'))
    $base = "http://127.0.0.1:$port"
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    try {
      while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw 'Synthetic host exited during startup; inspect the private run logs.' }
        try {
            $status = Invoke-RestMethod -Uri ($base + '/health/ready') -Headers $headers -NoProxy -TimeoutSec 1
            if ($status.status -eq 'ready' -and $status.adapter -eq 'fake') {
                return @{ Process = $process; Base = $base }
            }
        }
        catch [System.Net.Http.HttpRequestException] { }
        catch [System.Threading.Tasks.TaskCanceledException] { }
        Start-Sleep -Milliseconds 100
      }
      throw 'Synthetic host readiness timed out.'
    }
    catch {
      Stop-OwnedHost $process
      throw
    }
}

function Stop-OwnedHost {
    param([Diagnostics.Process]$Process)
    try {
        if (-not $Process.HasExited) {
            $Process.Kill()
            if (-not $Process.WaitForExit(5000)) { throw 'The owned synthetic host did not exit.' }
        }
    }
    finally { $Process.Dispose() }
}

function Send-Json {
    param([string]$Base, [string]$Path, [object]$Body)
    Invoke-RestMethod -Uri ($Base + $Path) -Method Post -Headers $headers -NoProxy -ContentType application/json -Body ($Body | ConvertTo-Json -Depth 5)
}

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

try {
    $dataDirectory = Join-Path $runDirectory 'original'
    $first = Start-SyntheticHost -DataDirectory $dataDirectory -Label first
    $child = $first.Process
    $profile = Send-Json $first.Base '/v1/profiles' @{ name = 'Synthetic Docker profile'; baseMode = 'balanced' }
    $request = @{
        profileId = $profile.id
        expectedProfileVersion = $profile.version
        idempotencyKey = [guid]::NewGuid().ToString('N')
        correlationId = [guid]::NewGuid().ToString('D')
        expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(5).ToString('O')
    }
    $command = Send-Json $first.Base '/v1/commands/simulate' $request
    $report = Send-Json $first.Base '/v1/reports/synthetic' @{ title = 'Synthetic restart report'; body = 'Fixture only; no device observations.' }
    Assert-Condition ($command.command.outcome -eq 'simulated' -and $command.command.isSynthetic -and -not $command.command.hardwareChanged) 'The command was not explicitly a simulation.'
    Stop-OwnedHost $child
    $child = $null

    $second = Start-SyntheticHost -DataDirectory $dataDirectory -Label restarted
    $child = $second.Process
    $savedProfile = Invoke-RestMethod -Uri ($second.Base + '/v1/profiles/' + $profile.id) -Headers $headers -NoProxy
    $savedReport = Invoke-RestMethod -Uri ($second.Base + '/v1/reports/' + $report.id) -Headers $headers -NoProxy
    $replayed = Send-Json $second.Base '/v1/commands/simulate' $request
    Assert-Condition ($savedProfile.name -eq $profile.name -and $savedReport.body -eq $report.body) 'Records did not survive process restart.'
    Assert-Condition ($replayed.replayed -and $replayed.command.id -eq $command.command.id) 'Restart created a duplicate command.'
    Stop-OwnedHost $child
    $child = $null

    $backup = Join-Path $runDirectory 'backup.db'
    & $DotnetPath $HostAssembly backup --data-dir $dataDirectory --destination $backup
    if ($LASTEXITCODE -ne 0) { throw 'Offline backup failed.' }
    $restoredDirectory = Join-Path $runDirectory 'restored'
    & $DotnetPath $HostAssembly restore --data-dir $restoredDirectory --source $backup
    if ($LASTEXITCODE -ne 0) { throw 'Restore to a fresh directory failed.' }

    $third = Start-SyntheticHost -DataDirectory $restoredDirectory -Label restored
    $child = $third.Process
    $restoredProfile = Invoke-RestMethod -Uri ($third.Base + '/v1/profiles/' + $profile.id) -Headers $headers -NoProxy
    $restoredReport = Invoke-RestMethod -Uri ($third.Base + '/v1/reports/' + $report.id) -Headers $headers -NoProxy
    $restoredCommand = Send-Json $third.Base '/v1/commands/simulate' $request
    $events = Invoke-RestMethod -Uri ($third.Base + '/v1/events') -Headers $headers -NoProxy
    Assert-Condition ($restoredProfile.id -eq $profile.id -and $restoredReport.id -eq $report.id) 'Restore changed record identities.'
    Assert-Condition ($restoredCommand.replayed -and $restoredCommand.command.id -eq $command.command.id -and $events.Count -eq 3) 'Restore lost command idempotency or audit events.'
    Stop-OwnedHost $child
    $child = $null
    [pscustomobject]@{ Mode = 'fake'; Restart = 'PASS'; BackupRestore = 'PASS'; Idempotency = 'PASS'; Profiles = 1; Commands = 1; Reports = 1; Events = 3 }
}
finally {
    if ($child) { Stop-OwnedHost $child }
}
