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
        builder.Services.AddSingleton<FakeAdapter>();
        builder.Services.AddSingleton<ISqliteConnections>(new SqliteConnections(settings.DataDirectory));
        builder.Services.AddSingleton<SqliteStore>();
        builder.Services.AddHostedService<StoreLifetime>();
        configureServices?.Invoke(builder.Services);
        var app = builder.Build();
        var expectedTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(settings.ApiToken));

        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-ThermalPilot-Adapter"] = "fake";
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

        app.MapGet("/health/live", () => Results.Ok(new { status = "alive", adapter = "fake", isSynthetic = true }));
        app.MapGet("/health/ready", (SqliteStore store) => Results.Ok(new { status = "ready", adapter = "fake", database = store.Summary() }));
        app.MapGet("/v1/status", () => new HostStatus(1, SqliteStore.SchemaVersion, AdapterMode.Fake, true, false, false, false));
        app.MapGet("/v1/capabilities", (FakeAdapter adapter) => adapter.Capabilities);
        app.MapGet("/v1/telemetry", (FakeAdapter adapter) => adapter.Snapshot());
        app.MapGet("/v1/policy", (SqliteStore store) => store.GetPolicy());
        app.MapGet("/v1/profiles", (SqliteStore store) => store.ListProfiles());
        app.MapGet("/v1/profiles/{id:guid}", (Guid id, SqliteStore store) =>
            store.GetProfile(id) is { } profile ? Results.Ok(profile) : Results.NotFound());
        app.MapPost("/v1/profiles", (CreateProfileRequest request, SqliteStore store) =>
        {
            var profile = store.CreateProfile(request);
            app.Logger.LogInformation("Profile {ProfileId} saved in fake mode", profile.Id);
            return Results.Created("/v1/profiles/" + profile.Id, profile);
        });
        app.MapGet("/v1/commands", (SqliteStore store) => store.ListCommands());
        app.MapGet("/v1/commands/{id:guid}", (Guid id, SqliteStore store) =>
            store.GetCommand(id) is { } command ? Results.Ok(command) : Results.NotFound());
        app.MapPost("/v1/commands/simulate", (SimulateCommandRequest request, SqliteStore store) =>
        {
            var receipt = store.SimulateCommand(request);
            app.Logger.LogInformation("Command {CommandId} simulated; correlation {CorrelationId}; replay {Replayed}",
                receipt.Command.Id, receipt.Command.CorrelationId, receipt.Replayed);
            return Results.Ok(receipt);
        });
        app.MapGet("/v1/reports", (SqliteStore store) => store.ListReports());
        app.MapGet("/v1/reports/{id:guid}", (Guid id, SqliteStore store) =>
            store.GetReport(id) is { } report ? Results.Ok(report) : Results.NotFound());
        app.MapPost("/v1/reports/synthetic", (CreateSyntheticReportRequest request, SqliteStore store) =>
        {
            var report = store.CreateSyntheticReport(request);
            app.Logger.LogInformation("Synthetic report {ReportId} saved", report.Id);
            return Results.Created("/v1/reports/" + report.Id, report);
        });
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
