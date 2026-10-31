using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using ThermalPilot.Contracts;
using ThermalPilot.Host;

namespace ThermalPilot.Host.Tests;

public sealed class TestWorkspace : IDisposable
{
    private readonly string parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data"));
    public string Root { get; }

    public TestWorkspace()
    {
        Root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string PathFor(string name) => Path.Combine(Root, name);
    public SqliteStore Store(string name = "data")
    {
        var store = new SqliteStore(new SqliteConnections(PathFor(name)), TimeProvider.System, new FakeAdapter());
        store.Initialize();
        return store;
    }

    public void Dispose()
    {
        var fullPath = Path.GetFullPath(Root);
        if (!fullPath.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing cleanup outside this test's owned directory.");
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
    }
}

public sealed class SwitchableConnections(ISqliteConnections inner) : ISqliteConnections
{
    public string DatabasePath => inner.DatabasePath;
    public bool ReadOnly { get; set; }
    public bool Full { get; set; }

    public SqliteConnection Open()
    {
        var connection = inner.Open();
        using var command = connection.CreateCommand();
        if (ReadOnly)
        {
            command.CommandText = "PRAGMA query_only=ON;";
            command.ExecuteNonQuery();
        }
        if (Full)
        {
            command.CommandText = "PRAGMA page_count;";
            var pages = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            command.CommandText = $"PRAGMA max_page_count={pages};";
            command.ExecuteScalar();
        }
        return connection;
    }
}

public sealed class RunningHub : IAsyncDisposable
{
    private readonly Microsoft.AspNetCore.Builder.WebApplication app;
    public HttpClient Client { get; }
    public static string Token => new('t', 48);

    private RunningHub(Microsoft.AspNetCore.Builder.WebApplication application)
    {
        app = application;
        Client = application.GetTestClient();
        Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
    }

    public static async Task<RunningHub> Start(string dataDirectory, Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? services = null)
    {
        var app = HubApplication.Build(new HostSettings(dataDirectory, Token), web => web.UseTestServer(), services);
        try { await app.StartAsync(); }
        catch { await app.DisposeAsync(); throw; }
        return new RunningHub(app);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }
}

public static class TestRequests
{
    public static SimulateCommandRequest Command(ProfileRecord profile, string? key = null) =>
        new(profile.Id, profile.Version, key ?? Guid.NewGuid().ToString("N"), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
}
