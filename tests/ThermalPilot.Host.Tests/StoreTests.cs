using System.Text.Json;
using Microsoft.Data.Sqlite;
using ThermalPilot.Contracts;
using ThermalPilot.Host;
using Xunit;

namespace ThermalPilot.Host.Tests;

public sealed class StoreTests
{
    [Fact]
    public void Profile_command_report_and_audit_survive_reopening()
    {
        using var workspace = new TestWorkspace();
        var first = workspace.Store();
        var profile = first.CreateProfile(new("Docker coding", ProfileMode.Balanced));
        var request = TestRequests.Command(profile);
        var command = first.SimulateCommand(request);
        var report = first.CreateSyntheticReport(new("Synthetic session", "Fixture data, no measurements."));
        var second = workspace.Store();
        Assert.Equal(profile, second.GetProfile(profile.Id));
        Assert.Equal(command.Command, second.GetCommand(command.Command.Id));
        Assert.Equal(report, second.GetReport(report.Id));
        Assert.Equal(new StoreSummary(1, 1, 1, 1, 3), second.Summary());
        Assert.True(second.SimulateCommand(request).Replayed);
        Assert.Equal(3, second.ListEvents().Count);
    }

    [Fact]
    public async Task Concurrent_duplicate_commands_have_one_durable_outcome()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        var profile = store.CreateProfile(new("Inference", ProfileMode.Turbo));
        var request = TestRequests.Command(profile);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => store.SimulateCommand(request))));
        Assert.Single(results, result => !result.Replayed);
        Assert.Single(results.Select(result => result.Command.Id).Distinct());
        Assert.Equal(1, store.Summary().Commands);
        Assert.Equal(2, store.Summary().Events);
        Assert.All(results, result => Assert.False(result.Command.HardwareChanged));
    }

    [Fact]
    public void Reusing_a_key_for_a_different_request_is_a_conflict()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        var profile = store.CreateProfile(new("Meeting", ProfileMode.Silent));
        var request = TestRequests.Command(profile);
        store.SimulateCommand(request);
        var failure = Assert.Throws<RequestFailure>(() => store.SimulateCommand(request with { CorrelationId = Guid.NewGuid() }));
        Assert.Equal(409, failure.StatusCode);
        Assert.Equal(1, store.Summary().Commands);
    }

    [Fact]
    public void Stale_versions_and_expired_commands_do_not_create_audit_successes()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        var profile = store.CreateProfile(new("Meeting", ProfileMode.Silent));
        Assert.Equal(409, Assert.Throws<RequestFailure>(() => store.SimulateCommand(TestRequests.Command(profile) with { ExpectedProfileVersion = 2 })).StatusCode);
        Assert.Equal(410, Assert.Throws<RequestFailure>(() => store.SimulateCommand(TestRequests.Command(profile) with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) })).StatusCode);
        Assert.Equal(new StoreSummary(1, 1, 0, 0, 1), store.Summary());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nname")]
    public void Invalid_profile_names_are_rejected_without_writes(string name)
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        Assert.Throws<RequestFailure>(() => store.CreateProfile(new(name, ProfileMode.Balanced)));
        Assert.Equal(0, store.Summary().Profiles);
    }

    [Fact]
    public void Text_is_parameterized_instead_of_becoming_SQL()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        const string name = "'); DROP TABLE profiles; --";
        var profile = store.CreateProfile(new(name, ProfileMode.Balanced));
        Assert.Equal(name, store.GetProfile(profile.Id)!.Name);
        Assert.Equal(1, store.Summary().Profiles);
    }

    [Fact]
    public void Migration_does_not_adopt_foreign_or_future_databases()
    {
        using var workspace = new TestWorkspace();
        var connections = new SqliteConnections(workspace.PathFor("foreign"));
        Directory.CreateDirectory(workspace.PathFor("foreign"));
        using (var connection = connections.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE other_application(value TEXT); INSERT INTO other_application VALUES('preserve');";
            command.ExecuteNonQuery();
        }
        var foreign = new SqliteStore(connections, TimeProvider.System, new FakeAdapter());
        Assert.Throws<StorageFailure>(foreign.Initialize);
        using (var connection = connections.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM other_application;";
            Assert.Equal("preserve", command.ExecuteScalar());
        }
        workspace.Store("future");
        var futureConnections = new SqliteConnections(workspace.PathFor("future"));
        using (var connection = futureConnections.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=2;";
            command.ExecuteNonQuery();
        }
        Assert.Throws<StorageFailure>(() => workspace.Store("future"));
    }

    [Fact]
    public void Corrupt_database_fails_without_replacing_it()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.PathFor("corrupt");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "thermalpilot.db");
        const string contents = "Not a SQLite database. Preserve this.";
        File.WriteAllText(path, contents);
        Assert.Throws<StorageFailure>(() => workspace.Store("corrupt"));
        Assert.Equal(contents, File.ReadAllText(path));
    }

    [Fact]
    public void Unwritable_directory_boundary_is_reported()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.PathFor("file-instead-of-directory");
        File.WriteAllText(path, "existing file");
        var store = new SqliteStore(new SqliteConnections(path), TimeProvider.System, new FakeAdapter());
        Assert.Throws<StorageFailure>(store.Initialize);
        Assert.Equal("existing file", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Real_SQLite_readonly_or_full_error_rolls_back_report_and_event(bool readOnly)
    {
        using var workspace = new TestWorkspace();
        var normal = workspace.Store();
        var connections = new SwitchableConnections(new SqliteConnections(workspace.PathFor("data")))
        {
            ReadOnly = readOnly,
            Full = !readOnly
        };
        var failing = new SqliteStore(connections, TimeProvider.System, new FakeAdapter());
        var failure = Assert.Throws<StorageFailure>(() => failing.CreateSyntheticReport(new("Synthetic report", new string('x', 15_000))));
        Assert.Equal(readOnly ? "sqlite_8" : "sqlite_13", failure.Code);
        Assert.Equal(new StoreSummary(1, 0, 0, 0, 0), normal.Summary());
    }

    [Fact]
    public void Backup_and_restore_preserve_records_and_idempotency()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        var profile = store.CreateProfile(new("Coding", ProfileMode.Balanced));
        var request = TestRequests.Command(profile);
        var command = store.SimulateCommand(request);
        var report = store.CreateSyntheticReport(new("Synthetic evidence", "No live data."));
        var backup = workspace.PathFor("snapshot.db");
        DatabaseMaintenance.Backup(workspace.PathFor("data"), backup);
        var summary = DatabaseMaintenance.RestoreToNewDirectory(backup, workspace.PathFor("restored"));
        Assert.Equal(store.Summary(), summary);
        var restored = workspace.Store("restored");
        Assert.Equal(profile, restored.GetProfile(profile.Id));
        Assert.Equal(report, restored.GetReport(report.Id));
        Assert.Equal(command.Command.Id, restored.SimulateCommand(request).Command.Id);
        Assert.True(restored.SimulateCommand(request).Replayed);
        Assert.Equal(3, restored.Summary().Events);
        Assert.Throws<RequestFailure>(() => DatabaseMaintenance.RestoreToNewDirectory(backup, workspace.PathFor("data")));
        Assert.Equal(profile, store.GetProfile(profile.Id));
    }

    [Fact]
    public void Invalid_backup_never_replaces_original_data()
    {
        using var workspace = new TestWorkspace();
        var original = workspace.Store();
        var profile = original.CreateProfile(new("Original", ProfileMode.Balanced));
        var backup = workspace.PathFor("bad-backup.db");
        File.WriteAllText(backup, "invalid");
        Assert.Throws<StorageFailure>(() => DatabaseMaintenance.RestoreToNewDirectory(backup, workspace.PathFor("restore-target")));
        Assert.Equal(profile, original.GetProfile(profile.Id));
        Assert.False(File.Exists(Path.Combine(workspace.PathFor("restore-target"), "thermalpilot.db")));
    }

    [Fact]
    public void Malformed_policy_is_normalized_and_restore_can_retry_same_fresh_target()
    {
        using var workspace = new TestWorkspace();
        var original = workspace.Store();
        var profile = original.CreateProfile(new("Original", ProfileMode.Balanced));
        var bad = workspace.PathFor("bad-policy.db");
        original.Backup(bad);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = bad, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE preferences SET document='{}' WHERE key='retention';";
            command.ExecuteNonQuery();
        }
        var target = workspace.PathFor("restored");
        Assert.Throws<StorageFailure>(() => DatabaseMaintenance.RestoreToNewDirectory(bad, target));
        Assert.False(File.Exists(Path.Combine(target, "thermalpilot.db")));
        Assert.Equal(profile, original.GetProfile(profile.Id));
        var good = workspace.PathFor("good-policy.db");
        original.Backup(good);
        Assert.Equal(original.Summary(), DatabaseMaintenance.RestoreToNewDirectory(good, target));
    }

    [Fact]
    public void Running_host_lease_blocks_other_owner_and_maintenance()
    {
        using var workspace = new TestWorkspace();
        workspace.Store();
        using var lease = new DatabaseLease(workspace.PathFor("data"));
        Assert.Throws<IOException>(() => new DatabaseLease(workspace.PathFor("data")));
        Assert.Throws<IOException>(() => DatabaseMaintenance.Backup(workspace.PathFor("data"), workspace.PathFor("snapshot.db")));
    }

    [Fact]
    public void Policy_and_contracts_cannot_claim_live_hardware()
    {
        using var workspace = new TestWorkspace();
        var policy = workspace.Store().GetPolicy();
        Assert.False(policy.CollectionEnabled);
        Assert.False(policy.CloudEnabled);
        Assert.Equal(0, policy.CloudBudget);
        var adapter = new FakeAdapter();
        Assert.False(adapter.Capabilities.HardwareConnected);
        Assert.False(adapter.Capabilities.HardwareWritesEnabled);
        Assert.True(adapter.Snapshot().IsSynthetic);
        Assert.Null(adapter.Snapshot().CpuTemperature.Value);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateProfileRequest>(
            """{"name":"Unsafe","baseMode":"balanced","mode_command":"do something"}""", ContractJson.Options));
    }
}
