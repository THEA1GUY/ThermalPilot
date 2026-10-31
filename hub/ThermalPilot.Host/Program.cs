using System.Text.Json;
using System.Security.Cryptography.X509Certificates;

namespace ThermalPilot.Host;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var verb = args.Length == 0 ? "serve" : args[0];
            var options = ParseOptions(args.Skip(args.Length == 0 ? 0 : 1).ToArray());
            var dataDirectory = options.GetValueOrDefault("--data-dir") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThermalPilot", "hub");
            dataDirectory = Path.GetFullPath(dataDirectory);

            if (verb == "backup")
            {
                Console.WriteLine(JsonSerializer.Serialize(DatabaseMaintenance.Backup(dataDirectory, Required(options, "--destination"))));
                return 0;
            }
            if (verb == "restore")
            {
                Console.WriteLine(JsonSerializer.Serialize(DatabaseMaintenance.RestoreToNewDirectory(Required(options, "--source"), dataDirectory)));
                return 0;
            }
            if (verb != "serve") throw new ArgumentException("Use serve, backup or restore.");
            var port = options.TryGetValue("--port", out var portText) ? int.Parse(portText, System.Globalization.CultureInfo.InvariantCulture) : 5127;
            bool? collect = options.TryGetValue("--collect", out var enabled) ? bool.Parse(enabled) : null;
            var allowLan = options.TryGetValue("--lan", out var lanText) && bool.Parse(lanText);
            var certificatePath = options.GetValueOrDefault("--tls-pfx");
            if (allowLan != (certificatePath is not null)) throw new ArgumentException("--lan true requires --tls-pfx; the certificate is only used for HTTPS LAN mode.");
            var phonePort = options.TryGetValue("--phone-port", out var phonePortText) ? int.Parse(phonePortText, System.Globalization.CultureInfo.InvariantCulture) : 5128;
            if (!allowLan && options.ContainsKey("--phone-port")) throw new ArgumentException("--phone-port requires --lan true.");
            using var certificate = certificatePath is null ? null : X509CertificateLoader.LoadPkcs12FromFile(
                Path.GetFullPath(certificatePath), Environment.GetEnvironmentVariable("THERMALPILOT_TLS_PASSWORD"), X509KeyStorageFlags.EphemeralKeySet);
            var settings = new HostSettings(dataDirectory, Environment.GetEnvironmentVariable("THERMALPILOT_API_TOKEN") ?? "",
                port, CollectTelemetry: collect, AllowLan: allowLan, TlsCertificate: certificate, PhonePort: phonePort);
            await using var app = HubApplication.Build(settings);
            await app.RunAsync();
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException or StorageFailure or RequestFailure or InvalidOperationException)
        {
            Console.Error.WriteLine("ThermalPilot could not start or complete maintenance: " + exception.GetType().Name + ". Check the data path, token, port and database availability.");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("Every option requires a value.");
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (args[index] is not ("--data-dir" or "--port" or "--destination" or "--source" or "--collect" or "--lan" or "--tls-pfx" or "--phone-port") ||
                !options.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("Unknown or duplicate option.");
        }
        return options;
    }

    private static string Required(Dictionary<string, string> options, string key) =>
        options.GetValueOrDefault(key) ?? throw new ArgumentException("Missing required option: " + key);
}
