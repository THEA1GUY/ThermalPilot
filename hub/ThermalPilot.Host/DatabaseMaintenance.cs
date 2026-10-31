using Microsoft.Data.Sqlite;
using System.Text.Json;
using ThermalPilot.Contracts;

namespace ThermalPilot.Host;

public static class DatabaseMaintenance
{
    public static StoreSummary Backup(string dataDirectory, string destination)
    {
        if (!File.Exists(new SqliteConnections(dataDirectory).DatabasePath))
            throw new RequestFailure("database_not_found", "An existing database is required for backup.", 404);
        using var lease = new DatabaseLease(dataDirectory);
        var store = new SqliteStore(new SqliteConnections(dataDirectory), TimeProvider.System, new FakeAdapter());
        store.Initialize();
        store.Backup(destination);
        return store.Summary();
    }

    public static StoreSummary RestoreToNewDirectory(string backupPath, string dataDirectory)
    {
        var connections = new SqliteConnections(dataDirectory);
        RequireFreshTarget(dataDirectory);
        using var lease = new DatabaseLease(dataDirectory);
        RequireFreshTarget(dataDirectory);
        var temporary = connections.DatabasePath + "." + Guid.NewGuid().ToString("N") + ".restore";
        try
        {
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(backupPath),
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 1
            }.ToString()))
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                source.Open();
                SqliteStore.AssertCurrent(source);
                destination.Open();
                source.BackupDatabase(destination);
                SqliteStore.AssertCurrent(destination);
            }
            File.Move(temporary, connections.DatabasePath, overwrite: false);
            var restored = new SqliteStore(connections, TimeProvider.System, new FakeAdapter());
            restored.Initialize();
            restored.GetPolicy();
            return restored.Summary();
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            throw new StorageFailure("restore_unavailable", exception);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void RequireFreshTarget(string dataDirectory)
    {
        if (Directory.Exists(dataDirectory) && Directory.EnumerateFileSystemEntries(dataDirectory)
            .Any(path => Path.GetFileName(path) != "host.lock" || !File.Exists(path)))
            throw new RequestFailure("restore_target_exists", "Restore requires a fresh data directory; the original database is preserved.", 409);
    }
}
