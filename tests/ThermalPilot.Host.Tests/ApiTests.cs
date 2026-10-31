using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThermalPilot.Contracts;
using ThermalPilot.Host;
using Xunit;

namespace ThermalPilot.Host.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task Authentication_origin_and_host_checks_protect_local_API()
    {
        using var workspace = new TestWorkspace();
        await using var hub = await RunningHub.Start(workspace.PathFor("api"));
        hub.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await hub.Client.GetAsync("/v1/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hub.Client.GetAsync("/health/live")).StatusCode);
        hub.Client.DefaultRequestHeaders.Authorization = new("Bearer", RunningHub.Token);
        using var origin = new HttpRequestMessage(HttpMethod.Get, "/v1/status");
        origin.Headers.Add("Origin", "http://example.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await hub.Client.SendAsync(origin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hub.Client.GetAsync("http://example.invalid/v1/status")).StatusCode);
    }

    [Fact]
    public async Task Status_capabilities_and_snapshots_are_explicitly_fake()
    {
        using var workspace = new TestWorkspace();
        await using var hub = await RunningHub.Start(workspace.PathFor("api"));
        var response = await hub.Client.GetAsync("/v1/status");
        Assert.Equal("fake", response.Headers.GetValues("X-ThermalPilot-Adapter").Single());
        Assert.True(response.Headers.CacheControl!.NoStore);
        var status = await response.Content.ReadFromJsonAsync<HostStatus>(ContractJson.Options);
        Assert.NotNull(status);
        Assert.Equal(AdapterMode.Fake, status.Adapter);
        Assert.True(status.IsSynthetic);
        Assert.False(status.CloudEnabled);
        Assert.False(status.CollectionEnabled);
        Assert.False(status.PhoneAccessEnabled);
        var capabilities = await hub.Client.GetFromJsonAsync<DeviceCapabilities>("/v1/capabilities", ContractJson.Options);
        Assert.False(capabilities!.HardwareConnected);
        var snapshot = await hub.Client.GetFromJsonAsync<TelemetrySnapshot>("/v1/telemetry", ContractJson.Options);
        Assert.Null(snapshot!.CpuTemperature.Value);
        Assert.Equal(MeasurementQuality.Unavailable, snapshot.CpuTemperature.Quality);
    }

    [Theory]
    [InlineData("""{"name":"","baseMode":"balanced"}""")]
    [InlineData("""{"name":"Missing mode"}""")]
    [InlineData("""{"baseMode":"balanced"}""")]
    [InlineData("""{"name":"Profile","baseMode":"unknown"}""")]
    [InlineData("""{"name":"Profile","baseMode":1}""")]
    [InlineData("""{"name":"Profile","baseMode":"balanced","mode_command":"unsafe"}""")]
    public async Task Invalid_contracts_are_rejected_without_creating_profiles(string body)
    {
        using var workspace = new TestWorkspace();
        await using var hub = await RunningHub.Start(workspace.PathFor("api"));
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await hub.Client.PostAsync("/v1/profiles", content)).StatusCode);
        Assert.Empty((await hub.Client.GetFromJsonAsync<ProfileRecord[]>("/v1/profiles", ContractJson.Options))!);
    }

    [Fact]
    public async Task Complete_records_survive_actual_host_restart()
    {
        using var workspace = new TestWorkspace();
        ProfileRecord profile;
        CommandReceipt receipt;
        ReportRecord report;
        SimulateCommandRequest request;
        await using (var first = await RunningHub.Start(workspace.PathFor("api")))
        {
            var profileResponse = await first.Client.PostAsJsonAsync("/v1/profiles", new CreateProfileRequest("Docker coding", ProfileMode.Balanced), ContractJson.Options);
            Assert.Equal(HttpStatusCode.Created, profileResponse.StatusCode);
            profile = (await profileResponse.Content.ReadFromJsonAsync<ProfileRecord>(ContractJson.Options))!;
            request = TestRequests.Command(profile);
            receipt = (await (await first.Client.PostAsJsonAsync("/v1/commands/simulate", request, ContractJson.Options))
                .Content.ReadFromJsonAsync<CommandReceipt>(ContractJson.Options))!;
            Assert.Equal(CommandOutcome.Simulated, receipt.Command.Outcome);
            Assert.False(receipt.Command.HardwareChanged);
            report = (await (await first.Client.PostAsJsonAsync("/v1/reports/synthetic",
                new CreateSyntheticReportRequest("Synthetic session", "Fixture, not a hardware observation."), ContractJson.Options))
                .Content.ReadFromJsonAsync<ReportRecord>(ContractJson.Options))!;
        }
        await using var second = await RunningHub.Start(workspace.PathFor("api"));
        Assert.Equal(profile, await second.Client.GetFromJsonAsync<ProfileRecord>("/v1/profiles/" + profile.Id, ContractJson.Options));
        Assert.Equal(report, await second.Client.GetFromJsonAsync<ReportRecord>("/v1/reports/" + report.Id, ContractJson.Options));
        var replay = await (await second.Client.PostAsJsonAsync("/v1/commands/simulate", request, ContractJson.Options))
            .Content.ReadFromJsonAsync<CommandReceipt>(ContractJson.Options);
        Assert.True(replay!.Replayed);
        Assert.Equal(receipt.Command.Id, replay.Command.Id);
        Assert.Equal(3, (await second.Client.GetFromJsonAsync<AuditEvent[]>("/v1/events", ContractJson.Options))!.Length);
        Assert.Equal(HttpStatusCode.OK, (await second.Client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Storage_full_is_503_with_no_false_success_or_partial_event()
    {
        using var workspace = new TestWorkspace();
        var connections = new SwitchableConnections(new SqliteConnections(workspace.PathFor("api")));
        await using var hub = await RunningHub.Start(workspace.PathFor("api"), services =>
        {
            services.RemoveAll<ISqliteConnections>();
            services.AddSingleton<ISqliteConnections>(connections);
        });
        connections.Full = true;
        var response = await hub.Client.PostAsJsonAsync("/v1/reports/synthetic", new CreateSyntheticReportRequest("Synthetic full-disk test", new string('x', 15_000)), ContractJson.Options);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("sqlite_13", await response.Content.ReadAsStringAsync());
        connections.Full = false;
        Assert.Empty((await hub.Client.GetFromJsonAsync<ReportRecord[]>("/v1/reports", ContractJson.Options))!);
        Assert.Empty((await hub.Client.GetFromJsonAsync<AuditEvent[]>("/v1/events", ContractJson.Options))!);
    }

    [Fact]
    public void Non_fake_adapter_or_missing_token_is_rejected_before_startup()
    {
        using var workspace = new TestWorkspace();
        Assert.Throws<ArgumentException>(() => HubApplication.Build(new HostSettings(workspace.PathFor("new"), RunningHub.Token, Adapter: "Real")));
        Assert.Throws<ArgumentException>(() => HubApplication.Build(new HostSettings(workspace.PathFor("new"), "")));
        Assert.False(Directory.Exists(workspace.PathFor("new")));
    }
}
