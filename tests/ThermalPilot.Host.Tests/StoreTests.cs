using System.Text.Json;
using Microsoft.Data.Sqlite;
using ThermalPilot.Contracts;
using ThermalPilot.Host;
using Xunit;

namespace ThermalPilot.Host.Tests;

public sealed class StoreTests
{
    [Fact]
    public void Profile_and_audit_survive_reopening()
    {
        using var workspace = new TestWorkspace();
        var profile = workspace.Store().CreateProfile(new("Coding", ProfileMode.Balanced));
        var reopened = workspace.Store();
        Assert.Equal(profile, reopened.GetProfile(profile.Id));
        Assert.Equal(new StoreSummary(4, 1, 0, 0, 1), reopened.Summary());
        Assert.Single(reopened.ListEvents());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nname")]
    public void Invalid_profiles_leave_no_rows(string name)
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        Assert.Throws<RequestFailure>(() => store.CreateProfile(new(name, ProfileMode.Balanced)));
        Assert.Equal(0, store.Summary().Profiles);
    }

    [Fact]
    public void SQL_text_is_saved_as_data()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        const string name = "'); DROP TABLE profiles; --";
        var profile = store.CreateProfile(new(name, ProfileMode.Balanced));
        Assert.Equal(name, store.GetProfile(profile.Id)!.Name);
        Assert.Equal(1, store.Summary().Profiles);
    }

    [Fact]
    public void Foreign_future_and_corrupt_databases_are_preserved()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(workspace.PathFor("foreign"));
        var foreign = new SqliteConnections(workspace.PathFor("foreign"));
        using (var connection = foreign.Open())
        {
            using var sql = connection.CreateCommand();
            sql.CommandText = "CREATE TABLE other(value TEXT); INSERT INTO other VALUES('preserve');";
            sql.ExecuteNonQuery();
        }
        Assert.Throws<StorageFailure>(() => workspace.Store("foreign"));
        using (var connection = foreign.Open())
        {
            using var sql = connection.CreateCommand();
            sql.CommandText = "SELECT value FROM other;";
            Assert.Equal("preserve", sql.ExecuteScalar());
        }
        workspace.Store("future");
        using (var connection = new SqliteConnections(workspace.PathFor("future")).Open())
        {
            using var sql = connection.CreateCommand();
            sql.CommandText = "PRAGMA user_version=99;";
            sql.ExecuteNonQuery();
        }
        Assert.Throws<StorageFailure>(() => workspace.Store("future"));
        Directory.CreateDirectory(workspace.PathFor("corrupt"));
        var corrupt = Path.Combine(workspace.PathFor("corrupt"), "thermalpilot.db");
        File.WriteAllText(corrupt, "not a SQLite database");
        Assert.Throws<StorageFailure>(() => workspace.Store("corrupt"));
        Assert.Equal("not a SQLite database", File.ReadAllText(corrupt));
    }

    [Fact]
    public void Backup_restore_preserves_profiles_and_original_database()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        var profile = store.CreateProfile(new("Meeting", ProfileMode.Silent));
        var backup = workspace.PathFor("snapshot.db");
        store.Backup(backup);
        Assert.Throws<RequestFailure>(() => store.Backup(backup));
        var result = DatabaseMaintenance.RestoreToNewDirectory(backup, workspace.PathFor("restored"));
        Assert.Equal(1, result.Profiles);
        Assert.Equal(profile, workspace.Store("restored").GetProfile(profile.Id));
        Assert.Equal(profile, store.GetProfile(profile.Id));
        Assert.Throws<RequestFailure>(() => DatabaseMaintenance.RestoreToNewDirectory(backup, workspace.PathFor("data")));
    }

    [Fact]
    public void Schema_one_backup_is_migrated_in_staging_before_publication()
    {
        using var workspace = new TestWorkspace();
        var profile = workspace.Store().CreateProfile(new("Existing", ProfileMode.Balanced));
        using (var connection = new SqliteConnections(workspace.PathFor("data")).Open())
        {
            using var sql = connection.CreateCommand();
            sql.CommandText = "DROP TABLE paired_clients; DROP TABLE incidents; DROP TABLE telemetry; DROP TABLE telemetry_gaps; DELETE FROM schema_migrations WHERE version>1; PRAGMA user_version=1;";
            sql.ExecuteNonQuery();
        }
        var backup = Path.Combine(workspace.PathFor("data"), "thermalpilot.db");
        Assert.Equal(4, DatabaseMaintenance.RestoreToNewDirectory(backup, workspace.PathFor("restored")).SchemaVersion);
        Assert.Equal(profile, workspace.Store("restored").GetProfile(profile.Id));
        using var original = new SqliteConnections(workspace.PathFor("data")).Open();
        using var check = original.CreateCommand();
        check.CommandText = "PRAGMA user_version;";
        Assert.Equal(1L, check.ExecuteScalar());
    }

    [Fact]
    public void Incident_notes_and_audit_survive_reopening_without_a_live_snapshot()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        var incident = store.CreateIncident(new("Fan noise during a call"), null);
        var reopened = workspace.Store();
        Assert.Equal(incident, Assert.Single(reopened.ListIncidents()));
        Assert.Null(incident.SnapshotSequence);
        Assert.Contains(reopened.ListEvents(), item => item.Kind == EventKind.IncidentMarked && item.EntityId == incident.Id);
    }

    [Fact]
    public void Paired_tokens_are_stored_hashed_and_revoke_immediately()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        const string token = "phone-session-token-that-is-long-and-random-123456";
        var client = store.AddPairedClient("Android phone", token);
        Assert.True(store.IsActivePairedClient(token));
        Assert.False(store.IsActivePairedClient("invalid-client-token-value-123456789"));
        Assert.Null(client.RevokedAtUtc);
        Assert.True(store.RevokePairedClient(client.Id));
        Assert.False(store.IsActivePairedClient(token));
        Assert.NotNull(Assert.Single(store.ListPairedClients()).RevokedAtUtc);
    }

    [Fact]
    public void Invalid_backup_policy_fails_cleanly_and_valid_retry_succeeds()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        store.CreateProfile(new("Saved", ProfileMode.Balanced));
        var valid = workspace.PathFor("valid.db");
        var invalid = workspace.PathFor("invalid.db");
        store.Backup(valid);
        File.Copy(valid, invalid);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = invalid, Pooling = false }.ToString()))
        {
            connection.Open();
            using var sql = connection.CreateCommand();
            sql.CommandText = "UPDATE preferences SET document='{}' WHERE key='retention';";
            sql.ExecuteNonQuery();
        }
        Assert.Throws<StorageFailure>(() => DatabaseMaintenance.RestoreToNewDirectory(invalid, workspace.PathFor("restored")));
        Assert.Equal(1, DatabaseMaintenance.RestoreToNewDirectory(valid, workspace.PathFor("restored")).Profiles);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Native_read_only_or_full_storage_does_not_report_a_saved_profile(bool readOnly)
    {
        using var workspace = new TestWorkspace();
        workspace.Store();
        var connections = new SwitchableConnections(new SqliteConnections(workspace.PathFor("data"))) { ReadOnly = readOnly, Full = !readOnly };
        var store = new SqliteStore(connections, TimeProvider.System);
        var failure = Assert.Throws<StorageFailure>(() =>
        {
            for (int i = 0; i < 1000; i++) store.CreateProfile(new(new string('x', 80), ProfileMode.Balanced));
        });
        Assert.Equal(readOnly ? "sqlite_8" : "sqlite_13", failure.Code);
        connections.ReadOnly = connections.Full = false;
        Assert.Equal(store.Summary().Profiles, store.Summary().Events);
    }

    [Fact]
    public void Exclusive_lease_and_unusable_directory_fail_explicitly()
    {
        using var workspace = new TestWorkspace();
        using var lease = new DatabaseLease(workspace.PathFor("lease"));
        Assert.Throws<IOException>(() => new DatabaseLease(workspace.PathFor("lease")));
        File.WriteAllText(workspace.PathFor("file"), "preserve");
        Assert.Throws<StorageFailure>(() => workspace.Store("file"));
        Assert.Equal("preserve", File.ReadAllText(workspace.PathFor("file")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(32769)]
    public async Task Invalid_wire_lengths_are_rejected(int length)
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(length));
        await Assert.ThrowsAsync<InvalidDataException>(() => BridgeWire.ReadAsync<BridgeRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Expired_rows_are_removed_even_when_live_collection_is_disabled()
    {
        using var workspace = new TestWorkspace();
        workspace.Store();
        using (var connection = new SqliteConnections(workspace.PathFor("data")).Open())
        {
            using var sql = connection.CreateCommand();
            sql.CommandText = "INSERT INTO telemetry VALUES('expired-input',1,$at,'{}');";
            sql.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.AddDays(-4).ToString("O"));
            sql.ExecuteNonQuery();
        }
        await using var hub = await RunningHub.Start(workspace.PathFor("data"));
        var history = await hub.Client.GetStringAsync("/v1/telemetry/history");
        Assert.Equal("[]", history);
        using var verify = new SqliteConnections(workspace.PathFor("data")).Open();
        using var query = verify.CreateCommand();
        query.CommandText = "SELECT count(*) FROM telemetry;";
        Assert.Equal(0L, query.ExecuteScalar());
    }

    [Fact]
    public void Shell_fields_and_default_cloud_spend_are_rejected()
    {
        using var workspace = new TestWorkspace();
        var policy = workspace.Store().GetPolicy();
        Assert.False(policy.CollectionEnabled);
        Assert.False(policy.CloudEnabled);
        Assert.Equal(0, policy.CloudBudget);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateProfileRequest>(
            """{"name":"Unsafe","baseMode":"balanced","mode_command":"shell"}""", ContractJson.Options));
    }

    [Fact]
    public void Collection_preference_is_saved_and_survives_reopening()
    {
        using var workspace = new TestWorkspace();
        var store = workspace.Store();
        Assert.True(store.SetCollectionEnabled(true).CollectionEnabled);
        Assert.True(workspace.Store().GetPolicy().CollectionEnabled);
        Assert.False(store.SetCollectionEnabled(false).CollectionEnabled);
        Assert.False(workspace.Store().GetPolicy().CollectionEnabled);
        Assert.Contains(store.ListEvents(), item => item.Kind == EventKind.CollectionPreferenceChanged);
    }
}
