using Microsoft.Data.Sqlite;

namespace ThermalPilot.Host;

public interface ISqliteConnections
{
    string DatabasePath { get; }
    SqliteConnection Open();
}

public sealed class SqliteConnections(string dataDirectory) : ISqliteConnections
{
    public string DatabasePath { get; } = Path.Combine(Path.GetFullPath(dataDirectory), "thermalpilot.db");

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 1
        }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA page_size;";
            var pageSize = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            var maximumPages = 256L * 1024 * 1024 / pageSize;
            command.CommandText = $"PRAGMA max_page_count = {maximumPages};";
            if (Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > maximumPages)
                throw new InvalidDataException("The database already exceeds this checkpoint's page budget.");
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}

public sealed class DatabaseLease : IDisposable
{
    private readonly FileStream stream;

    public DatabaseLease(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        stream = new FileStream(Path.Combine(dataDirectory, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public void Dispose() => stream.Dispose();
}
