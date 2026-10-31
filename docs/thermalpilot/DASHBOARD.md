# Dashboard and Android pairing

Updated: 31 October 2026, Africa/Lagos.

TP-03 adds a React dashboard served by the local ASP.NET host. It reads actual live telemetry, persisted samples, health, policy, existing reports/events and saved incident notes. Unavailable/stale quality remains visible; synthetic snapshots and reports are filtered from the dashboard. No external fonts, analytics, CDN or cloud service is used. Browser requests use same-origin bearer authentication, `no-store` responses and a restrictive content policy.

Desktop mode remains the default: HTTP binds to loopback only at port 5127. It accepts the owner token from `THERMALPILOT_API_TOKEN`; browser tokens stay in memory and are discarded on reload. Owners can turn real host collection on or off from Preferences. That setting is saved in SQLite and audited; while off, the bridge is not polled. The dashboard is not required for collection. Browser polling and selected history range do not change the sensor collection schedule.

## Enable Android access

Phone access starts disabled. Choose the active Wi-Fi IPv4 address of the PC with `ipconfig`, then generate a private local CA certificate and a server certificate whose subject names include that IP:

```powershell
./scripts/New-PhoneTlsCertificate.ps1 -LanIPv4 192.168.1.23
```

The script writes its files below `%LOCALAPPDATA%/ThermalPilot/phone-certificate`; the PFX stays on the PC and is protected by a password you enter. Transfer only `thermalpilot-phone-root.cer` to your Android phone using USB or another private method. In Android Settings, search for **Install a certificate** and install it as a **CA certificate**. Android versions and vendors use different menu names. Confirm that the certificate subject is `ThermalPilot Local Phone CA`. Do not install a certificate sent by an unknown person.

Start the host in a PowerShell window. The HTTPS password is read from the process environment and is never written to repo configuration. Replace the example data paths as needed:

```powershell
$taskDotnet = Join-Path $env:LOCALAPPDATA 'ThermalPilot/tools/dotnet/dotnet.exe'
$env:THERMALPILOT_API_TOKEN = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$secureTlsPassword = Read-Host 'PFX password' -AsSecureString
$tlsPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureTlsPassword)
try { $env:THERMALPILOT_TLS_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tlsPointer) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tlsPointer) }
& $taskDotnet ./artifacts/controller/ThermalPilot.Controller.dll
```

In a second PowerShell window, set the same owner token and TLS password, then run:

```powershell
$env:THERMALPILOT_API_TOKEN = '<same owner token>'
$secureTlsPassword = Read-Host 'PFX password' -AsSecureString
$tlsPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureTlsPassword)
try { $env:THERMALPILOT_TLS_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tlsPointer) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tlsPointer) }
& $taskDotnet ./artifacts/hub/ThermalPilot.Host.dll serve --collect true --lan true --tls-pfx "$env:LOCALAPPDATA/ThermalPilot/phone-certificate/thermalpilot-phone-server.pfx" --phone-port 5128
```

The first command above starts the controller; start it only once. The host keeps the desktop page on loopback port 5127 and adds HTTPS on port 5128 for private LAN clients. It does not create a Windows Firewall rule. If Windows blocks the phone, an administrator can allow only the Private profile and local subnet:

```powershell
New-NetFirewallRule -DisplayName 'ThermalPilot Android dashboard' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5128 -Profile Private -RemoteAddress LocalSubnet
```

Open `http://localhost:5127`, sign in with the owner token, and create a one-use pairing QR. The QR expires after five minutes and is consumed on first use. Choose the PC address that belongs to the phone's Wi-Fi if Windows reports more than one private address. Scan the QR with the Android camera. The URL keeps the code in its fragment so the browser does not send it in the initial HTTP request or the `Referer` header.

Each paired phone gets a random 256-bit token. The host stores only its SHA-256 hash in SQLite. Paired sessions can read the dashboard and create incident notes; profile, pairing and other mutation routes stay owner-only. The owner dashboard can revoke a phone, and the host rejects the revoked token on its next request. Pairing codes are in-memory, one-use and lost when the host stops. The phone token remains active across host restarts until revoked.

The phone link uses the current private IPv4 address in the certificate. If DHCP changes that address, generate a replacement certificate for the new address and reinstall its root certificate on Android. The host does not claim installability; the phone UI is an HTTPS browser page.

## TP-03 owner acceptance

The source and automated checks prove actual-only rendering, representative history across bounded periods, same-origin restrictions, TLS-only LAN ingress, short-lived one-use pairing codes, hashed phone tokens, read-only phone scope and immediate revocation. TP-03 is not complete until the owner installs the CA and scans the QR from the actual Android phone, reads live or explicitly unavailable sensor values, confirms persisted history after a host restart, and verifies a revoked phone is rejected. Keyboard and touch checks must also be recorded on the devices used.

Hardware writes, profile activation, schedule execution and cloud calls remain disabled in this checkpoint. No runtime telemetry goes to a cloud service.
