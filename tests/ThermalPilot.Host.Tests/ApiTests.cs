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
    public void Pairing_code_expires_and_can_only_be_redeemed_once()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var pairing = new PairingCodeService(clock);
        var issued = pairing.Create();
        Assert.Equal(clock.GetUtcNow().AddMinutes(5), issued.ExpiresAtUtc);
        Assert.False(pairing.TryRedeem("invalid-connection-code-123456789"));
        Assert.True(pairing.TryRedeem(issued.Code));
        Assert.False(pairing.TryRedeem(issued.Code));
        var next = pairing.Create();
        clock.Now = clock.Now.AddMinutes(6);
        Assert.False(pairing.TryRedeem(next.Code));
    }

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

    [Fact]
    public async Task Dashboard_history_range_and_incident_api_use_only_persisted_records()
    {
        using var workspace = new TestWorkspace();
        await using var hub = await RunningHub.Start(workspace.PathFor("dashboard"));
        var status = await hub.Client.GetFromJsonAsync<HostStatus>("/v1/status", ContractJson.Options);
        Assert.Equal(5, status!.SchemaVersion);
        Assert.Empty((await hub.Client.GetFromJsonAsync<LiveSnapshot[]>("/v1/telemetry/range?hours=1", ContractJson.Options))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await hub.Client.GetAsync("/v1/telemetry/range?hours=2")).StatusCode);

        var response = await hub.Client.PostAsJsonAsync("/v1/incidents", new CreateIncidentRequest("Fan noise during call"), ContractJson.Options);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var incident = (await response.Content.ReadFromJsonAsync<IncidentRecord>(ContractJson.Options))!;
        Assert.Null(incident.SnapshotSequence);
        Assert.Single((await hub.Client.GetFromJsonAsync<IncidentRecord[]>("/v1/incidents", ContractJson.Options))!);
    }

    [Fact]
    public async Task Dashboard_assets_are_served_without_caching_or_external_sources()
    {
        using var workspace = new TestWorkspace();
        await using var hub = await RunningHub.Start(workspace.PathFor("dashboard-assets"));
        using var response = await hub.Client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"root\"", html);
        Assert.DoesNotContain("https://", html);
        hub.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await hub.Client.GetAsync("/v1/status")).StatusCode);
    }

    [Fact]
    public async Task Paired_phone_can_read_and_mark_but_cannot_manage_or_write_profiles()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store("phone");
        const string phoneToken = "phone-session-token-that-is-long-and-random-123456";
        var client = store.AddPairedClient("Android phone", phoneToken);
        await using var hub = await RunningHub.Start(workspace.PathFor("phone"));
        hub.Client.DefaultRequestHeaders.Authorization = new("Bearer", phoneToken);
        Assert.Equal(HttpStatusCode.OK, (await hub.Client.GetAsync("/v1/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hub.Client.PostAsJsonAsync("/v1/profiles", new CreateProfileRequest("Unsafe", ProfileMode.Balanced), ContractJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hub.Client.PostAsJsonAsync("/v1/profiles/import", new ProfileArchive(1, "Unsafe", ProfileMode.Balanced), ContractJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hub.Client.PostAsJsonAsync("/v1/profiles/00000000-0000-0000-0000-000000000001/clone", new CloneProfileRequest("Unsafe"), ContractJson.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hub.Client.GetAsync("/v1/pairing/clients")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await hub.Client.PostAsJsonAsync("/v1/incidents", new CreateIncidentRequest("Phone observation"), ContractJson.Options)).StatusCode);
        Assert.True(store.RevokePairedClient(client.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await hub.Client.GetAsync("/v1/status")).StatusCode);
    }

    [Fact]
    public async Task Collection_preference_is_owner_only_and_changes_effective_host_state()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store("collection");
        const string phoneToken = "phone-session-token-that-is-long-and-random-123456";
        store.AddPairedClient("Android phone", phoneToken);
        await using var hub = await RunningHub.Start(workspace.PathFor("collection"));
        hub.Client.DefaultRequestHeaders.Authorization = new("Bearer", phoneToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await hub.Client.PostAsJsonAsync("/v1/policy/collection", new SetCollectionRequest(true), ContractJson.Options)).StatusCode);
        hub.Client.DefaultRequestHeaders.Authorization = new("Bearer", RunningHub.Token);
        var response = await hub.Client.PostAsJsonAsync("/v1/policy/collection", new SetCollectionRequest(true), ContractJson.Options);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await hub.Client.GetFromJsonAsync<HostStatus>("/v1/status", ContractJson.Options))!.CollectionEnabled);
        var toggles = Enumerable.Range(0, 12).Select(index =>
            hub.Client.PostAsJsonAsync("/v1/policy/collection", new SetCollectionRequest(index % 2 == 0), ContractJson.Options));
        var toggleResponses = await Task.WhenAll(toggles);
        Assert.All(toggleResponses, item => Assert.Equal(HttpStatusCode.OK, item.StatusCode));
        using var policyResponse = await hub.Client.GetAsync("/v1/policy");
        using var policy = System.Text.Json.JsonDocument.Parse(await policyResponse.Content.ReadAsStringAsync());
        var stored = policy.RootElement.GetProperty("effectiveCollectionEnabled").GetBoolean();
        Assert.Equal(stored, (await hub.Client.GetFromJsonAsync<HostStatus>("/v1/status", ContractJson.Options))!.CollectionEnabled);
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
    public async Task Profiles_can_be_cloned_versioned_exported_and_imported_without_hardware_activation()
    {
        using var workspace = new TestWorkspace();
        await using var hub = await RunningHub.Start(workspace.PathFor("profiles"));
        var create = await hub.Client.PostAsJsonAsync("/v1/profiles", new CreateProfileRequest("Call", ProfileMode.Silent), ContractJson.Options);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var original = (await create.Content.ReadFromJsonAsync<ProfileRecord>(ContractJson.Options))!;

        var preview = await hub.Client.GetFromJsonAsync<ProfileActivationPreview>($"/v1/profiles/{original.Id}/activation-preview", ContractJson.Options);
        Assert.False(preview!.CanActivate);
        Assert.False(preview.HardwareWritesEnabled);
        Assert.Equal("single_writer_coordinator_required", preview.ReasonCode);
        Assert.Empty((await hub.Client.GetFromJsonAsync<CommandRecord[]>("/v1/commands", ContractJson.Options))!);

        var cloneResponse = await hub.Client.PostAsJsonAsync($"/v1/profiles/{original.Id}/clone", new CloneProfileRequest("Presentation"), ContractJson.Options);
        Assert.Equal(HttpStatusCode.Created, cloneResponse.StatusCode);
        var clone = (await cloneResponse.Content.ReadFromJsonAsync<ProfileRecord>(ContractJson.Options))!;
        Assert.NotEqual(original.Id, clone.Id);
        Assert.Equal(ProfileMode.Silent, clone.BaseMode);

        var versionResponse = await hub.Client.PostAsJsonAsync($"/v1/profiles/{original.Id}/versions", new CreateProfileVersionRequest(ProfileMode.Balanced), ContractJson.Options);
        Assert.Equal(HttpStatusCode.OK, versionResponse.StatusCode);
        var versionTwo = (await versionResponse.Content.ReadFromJsonAsync<ProfileRecord>(ContractJson.Options))!;
        Assert.Equal(2, versionTwo.Version);
        Assert.Equal(2, (await hub.Client.GetFromJsonAsync<ProfileVersionRecord[]>($"/v1/profiles/{original.Id}/versions", ContractJson.Options))!.Length);

        var export = await hub.Client.GetFromJsonAsync<ProfileArchive>($"/v1/profiles/{original.Id}/export", ContractJson.Options);
        Assert.Equal(new ProfileArchive(1, "Call", ProfileMode.Balanced), export);
        var importResponse = await hub.Client.PostAsJsonAsync("/v1/profiles/import", export, ContractJson.Options);
        Assert.Equal(HttpStatusCode.Created, importResponse.StatusCode);
        var imported = (await importResponse.Content.ReadFromJsonAsync<ProfileRecord>(ContractJson.Options))!;
        Assert.NotEqual(original.Id, imported.Id);
        Assert.Equal(ProfileMode.Balanced, imported.BaseMode);
        Assert.Empty((await hub.Client.GetFromJsonAsync<CommandRecord[]>("/v1/commands", ContractJson.Options))!);
    }

    [Fact]
    public async Task Profile_archive_rejects_unknown_format_and_extra_command_fields()
    {
        using var workspace = new TestWorkspace();
        await using var hub = await RunningHub.Start(workspace.PathFor("profile-import-validation"));
        using var unknownFormat = new StringContent("""{"formatVersion":2,"name":"Unsafe","baseMode":"balanced"}""", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await hub.Client.PostAsync("/v1/profiles/import", unknownFormat)).StatusCode);
        using var unsafeMode = new StringContent("""{"formatVersion":1,"name":"Unsafe","baseMode":"balanced","mode_command":"anything"}""", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await hub.Client.PostAsync("/v1/profiles/import", unsafeMode)).StatusCode);
        Assert.Empty((await hub.Client.GetFromJsonAsync<ProfileRecord[]>("/v1/profiles", ContractJson.Options))!);
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

internal sealed class FixedTimeProvider(DateTimeOffset initial) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = initial;
    public override DateTimeOffset GetUtcNow() => Now;
}
