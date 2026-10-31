using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ThermalPilot.Contracts;

namespace ThermalPilot.Host;

public sealed class TelemetryStore(ISqliteConnections connections)
{
    public void Prune() => Run(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM telemetry WHERE captured_at < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddHours(-72).ToString("O"));
        command.ExecuteNonQuery();
        return true;
    });
    public void Append(LiveSnapshot snapshot) => Run(connection =>
    {
        using var transaction = connection.BeginTransaction(deferred: false);
        // Bound both age and row count before insertion; data is local and raw only in TP-02.
        using var prune = connection.CreateCommand();
        prune.Transaction = transaction;
        prune.CommandText = """
            DELETE FROM telemetry WHERE captured_at < $cutoff;
            DELETE FROM telemetry WHERE rowid NOT IN (SELECT rowid FROM telemetry ORDER BY rowid DESC LIMIT 19999);
            """;
        prune.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddHours(-72).ToString("O"));
        prune.ExecuteNonQuery();
        var json = JsonSerializer.Serialize(snapshot, ContractJson.Options);
        if (Encoding.UTF8.GetByteCount(json) > 8192) throw new InvalidDataException("Telemetry record exceeds budget.");
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT OR IGNORE INTO telemetry(session_id,sequence,captured_at,document) VALUES($session,$sequence,$at,$json);";
        insert.Parameters.AddWithValue("$session", snapshot.SessionId.ToString("D"));
        insert.Parameters.AddWithValue("$sequence", snapshot.Sequence);
        insert.Parameters.AddWithValue("$at", snapshot.CapturedAtUtc.ToString("O"));
        insert.Parameters.AddWithValue("$json", json);
        insert.ExecuteNonQuery();
        transaction.Commit();
        return true;
    });

    public void RecordGap(DateTimeOffset at, string reason, double seconds) => Run(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO telemetry_gaps(at,reason,seconds) VALUES($at,$reason,$seconds);
            DELETE FROM telemetry_gaps WHERE rowid NOT IN (SELECT rowid FROM telemetry_gaps ORDER BY rowid DESC LIMIT 1000);
            """;
        command.Parameters.AddWithValue("$at", at.ToString("O"));
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$seconds", seconds);
        command.ExecuteNonQuery();
        return true;
    });

    public object Summary()
    {
        Prune();
        return Run(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*),min(captured_at),max(captured_at),(SELECT count(*) FROM telemetry_gaps) FROM telemetry;";
        using var reader = command.ExecuteReader();
        reader.Read();
        return (object)new
        {
            samples = reader.GetInt64(0),
            firstAtUtc = reader.IsDBNull(1) ? null : reader.GetString(1),
            lastAtUtc = reader.IsDBNull(2) ? null : reader.GetString(2),
            gaps = reader.GetInt64(3),
            maximumRows = 20000,
            maximumRawHours = 72
        };
    });
    }

    public LiveSnapshot[] Latest(int limit = 100)
    {
        Prune();
        return Run(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT document FROM telemetry ORDER BY rowid DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
        using var reader = command.ExecuteReader();
        List<LiveSnapshot> samples = [];
        while (reader.Read()) samples.Add(JsonSerializer.Deserialize<LiveSnapshot>(reader.GetString(0), ContractJson.Options)
            ?? throw new InvalidDataException("Missing telemetry."));
        return samples.ToArray();
    });
    }

    private T Run<T>(Func<SqliteConnection, T> operation)
    {
        try { using var connection = connections.Open(); return operation(connection); }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        { throw new StorageFailure(ex is SqliteException sqlite ? "sqlite_" + sqlite.SqliteErrorCode : "telemetry_storage_unavailable", ex); }
    }
}
