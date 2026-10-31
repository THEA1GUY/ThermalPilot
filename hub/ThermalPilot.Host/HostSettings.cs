namespace ThermalPilot.Host;

public sealed record HostSettings(string DataDirectory, string ApiToken, int Port = 5127, string Adapter = "ReadOnlyBridge", bool CollectTelemetry = false)
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
    }
}
