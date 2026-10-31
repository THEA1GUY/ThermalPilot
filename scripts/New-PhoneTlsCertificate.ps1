#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$LanIPv4
)

$ErrorActionPreference = 'Stop'
$directory = Join-Path $env:LOCALAPPDATA 'ThermalPilot/phone-certificate'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$pfxPath = Join-Path $directory 'thermalpilot-phone-server.pfx'
$caPath = Join-Path $directory 'thermalpilot-phone-root.cer'
if ((Test-Path -LiteralPath $pfxPath) -or (Test-Path -LiteralPath $caPath)) {
    throw "Certificate outputs already exist in $directory. Move or remove them explicitly before generating a replacement."
}

$addresses = @([System.Net.IPAddress]::Loopback) + @(
    foreach ($text in $LanIPv4) {
        $address = $null
        if (-not [System.Net.IPAddress]::TryParse($text, [ref]$address) -or
            $address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
            throw "Provide an IPv4 address: $text"
        }
        $octets = $address.GetAddressBytes()
        $private = $octets[0] -eq 10 -or ($octets[0] -eq 172 -and $octets[1] -ge 16 -and $octets[1] -le 31) -or ($octets[0] -eq 192 -and $octets[1] -eq 168)
        if (-not $private -or [System.Net.IPAddress]::IsLoopback($address)) { throw "Use a private LAN IPv4 address: $text" }
        $address
    }
)

$now = [DateTimeOffset]::UtcNow
$caKey = [System.Security.Cryptography.RSA]::Create(3072)
$serverKey = [System.Security.Cryptography.RSA]::Create(3072)
$caCertificate = $null
$serverCertificate = $null
$serverWithKey = $null
$securePassword = $null
$passwordPointer = [IntPtr]::Zero
try {
    $caRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=ThermalPilot Local Phone CA', $caKey, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $caRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
    $caRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign -bor [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::CrlSign, $true))
    $caRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($caRequest.PublicKey, $false))
    $caCertificate = $caRequest.CreateSelfSigned($now.AddMinutes(-5), $now.AddYears(2))

    $serverRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=ThermalPilot Phone Dashboard', $serverKey, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $serverRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $serverRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment, $true))
    $serverAuth = [System.Security.Cryptography.OidCollection]::new()
    $serverAuth.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1')) | Out-Null
    $serverRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($serverAuth, $true))
    $subjectNames = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $subjectNames.AddDnsName('localhost')
    foreach ($address in $addresses) { $subjectNames.AddIpAddress($address) }
    $serverRequest.CertificateExtensions.Add($subjectNames.Build($true))
    $serverCertificate = $serverRequest.Create($caCertificate, $now.AddMinutes(-5), $now.AddDays(180), [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16))
    $serverWithKey = $serverCertificate.CopyWithPrivateKey($serverKey)

    $securePassword = Read-Host 'Create a password for the local HTTPS PFX file' -AsSecureString
    if ($securePassword.Length -lt 16) { throw 'Use a PFX password with at least 16 characters.' }
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    [System.IO.File]::WriteAllBytes($pfxPath, $serverWithKey.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $plainPassword))
    $plainPassword = $null
    [System.IO.File]::WriteAllBytes($caPath, $caCertificate.RawData)
    [pscustomobject]@{ ServerPfx = $pfxPath; RootCertificate = $caPath; PhonePort = 5128; ServerNames = @('localhost') + @($addresses | ForEach-Object ToString) }
}
finally {
    if ($passwordPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer) }
    if ($serverWithKey) { $serverWithKey.Dispose() }
    if ($serverCertificate) { $serverCertificate.Dispose() }
    if ($caCertificate) { $caCertificate.Dispose() }
    $serverKey.Dispose()
    $caKey.Dispose()
}
