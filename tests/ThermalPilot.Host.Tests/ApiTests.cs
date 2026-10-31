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
    public async Task Real_mode_has_no_fabricated_readings_when_bridge_is_absent()
    {
        using var workspace = new TestWorkspace();
        await using var hub = await RunningHub.Start(workspace.PathFor("api"));
        var response = await hub.Client.GetAsync("/v1/status");
        Assert.Equal("readOnlyBridge", response.Headers.GetValues("X-ThermalPilot-Adapter").Single());
        var status = await response.Content.ReadFromJsonAsync<HostStatus>(ContractJson.Options);
        Assert.Equal(AdapterMode.ReadOnlyBridge, status!.Adapter);
        Assert.False(status.IsSynthetic);
        Assert.False(status.CloudEnabled);
        Assert.False(status.CollectionEnabled);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await hub.Client.GetAsync("/v1/telemetry")).StatusCode);
        Assert.Empty((await hub.Client.GetFromJsonAsync<LiveSnapshot[]>("/v1/telemetry/history", ContractJson.Options))!);
        Assert.Contains((await hub.Client.PostAsJsonAsync("/v1/commands/simulate", new { })).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Contains((await hub.Client.PostAsJsonAsync("/v1/reports/synthetic", new { })).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
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
    public async Task Saved_profile_survives_host_restart_without_claiming_activation()
    {
        using var workspace = new TestWorkspace();
        ProfileRecord profile;
        await using (var first = await RunningHub.Start(workspace.PathFor("api")))
        {
            var response = await first.Client.PostAsJsonAsync("/v1/profiles", new CreateProfileRequest("Coding", ProfileMode.Balanced), ContractJson.Options);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            profile = (await response.Content.ReadFromJsonAsync<ProfileRecord>(ContractJson.Options))!;
        }
        await using var second = await RunningHub.Start(workspace.PathFor("api"));
        Assert.Equal(profile, await second.Client.GetFromJsonAsync<ProfileRecord>("/v1/profiles/" + profile.Id, ContractJson.Options));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await second.Client.GetAsync("/v1/telemetry")).StatusCode);
    }
    [Fact]
    public void Fake_adapter_or_missing_token_is_rejected_before_startup()
    {
        using var workspace = new TestWorkspace();
        Assert.Throws<ArgumentException>(() => HubApplication.Build(new HostSettings(workspace.PathFor("new"), RunningHub.Token, Adapter: "Fake")));
        Assert.Throws<ArgumentException>(() => HubApplication.Build(new HostSettings(workspace.PathFor("new"), "")));
        Assert.False(Directory.Exists(workspace.PathFor("new")));
    }
}
