using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ThermalPilot.Contracts;

namespace ThermalPilot.Host;

public sealed class SqliteStore(ISqliteConnections connections, TimeProvider clock)
{
    public const int SchemaVersion = 5;
    public const int ApplicationId = 0x54504C54;
    public static RetentionPolicy DefaultPolicy { get; } = new(false, 72, 30, 90, 256, false, 0);

    public void Initialize()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(connections.DatabasePath)!); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StorageFailure("storage_directory_unavailable", exception);
        }

        Run(connection =>
        {
            var version = Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
            var application = Convert.ToInt32(Scalar(connection, null, "PRAGMA application_id;"), CultureInfo.InvariantCulture);
            if (version == 0 && application == 0)
            {
                var existingTables = Convert.ToInt64(Scalar(connection, null,
                    "SELECT count(*) FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%';"), CultureInfo.InvariantCulture);
                if (existingTables != 0) throw new InvalidDataException("Refusing to adopt an unrelated database.");
                using var transaction = connection.BeginTransaction(deferred: false);
                using var migration = Sql(connection, transaction, """
                    CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);
                    CREATE TABLE profiles(id TEXT PRIMARY KEY, document TEXT NOT NULL CHECK(json_valid(document)));
                    CREATE TABLE commands(
                        id TEXT PRIMARY KEY, idempotency_key TEXT UNIQUE NOT NULL, request_hash TEXT NOT NULL,
                        profile_id TEXT NOT NULL REFERENCES profiles(id), document TEXT NOT NULL CHECK(json_valid(document)));
                    CREATE TABLE reports(id TEXT PRIMARY KEY, document TEXT NOT NULL CHECK(json_valid(document)));
                    CREATE TABLE events(
                        sequence INTEGER PRIMARY KEY AUTOINCREMENT, document TEXT NOT NULL CHECK(json_valid(document)));
                    CREATE TABLE preferences(key TEXT PRIMARY KEY, document TEXT NOT NULL CHECK(json_valid(document)));
                    INSERT INTO schema_migrations VALUES(1, $now);
                    INSERT INTO preferences VALUES('retention', $policy);
                    PRAGMA application_id = 1414548564;
                    PRAGMA user_version = 1;
                    """, ("$now", clock.GetUtcNow().ToString("O")), ("$policy", JsonSerializer.Serialize(DefaultPolicy, ContractJson.Options)));
                migration.ExecuteNonQuery();
                transaction.Commit();
            }

            if (Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"), CultureInfo.InvariantCulture) == 1)
            {
                AssertSchema(connection, 1);
                using var migration = connection.CreateCommand();
                migration.CommandText = """
                    BEGIN IMMEDIATE;
                    CREATE TABLE telemetry(session_id TEXT NOT NULL,sequence INTEGER NOT NULL,captured_at TEXT NOT NULL,
                        document TEXT NOT NULL CHECK(json_valid(document)),UNIQUE(session_id,sequence));
                    CREATE INDEX telemetry_age ON telemetry(captured_at);
                    CREATE TABLE telemetry_gaps(at TEXT NOT NULL,reason TEXT NOT NULL,seconds REAL NOT NULL);
                    INSERT INTO schema_migrations VALUES(2, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                    PRAGMA user_version=2;
                    COMMIT;
                    """;
                migration.ExecuteNonQuery();
            }
            if (Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"), CultureInfo.InvariantCulture) == 2)
            {
                AssertSchema(connection, 2);
                using var migration = connection.CreateCommand();
                migration.CommandText = """
                    BEGIN IMMEDIATE;
                    CREATE TABLE incidents(id TEXT PRIMARY KEY, marked_at TEXT NOT NULL, document TEXT NOT NULL CHECK(json_valid(document)));
                    CREATE INDEX incidents_marked_at ON incidents(marked_at);
                    INSERT INTO schema_migrations VALUES(3, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                    PRAGMA user_version=3;
                    COMMIT;
                    """;
                migration.ExecuteNonQuery();
            }
            if (Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"), CultureInfo.InvariantCulture) == 3)
            {
                AssertSchema(connection, 3);
                using var migration = connection.CreateCommand();
                migration.CommandText = """
                    BEGIN IMMEDIATE;
                    CREATE TABLE paired_clients(client_id TEXT PRIMARY KEY,display_name TEXT NOT NULL,token_hash TEXT NOT NULL UNIQUE,
                        created_at TEXT NOT NULL,revoked_at TEXT NULL);
                    CREATE INDEX paired_clients_active ON paired_clients(token_hash) WHERE revoked_at IS NULL;
                    INSERT INTO schema_migrations VALUES(4, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                    PRAGMA user_version=4;
                    COMMIT;
                    """;
                migration.ExecuteNonQuery();
            }
            if (Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"), CultureInfo.InvariantCulture) == 4)
            {
                AssertSchema(connection, 4);
                using var transaction = connection.BeginTransaction(deferred: false);
                using (var migration = Sql(connection, transaction, """
                    CREATE TABLE profile_versions(
                        profile_id TEXT NOT NULL REFERENCES profiles(id),
                        version INTEGER NOT NULL CHECK(version > 0),
                        document TEXT NOT NULL CHECK(json_valid(document)),
                        PRIMARY KEY(profile_id, version));
                    """))
                    migration.ExecuteNonQuery();
                using (var profiles = Sql(connection, transaction, "SELECT document FROM profiles;"))
                using (var reader = profiles.ExecuteReader())
                {
                    List<ProfileVersionRecord> versions = [];
                    while (reader.Read())
                    {
                        var profile = Deserialize<ProfileRecord>(reader.GetString(0));
                        versions.Add(new ProfileVersionRecord(profile.Id, profile.Version, profile.BaseMode, profile.CreatedAtUtc));
                    }
                    foreach (var profileVersion in versions)
                        InsertProfileVersion(connection, transaction, profileVersion);
                }
                using (var ledger = Sql(connection, transaction,
                           "INSERT INTO schema_migrations VALUES(5, $now); PRAGMA user_version=5;",
                           ("$now", clock.GetUtcNow().ToString("O"))))
                    ledger.ExecuteNonQuery();
                transaction.Commit();
            }
            AssertCurrent(connection);
            using var wal = Sql(connection, null, "PRAGMA journal_mode = WAL;");
            if (!string.Equals(Convert.ToString(wal.ExecuteScalar(), CultureInfo.InvariantCulture), "wal", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Write-ahead logging could not be enabled.");
            return true;
        });
    }

    public ProfileRecord CreateProfile(CreateProfileRequest request)
    {
        Validation.Profile(request);
        var profile = new ProfileRecord(Guid.NewGuid(), request.Name.Trim(), request.BaseMode, 1, clock.GetUtcNow());
        var version = new ProfileVersionRecord(profile.Id, profile.Version, profile.BaseMode, profile.CreatedAtUtc);
        return Run(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            InsertDocument(connection, transaction, "INSERT INTO profiles(id, document) VALUES($id,$document);", profile.Id, profile);
            InsertProfileVersion(connection, transaction, version);
            AppendEvent(connection, transaction, EventKind.ProfileCreated, profile.Id, Guid.NewGuid());
            transaction.Commit();
            return profile;
        });
    }

    public ProfileRecord? GetProfile(Guid id) => ReadOne<ProfileRecord>("SELECT document FROM profiles WHERE id=$id;", id);
    public IReadOnlyList<ProfileRecord> ListProfiles() => ReadMany<ProfileRecord>("SELECT document FROM profiles ORDER BY rowid DESC LIMIT 100;");

    public IReadOnlyList<ProfileVersionRecord> ListProfileVersions(Guid id) => Run(connection =>
    {
        var profileDocument = Scalar(connection, null, "SELECT document FROM profiles WHERE id=$id;", ("$id", id.ToString("D"))) as string;
        if (profileDocument is null) return (IReadOnlyList<ProfileVersionRecord>)Array.Empty<ProfileVersionRecord>();
        using var command = Sql(connection, null, "SELECT document FROM profile_versions WHERE profile_id=$id ORDER BY version;", ("$id", id.ToString("D")));
        using var reader = command.ExecuteReader();
        List<ProfileVersionRecord> versions = [];
        while (reader.Read()) versions.Add(Deserialize<ProfileVersionRecord>(reader.GetString(0)));
        if (versions.Count == 0)
        {
            var legacy = Deserialize<ProfileRecord>(profileDocument);
            versions.Add(new ProfileVersionRecord(legacy.Id, legacy.Version, legacy.BaseMode, legacy.CreatedAtUtc));
        }
        return (IReadOnlyList<ProfileVersionRecord>)versions;
    });

    public ProfileRecord? CloneProfile(Guid id, CloneProfileRequest request)
    {
        Validation.CloneProfile(request);
        return Run(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            using var sourceQuery = Sql(connection, transaction, "SELECT document FROM profiles WHERE id=$id;", ("$id", id.ToString("D")));
            var sourceDocument = sourceQuery.ExecuteScalar() as string;
            if (sourceDocument is null) return null;
            var source = Deserialize<ProfileRecord>(sourceDocument);
            var clone = new ProfileRecord(Guid.NewGuid(), request.Name.Trim(), source.BaseMode, 1, clock.GetUtcNow());
            var version = new ProfileVersionRecord(clone.Id, clone.Version, clone.BaseMode, clone.CreatedAtUtc);
            InsertDocument(connection, transaction, "INSERT INTO profiles(id, document) VALUES($id,$document);", clone.Id, clone);
            InsertProfileVersion(connection, transaction, version);
            AppendEvent(connection, transaction, EventKind.ProfileCloned, clone.Id, Guid.NewGuid());
            transaction.Commit();
            return clone;
        });
    }

    public ProfileRecord? CreateProfileVersion(Guid id, CreateProfileVersionRequest request)
    {
        Validation.ProfileVersion(request);
        return Run(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            using var profileQuery = Sql(connection, transaction, "SELECT document FROM profiles WHERE id=$id;", ("$id", id.ToString("D")));
            var document = profileQuery.ExecuteScalar() as string;
            if (document is null) return null;
            var profile = Deserialize<ProfileRecord>(document);
            if (profile.BaseMode == request.BaseMode)
                throw new RequestFailure("profile_version_unchanged", "Choose a different supported base mode to create a new version.", 409);
            var now = clock.GetUtcNow();
            var updated = profile with { BaseMode = request.BaseMode, Version = checked(profile.Version + 1) };
            var version = new ProfileVersionRecord(id, updated.Version, updated.BaseMode, now);
            InsertProfileVersion(connection, transaction, version);
            using (var update = Sql(connection, transaction, "UPDATE profiles SET document=$document WHERE id=$id;",
                       ("$document", JsonSerializer.Serialize(updated, ContractJson.Options)), ("$id", id.ToString("D"))))
                update.ExecuteNonQuery();
            AppendEvent(connection, transaction, EventKind.ProfileVersionCreated, id, Guid.NewGuid());
            transaction.Commit();
            return updated;
        });
    }

    public CommandRecord? GetCommand(Guid id) => ReadOne<CommandRecord>("SELECT document FROM commands WHERE id=$id;", id);
    public IReadOnlyList<CommandRecord> ListCommands() => ReadMany<CommandRecord>("SELECT document FROM commands ORDER BY rowid DESC LIMIT 100;");

    public ReportRecord? GetReport(Guid id) => ReadOne<ReportRecord>("SELECT document FROM reports WHERE id=$id;", id);
    public IReadOnlyList<ReportRecord> ListReports() => ReadMany<ReportRecord>("SELECT document FROM reports ORDER BY rowid DESC LIMIT 100;");

    public IReadOnlyList<AuditEvent> ListEvents() => Run(connection =>
    {
        using var command = Sql(connection, null, "SELECT sequence,document FROM events ORDER BY sequence DESC LIMIT 100;");
        using var reader = command.ExecuteReader();
        List<AuditEvent> events = [];
        while (reader.Read()) events.Add(Deserialize<AuditEvent>(reader.GetString(1)) with { Sequence = reader.GetInt64(0) });
        return (IReadOnlyList<AuditEvent>)events;
    });

    public RetentionPolicy GetPolicy() => Run(connection => ReadPolicy(connection));

    public RetentionPolicy SetCollectionEnabled(bool enabled) => Run<RetentionPolicy>(connection =>
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        var current = ReadPolicy(connection, transaction);
        var updated = current with { CollectionEnabled = enabled };
        using var command = Sql(connection, transaction,
            "UPDATE preferences SET document=$document WHERE key='retention';",
            ("$document", JsonSerializer.Serialize(updated, ContractJson.Options)));
        command.ExecuteNonQuery();
        AppendEvent(connection, transaction, EventKind.CollectionPreferenceChanged, Guid.Empty, Guid.NewGuid());
        transaction.Commit();
        return updated;
    });

    public IncidentRecord CreateIncident(CreateIncidentRequest request, LiveSnapshot? snapshot)
    {
        Validation.Incident(request);
        var incident = new IncidentRecord(Guid.NewGuid(), request.Note.Trim(), clock.GetUtcNow(),
            snapshot?.SessionId, snapshot?.Sequence, snapshot?.CapturedAtUtc);
        return Run(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            using var command = Sql(connection, transaction,
                "INSERT INTO incidents(id,marked_at,document) VALUES($id,$at,$document);",
                ("$id", incident.Id.ToString("D")), ("$at", incident.MarkedAtUtc.ToString("O")),
                ("$document", JsonSerializer.Serialize(incident, ContractJson.Options)));
            command.ExecuteNonQuery();
            AppendEvent(connection, transaction, EventKind.IncidentMarked, incident.Id, Guid.NewGuid());
            transaction.Commit();
            return incident;
        });
    }

    public IncidentRecord[] ListIncidents() => Run(connection =>
    {
        using var command = Sql(connection, null, "SELECT document FROM incidents ORDER BY marked_at DESC LIMIT 200;");
        using var reader = command.ExecuteReader();
        List<IncidentRecord> incidents = [];
        while (reader.Read()) incidents.Add(Deserialize<IncidentRecord>(reader.GetString(0)));
        return incidents.ToArray();
    });

    public PairedClientRecord AddPairedClient(string name, string clientToken)
    {
        Validation.PairedClientName(name);
        if (clientToken.Length < 32) throw new ArgumentException("A high-entropy pairing token is required.", nameof(clientToken));
        var client = new PairedClientRecord(Guid.NewGuid(), name.Trim(), clock.GetUtcNow(), null);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientToken)));
        return Run(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            using var command = Sql(connection, transaction,
                "INSERT INTO paired_clients(client_id,display_name,token_hash,created_at) VALUES($id,$name,$hash,$at);",
                ("$id", client.Id.ToString("D")), ("$name", client.Name), ("$hash", hash), ("$at", client.PairedAtUtc.ToString("O")));
            command.ExecuteNonQuery();
            AppendEvent(connection, transaction, EventKind.PhonePaired, client.Id, Guid.NewGuid());
            transaction.Commit();
            return client;
        });
    }

    public bool IsActivePairedClient(string clientToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientToken)));
        return Run(connection => Convert.ToInt64(Scalar(connection, null,
            "SELECT count(*) FROM paired_clients WHERE token_hash=$hash AND revoked_at IS NULL;", ("$hash", hash)), CultureInfo.InvariantCulture) == 1);
    }

    public PairedClientRecord[] ListPairedClients() => Run(connection =>
    {
        using var command = Sql(connection, null, "SELECT client_id,display_name,created_at,revoked_at FROM paired_clients ORDER BY created_at DESC LIMIT 100;");
        using var reader = command.ExecuteReader();
        List<PairedClientRecord> clients = [];
        while (reader.Read()) clients.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
            reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        return clients.ToArray();
    });

    public bool RevokePairedClient(Guid id) => Run(connection =>
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = Sql(connection, transaction,
            "UPDATE paired_clients SET revoked_at=$at WHERE client_id=$id AND revoked_at IS NULL;",
            ("$at", clock.GetUtcNow().ToString("O")), ("$id", id.ToString("D")));
        var changed = command.ExecuteNonQuery() == 1;
        if (changed) AppendEvent(connection, transaction, EventKind.PhoneRevoked, id, Guid.NewGuid());
        transaction.Commit();
        return changed;
    });

    private static RetentionPolicy ReadPolicy(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        var json = Scalar(connection, transaction, "SELECT document FROM preferences WHERE key='retention';") as string
            ?? throw new InvalidDataException("Retention policy is missing.");
        var policy = Deserialize<RetentionPolicy>(json);
        if (policy.CloudEnabled || policy.CloudBudget != 0 || (policy with { CollectionEnabled = false }) != DefaultPolicy)
            throw new InvalidDataException("The stored policy is incompatible with this checkpoint.");
        return policy;
    }

    public StoreSummary Summary() => Run(ReadSummary);

    public void Backup(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new RequestFailure("backup_exists", "The backup destination must be a new file.", 409);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Run(connection =>
            {
                using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString());
                target.Open();
                connection.BackupDatabase(target);
                AssertCurrent(target);
                return true;
            });
            File.Move(temporary, destination, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new StorageFailure("backup_unavailable", exception);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void AssertCurrent(SqliteConnection connection) => AssertSchema(connection, SchemaVersion);

    public static void AssertSupported(SqliteConnection connection)
    {
        var version = Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        if (version is not (1 or 2 or 3 or 4 or SchemaVersion)) throw new InvalidDataException("Unsupported backup schema.");
        AssertSchema(connection, version);
    }

    private static void AssertSchema(SqliteConnection connection, int expectedVersion)
    {
        var version = Convert.ToInt32(Scalar(connection, null, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        var application = Convert.ToInt32(Scalar(connection, null, "PRAGMA application_id;"), CultureInfo.InvariantCulture);
        if (version != expectedVersion || application != ApplicationId)
            throw new InvalidDataException("Unsupported database identity or schema version.");
        if (!string.Equals(Scalar(connection, null, "PRAGMA quick_check;") as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("Database integrity check failed.");
        var tables = Convert.ToInt64(Scalar(connection, null, """
            SELECT count(*) FROM sqlite_schema WHERE type='table'
            AND name IN ('schema_migrations','profiles','commands','reports','events','preferences','profile_versions');
            """), CultureInfo.InvariantCulture);
        var expectedTables = expectedVersion >= 5 ? 7 : 6;
        if (tables != expectedTables || Convert.ToInt64(Scalar(connection, null, "SELECT max(version) FROM schema_migrations;"), CultureInfo.InvariantCulture) != expectedVersion)
            throw new InvalidDataException("The migration ledger is inconsistent.");
        if (expectedVersion >= 2 && Count(connection, "SELECT count(*) FROM sqlite_schema WHERE type='table' AND name IN ('telemetry','telemetry_gaps');") != 2)
            throw new InvalidDataException("Telemetry migration is incomplete.");
        if (expectedVersion >= 3 && Count(connection, "SELECT count(*) FROM sqlite_schema WHERE type='table' AND name='incidents';") != 1)
            throw new InvalidDataException("Incident migration is incomplete.");
        if (expectedVersion >= 4 && Count(connection, "SELECT count(*) FROM sqlite_schema WHERE type='table' AND name='paired_clients';") != 1)
            throw new InvalidDataException("Phone pairing migration is incomplete.");
        if (expectedVersion >= 5 && Count(connection, "SELECT count(*) FROM sqlite_schema WHERE type='table' AND name='profile_versions';") != 1)
            throw new InvalidDataException("Profile version migration is incomplete.");
        ReadPolicy(connection);
    }

    public static StoreSummary ReadSummary(SqliteConnection connection) => new(SchemaVersion,
        Count(connection, "SELECT count(*) FROM profiles;"), Count(connection, "SELECT count(*) FROM commands;"),
        Count(connection, "SELECT count(*) FROM reports;"), Count(connection, "SELECT count(*) FROM events;"));

    private static long Count(SqliteConnection connection, string sql) =>
        Convert.ToInt64(Scalar(connection, null, sql), CultureInfo.InvariantCulture);

    private void AppendEvent(SqliteConnection connection, SqliteTransaction transaction, EventKind kind, Guid entityId, Guid correlationId)
    {
        var auditEvent = new AuditEvent(0, correlationId, kind, "localOwner", entityId, clock.GetUtcNow());
        using var command = Sql(connection, transaction, "INSERT INTO events(document) VALUES($document);",
            ("$document", JsonSerializer.Serialize(auditEvent, ContractJson.Options)));
        command.ExecuteNonQuery();
    }

    private static void InsertDocument<T>(SqliteConnection connection, SqliteTransaction transaction, string sql, Guid id, T value)
    {
        using var command = Sql(connection, transaction, sql, ("$id", id.ToString("D")), ("$document", JsonSerializer.Serialize(value, ContractJson.Options)));
        command.ExecuteNonQuery();
    }

    private static void InsertProfileVersion(SqliteConnection connection, SqliteTransaction transaction, ProfileVersionRecord version)
    {
        using var command = Sql(connection, transaction,
            "INSERT INTO profile_versions(profile_id, version, document) VALUES($id, $version, $document);",
            ("$id", version.ProfileId.ToString("D")), ("$version", version.Version),
            ("$document", JsonSerializer.Serialize(version, ContractJson.Options)));
        command.ExecuteNonQuery();
    }

    private T? ReadOne<T>(string sql, Guid id) where T : class => Run(connection =>
    {
        var document = Scalar(connection, null, sql, ("$id", id.ToString("D"))) as string;
        return document is null ? null : Deserialize<T>(document);
    });

    private IReadOnlyList<T> ReadMany<T>(string sql) => Run(connection =>
    {
        using var command = Sql(connection, null, sql);
        using var reader = command.ExecuteReader();
        List<T> items = [];
        while (reader.Read()) items.Add(Deserialize<T>(reader.GetString(0)));
        return (IReadOnlyList<T>)items;
    });

    private static T Deserialize<T>(string document) =>
        JsonSerializer.Deserialize<T>(document, ContractJson.Options) ?? throw new InvalidDataException("Stored document is missing.");

    private T Run<T>(Func<SqliteConnection, T> operation)
    {
        try
        {
            using var connection = connections.Open();
            return operation(connection);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            var code = exception is SqliteException sqlite ? "sqlite_" + sqlite.SqliteErrorCode : "storage_unavailable";
            throw new StorageFailure(code, exception);
        }
    }

    private static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object Value)[] values)
    {
        using var command = Sql(connection, transaction, sql, values);
        return command.ExecuteScalar();
    }

    private static SqliteCommand Sql(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object Value)[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
