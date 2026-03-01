using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;

public interface IColdBenchmarkStorage : IDisposable
{
    byte[] Get(string path);
    void Put(string path, byte[] data);
}

public sealed class JsonlColdBenchmarkStorage : IColdBenchmarkStorage
{
    private readonly string _root;
    private readonly string _normalizedRoot;
    private readonly string _rootWithSeparator;

    public JsonlColdBenchmarkStorage(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        _normalizedRoot = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _rootWithSeparator = _normalizedRoot + Path.DirectorySeparatorChar;
    }

    public byte[] Get(string path)
    {
        var fullPath = Resolve(path);
        return File.ReadAllBytes(fullPath);
    }

    public void Put(string path, byte[] data)
    {
        var fullPath = Resolve(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(fullPath, data);
    }

    public void Dispose()
    {
    }

    private string Resolve(string path)
    {
        var normalizedPath = path.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_root, normalizedPath));
        var isInRoot =
            fullPath.Equals(_normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        if (!isInRoot)
        {
            throw new InvalidOperationException($"Path escapes the storage root: {path}");
        }
        return fullPath;
    }
}

public sealed class SqliteColdBenchmarkStorage : IColdBenchmarkStorage
{
    private readonly string _connectionString;

    public SqliteColdBenchmarkStorage(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS cold_objects (
                                  path TEXT PRIMARY KEY,
                                  data BLOB NOT NULL
                              );
                              """;
        command.ExecuteNonQuery();
    }

    public byte[] Get(string path)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM cold_objects WHERE path = $path";
        command.Parameters.AddWithValue("$path", path);

        var result = command.ExecuteScalar();
        return result as byte[] ?? throw new KeyNotFoundException($"Path not found: {path}");
    }

    public void Put(string path, byte[] data)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO cold_objects(path, data)
                              VALUES ($path, $data)
                              ON CONFLICT(path) DO UPDATE SET data = excluded.data;
                              """;
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$data", data);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
    }
}

public sealed class DuckDbColdBenchmarkStorage : IColdBenchmarkStorage
{
    private readonly string _connectionString;

    public DuckDbColdBenchmarkStorage(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());
        _connectionString = $"Data Source={fullPath}";

        using var connection = new DuckDBConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS cold_objects (
                                  path VARCHAR PRIMARY KEY,
                                  data BLOB NOT NULL
                              );
                              """;
        command.ExecuteNonQuery();
    }

    public byte[] Get(string path)
    {
        using var connection = new DuckDBConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM cold_objects WHERE path = ?";
        command.Parameters.Add(new DuckDBParameter { Value = path });
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new KeyNotFoundException($"Path not found: {path}");
        }
        return (byte[])reader.GetValue(0);
    }

    public void Put(string path, byte[] data)
    {
        using var connection = new DuckDBConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO cold_objects(path, data)
                              VALUES (?, ?)
                              ON CONFLICT(path) DO UPDATE SET data = EXCLUDED.data;
                              """;
        command.Parameters.Add(new DuckDBParameter { Value = path });
        command.Parameters.Add(new DuckDBParameter { Value = data });
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
    }
}
