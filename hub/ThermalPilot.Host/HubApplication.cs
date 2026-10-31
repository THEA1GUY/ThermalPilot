using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using ThermalPilot.Contracts;

namespace ThermalPilot.Host;

public static class HubApplication
{
    public static WebApplication Build(HostSettings settings, Action<IWebHostBuilder>? configureWebHost = null,
        Action<IServiceCollection>? configureServices = null)
    {
        settings.Validate();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, settings.Port);
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
            if ((context.Connection.RemoteIpAddress is { } remote && !IPAddress.IsLoopback(remote)) ||
                context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]") ||
                context.Request.Headers.ContainsKey("Origin"))
            {
                await Problem(context, 403, "local_tools_only", "This checkpoint accepts local tools only.");
                return;
            }

            if (context.Request.Path != "/health/live")
            {
                var authorization = context.Request.Headers.Authorization.ToString();
                if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                    !CryptographicOperations.FixedTimeEquals(expectedTokenHash,
                        SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]))))
                {
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await Problem(context, 401, "unauthorized", "A valid local access token is required.");
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

        app.MapGet("/health/live", () => Results.Ok(new { status = "alive", adapter = "readOnlyBridge", isSynthetic = false }));
        app.MapGet("/health/ready", (SqliteStore store, LiveTelemetryService live) =>
            Results.Ok(new { status = "ready", adapter = "readOnlyBridge", database = store.Summary(), telemetry = live.Health }));
        app.MapGet("/v1/status", () => new HostStatus(2, SqliteStore.SchemaVersion, AdapterMode.ReadOnlyBridge, false, false, settings.CollectTelemetry, false));
        app.MapGet("/v1/capabilities", (LiveTelemetryService live) =>
            new DeviceCapabilities(AdapterMode.ReadOnlyBridge, live.Snapshot() is { } sample && sample.Sensors.Any(r => r.Quality == MeasurementQuality.Fresh),
                false, live.Snapshot() is { } snapshot && snapshot.Sensors.Any(r => r.Quality == MeasurementQuality.Fresh), []));
        app.MapGet("/v1/telemetry", (LiveTelemetryService live) => live.Snapshot() is { } snapshot
            ? Results.Ok(snapshot) : Results.Problem(statusCode: 503, title: "real_telemetry_unavailable"));
        app.MapGet("/v1/telemetry/history", (TelemetryStore store) => store.Latest());
        app.MapGet("/v1/telemetry/summary", (TelemetryStore store) => store.Summary());
        app.MapGet("/v1/telemetry/health", (LiveTelemetryService live) => live.Health);
        app.MapGet("/v1/policy", (SqliteStore store) => new
        {
            storedDefaults = store.GetPolicy(),
            effectiveCollectionEnabled = settings.CollectTelemetry,
            cloudEnabled = false,
            rawMaximumRows = 20000,
            rawMaximumHours = 72,
            aggregateCollectionEnabled = false
        });
        app.MapGet("/v1/profiles", (SqliteStore store) => store.ListProfiles());
        app.MapGet("/v1/profiles/{id:guid}", (Guid id, SqliteStore store) =>
            store.GetProfile(id) is { } profile ? Results.Ok(profile) : Results.NotFound());
        app.MapPost("/v1/profiles", (CreateProfileRequest request, SqliteStore store) =>
        {
            var profile = store.CreateProfile(request);
            app.Logger.LogInformation("Profile {ProfileId} saved; hardware application is disabled", profile.Id);
            return Results.Created("/v1/profiles/" + profile.Id, profile);
        });
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
