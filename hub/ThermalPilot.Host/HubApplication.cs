using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http.Json;
using ThermalPilot.Contracts;

namespace ThermalPilot.Host;

public static class HubApplication
{
    public static WebApplication Build(HostSettings settings, Action<IWebHostBuilder>? configureWebHost = null,
        Action<IServiceCollection>? configureServices = null)
    {
        settings.Validate();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [], EnvironmentName = "Production", WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        });
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, settings.Port);
            if (settings.AllowLan)
                options.ListenAnyIP(settings.PhonePort, endpoint => endpoint.UseHttps(settings.TlsCertificate!));
            options.Limits.MaxRequestBodySize = 32_768;
        });
        configureWebHost?.Invoke(builder.WebHost);
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.UnmappedMemberHandling = ContractJson.Options.UnmappedMemberHandling;
            options.SerializerOptions.RespectRequiredConstructorParameters = true;
            foreach (var converter in ContractJson.Options.Converters) options.SerializerOptions.Converters.Add(converter);
        });
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ISqliteConnections>(new SqliteConnections(settings.DataDirectory));
        builder.Services.AddSingleton<SqliteStore>();
        builder.Services.AddHostedService<StoreLifetime>();
        builder.Services.AddSingleton<TelemetryStore>();
        builder.Services.AddSingleton<PairingCodeService>();
        builder.Services.AddSingleton<LiveTelemetryService>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<LiveTelemetryService>());
        configureServices?.Invoke(builder.Services);
        var app = builder.Build();
        var expectedTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(settings.ApiToken));

        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-ThermalPilot-Adapter"] = "readOnlyBridge";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            if (settings.AllowLan && context.Request.IsHttps && IPAddress.TryParse(context.Request.Host.Host, out var requestedAddress) && !IPAddress.IsLoopback(requestedAddress))
                context.Response.Headers.StrictTransportSecurity = "max-age=15552000";
            var remote = NormalizeAddress(context.Connection.RemoteIpAddress);
            var localRequest = (remote is null || IPAddress.IsLoopback(remote)) && IsLocalHost(context.Request.Host.Host);
            var privateTlsRequest = settings.AllowLan && context.Request.IsHttps && remote is not null && IsPrivateAddress(remote) && IsAllowedHost(context.Request.Host.Host);
            if ((!localRequest && !privateTlsRequest) || !IsAllowedOrigin(context))
            {
                await Problem(context, 403, "network_boundary", "The request is outside the configured local or trusted LAN boundary.");
                return;
            }

            var pairingRedeem = context.Request.Path == "/v1/pairing/redeem" && HttpMethods.IsPost(context.Request.Method);
            if (pairingRedeem && (!settings.AllowLan || !context.Request.IsHttps))
            {
                await Problem(context, 403, "pairing_requires_https", "Phone pairing is available only over configured HTTPS.");
                return;
            }

            if (context.Request.Path != "/health/live" && !IsDashboardAsset(context.Request.Path) && !pairingRedeem)
            {
                var authorization = context.Request.Headers.Authorization.ToString();
                var ownerToken = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
                    CryptographicOperations.FixedTimeEquals(expectedTokenHash, SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..])));
                var pairedToken = false;
                if (!ownerToken && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    try { pairedToken = app.Services.GetRequiredService<SqliteStore>().IsActivePairedClient(authorization[7..]); }
                    catch (StorageFailure failure)
                    {
                        app.Logger.LogError("Phone authentication storage failed with code {StorageCode}", failure.Code);
                        await Problem(context, 503, failure.Code, "Local storage is unavailable; authentication could not be completed.");
                        return;
                    }
                }
                if (!ownerToken && !pairedToken)
                {
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await Problem(context, 401, "unauthorized", "A valid owner or active paired-device token is required.");
                    return;
                }
                context.Items["ThermalPilot.OwnerSession"] = ownerToken;
                if (!ownerToken && IsOwnerOnlyEndpoint(context.Request.Path))
                {
                    await Problem(context, 403, "owner_required", "This phone-pairing action requires the owner token.");
                    return;
                }
                if (pairedToken && !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) ||
                    context.Request.Path == "/v1/incidents" && HttpMethods.IsPost(context.Request.Method)))
                {
                    await Problem(context, 403, "phone_read_only", "Paired phone sessions can view data and mark incidents; owner controls stay local.");
                    return;
                }
            }

            try { await next(context); }
            catch (RequestFailure failure)
            {
                await Problem(context, failure.StatusCode, failure.Code, failure.Message);
            }
            catch (StorageFailure failure)
            {
                app.Logger.LogError("Local storage operation failed with code {StorageCode}", failure.Code);
                await Problem(context, 503, failure.Code, "Local storage is unavailable; the operation was not reported as successful.");
            }
            catch (BadHttpRequestException)
            {
                await Problem(context, 400, "invalid_request", "The request does not match the supported contract.");
            }
            catch (JsonException)
            {
                await Problem(context, 400, "invalid_request", "The request does not match the supported contract.");
            }
        });

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/health/live", () => Results.Ok(new { status = "alive", adapter = "readOnlyBridge", isSynthetic = false }));
        app.MapGet("/health/ready", (SqliteStore store, LiveTelemetryService live) =>
            Results.Ok(new { status = "ready", adapter = "readOnlyBridge", database = store.Summary(), telemetry = live.Health }));
        app.MapGet("/v1/status", (LiveTelemetryService live) => new HostStatus(2, SqliteStore.SchemaVersion, AdapterMode.ReadOnlyBridge, false, false, live.CollectionEnabled, settings.AllowLan));
        app.MapGet("/v1/session", (HttpContext context) => new { role = context.Items["ThermalPilot.OwnerSession"] is true ? "owner" : "pairedPhone" });
        app.MapGet("/v1/capabilities", (LiveTelemetryService live) =>
            new DeviceCapabilities(AdapterMode.ReadOnlyBridge, live.Snapshot() is { } sample && sample.Sensors.Any(r => r.Quality == MeasurementQuality.Fresh),
                false, live.Snapshot() is { } snapshot && snapshot.Sensors.Any(r => r.Quality == MeasurementQuality.Fresh), []));
        app.MapGet("/v1/telemetry", (LiveTelemetryService live) => live.Snapshot() is { } snapshot
            ? Results.Ok(snapshot) : Results.Problem(statusCode: 503, title: "real_telemetry_unavailable"));
        app.MapGet("/v1/telemetry/history", (TelemetryStore store) => store.Latest());
        app.MapGet("/v1/telemetry/range", (HttpRequest request, TelemetryStore store) =>
        {
            if (!int.TryParse(request.Query["hours"], System.Globalization.CultureInfo.InvariantCulture, out var hours) ||
                hours is not (1 or 6 or 24 or 72))
                return Results.BadRequest(new { error = "unsupported_history_range", supportedHours = new[] { 1, 6, 24, 72 } });
            var now = DateTimeOffset.UtcNow;
            return Results.Ok(store.Range(now.AddHours(-hours), now));
        });
        app.MapGet("/v1/telemetry/summary", (TelemetryStore store) => store.Summary());
        app.MapGet("/v1/telemetry/health", (LiveTelemetryService live) => live.Health);
        app.MapGet("/v1/incidents", (SqliteStore store) => store.ListIncidents());
        app.MapPost("/v1/incidents", (CreateIncidentRequest request, SqliteStore store, LiveTelemetryService live) =>
        {
            var incident = store.CreateIncident(request, live.Snapshot());
            app.Logger.LogInformation("Incident {IncidentId} marked from the dashboard", incident.Id);
            return Results.Created("/v1/incidents/" + incident.Id, incident);
        });
        app.MapPost("/v1/pairing/code", (PairingCodeService pairing) =>
        {
            if (!settings.AllowLan) return Results.Conflict(new { error = "https_lan_mode_required" });
            var code = pairing.Create();
            return Results.Ok(new { code.Code, code.ExpiresAtUtc, phonePort = settings.PhonePort, hostAddresses = GetLanAddresses() });
        });
        app.MapPost("/v1/pairing/redeem", (CreatePairingRequest request, PairingCodeService pairing, SqliteStore store) =>
        {
            Validation.PairedClientName(request.Name);
            if (!pairing.TryRedeem(request.Code)) return Results.Unauthorized();
            var clientToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var client = store.AddPairedClient(request.Name, clientToken);
            return Results.Ok(new PairingSession(clientToken, client));
        });
        app.MapGet("/v1/pairing/clients", (SqliteStore store) => store.ListPairedClients());
        app.MapDelete("/v1/pairing/clients/{id:guid}", (Guid id, SqliteStore store) =>
            store.RevokePairedClient(id) ? Results.NoContent() : Results.NotFound());
        app.MapGet("/v1/policy", (SqliteStore store, LiveTelemetryService live) => new
        {
            storedDefaults = store.GetPolicy(),
            effectiveCollectionEnabled = live.CollectionEnabled,
            cloudEnabled = false,
            rawMaximumRows = 20000,
            rawMaximumHours = 72,
            aggregateCollectionEnabled = false
        });
        app.MapPost("/v1/policy/collection", (SetCollectionRequest request, LiveTelemetryService live) =>
            Results.Ok(new { effectiveCollectionEnabled = live.SetCollectionEnabled(request.Enabled), hardwareWritesEnabled = false }));
        app.MapGet("/v1/profiles", (SqliteStore store) => store.ListProfiles());
        app.MapGet("/v1/profiles/{id:guid}", (Guid id, SqliteStore store) =>
            store.GetProfile(id) is { } profile ? Results.Ok(profile) : Results.NotFound());
        app.MapPost("/v1/profiles", (CreateProfileRequest request, SqliteStore store) =>
        {
            var profile = store.CreateProfile(request);
            app.Logger.LogInformation("Profile {ProfileId} saved; hardware application is disabled", profile.Id);
            return Results.Created("/v1/profiles/" + profile.Id, profile);
        });
        app.MapPost("/v1/profiles/import", (ProfileArchive archive, SqliteStore store) =>
        {
            Validation.ProfileArchive(archive);
            var profile = store.CreateProfile(new CreateProfileRequest(archive.Name, archive.BaseMode));
            return Results.Created("/v1/profiles/" + profile.Id, profile);
        });
        app.MapPost("/v1/profiles/{id:guid}/clone", (Guid id, CloneProfileRequest request, SqliteStore store) =>
            store.CloneProfile(id, request) is { } clone
                ? Results.Created("/v1/profiles/" + clone.Id, clone)
                : Results.NotFound());
        app.MapGet("/v1/profiles/{id:guid}/versions", (Guid id, SqliteStore store) =>
            store.GetProfile(id) is null ? Results.NotFound() : Results.Ok(store.ListProfileVersions(id)));
        app.MapPost("/v1/profiles/{id:guid}/versions", (Guid id, CreateProfileVersionRequest request, SqliteStore store) =>
            store.CreateProfileVersion(id, request) is { } version
                ? Results.Ok(version)
                : Results.NotFound());
        app.MapGet("/v1/profiles/{id:guid}/export", (Guid id, SqliteStore store) =>
            store.GetProfile(id) is { } profile
                ? Results.Ok(new ProfileArchive(1, profile.Name, profile.BaseMode))
                : Results.NotFound());
        app.MapGet("/v1/profiles/{id:guid}/activation-preview", (Guid id, SqliteStore store) =>
            store.GetProfile(id) is { } profile
                ? Results.Ok(new ProfileActivationPreview(profile, false, false, "single_writer_coordinator_required",
                    "This profile is saved only. Activation is disabled until all G-Helper and ThermalPilot hardware writers are coordinated and device acceptance is complete."))
                : Results.NotFound());
        app.MapGet("/v1/commands", (SqliteStore store) => store.ListCommands());
        app.MapGet("/v1/commands/{id:guid}", (Guid id, SqliteStore store) =>
            store.GetCommand(id) is { } command ? Results.Ok(command) : Results.NotFound());
        app.MapGet("/v1/reports", (SqliteStore store) => store.ListReports());
        app.MapGet("/v1/reports/{id:guid}", (Guid id, SqliteStore store) =>
            store.GetReport(id) is { } report ? Results.Ok(report) : Results.NotFound());
        app.MapGet("/v1/events", (SqliteStore store) => store.ListEvents());
        return app;
    }

    private static Task Problem(HttpContext context, int status, string code, string detail) =>
        Results.Problem(statusCode: status, title: code, detail: detail).ExecuteAsync(context);

    private static bool IsAllowedOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length == 0) return true;
        return Uri.TryCreate(origin, UriKind.Absolute, out var parsed) &&
            string.Equals(parsed.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parsed.Authority, context.Request.Host.ToUriComponent(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDashboardAsset(PathString path) =>
        path == "/" || path.StartsWithSegments("/assets") || path == "/favicon.svg";

    private static bool IsOwnerOnlyEndpoint(PathString path) => path.StartsWithSegments("/v1/pairing") || path.StartsWithSegments("/v1/policy/collection");

    private static bool IsLocalHost(string host) => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    private static bool IsAllowedHost(string host) => IsLocalHost(host) || IPAddress.TryParse(host, out var address) && IsPrivateAddress(address);

    private static IPAddress? NormalizeAddress(IPAddress? address) => address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC;
        }
        return false;
    }

    private static string[] GetLanAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(network => network.OperationalStatus == OperationalStatus.Up)
        .SelectMany(network => network.GetIPProperties().UnicastAddresses)
        .Select(unicast => unicast.Address)
        .Where(address => address.AddressFamily == AddressFamily.InterNetwork && IsPrivateAddress(address))
        .Select(address => address.ToString())
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
}

internal sealed class StoreLifetime(HostSettings settings, SqliteStore store) : IHostedService, IDisposable
{
    private DatabaseLease? lease;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            lease = new DatabaseLease(settings.DataDirectory);
            store.Initialize();
            store.GetPolicy();
            return Task.CompletedTask;
        }
        catch
        {
            lease?.Dispose();
            lease = null;
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lease?.Dispose();
        lease = null;
        return Task.CompletedTask;
    }

    public void Dispose() => lease?.Dispose();
}
