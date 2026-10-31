using System.Security.Cryptography.X509Certificates;

namespace ThermalPilot.Host;

public sealed record HostSettings(string DataDirectory, string ApiToken, int Port = 5127, string Adapter = "ReadOnlyBridge",
    bool? CollectTelemetry = null, bool AllowLan = false, X509Certificate2? TlsCertificate = null, int PhonePort = 5128)
{
    public void Validate()
    {
        if (!string.Equals(Adapter, "ReadOnlyBridge", StringComparison.Ordinal))
            throw new ArgumentException("Only the real read-only bridge is supported.");
        if (string.IsNullOrWhiteSpace(DataDirectory) || !Path.IsPathFullyQualified(DataDirectory))
            throw new ArgumentException("An absolute data directory is required.");
        if (ApiToken is null || ApiToken.Length is < 32 or > 256 || ApiToken.Any(character => character < 33 || character > 126))
            throw new ArgumentException("THERMALPILOT_API_TOKEN must contain 32-256 printable characters without spaces.");
        if (Port is < 1024 or > 65535)
            throw new ArgumentException("Select a port between 1024 and 65535.");
        if (AllowLan != (TlsCertificate is not null) || (AllowLan && (!TlsCertificate!.HasPrivateKey || TlsCertificate.NotAfter <= DateTime.UtcNow)))
            throw new ArgumentException("LAN access requires a valid TLS certificate with a private key; HTTP-only LAN access is refused.");
        if (AllowLan && (PhonePort is < 1024 or > 65535 || PhonePort == Port))
            throw new ArgumentException("The phone HTTPS port must be valid and different from the loopback desktop port.");
    }
}
