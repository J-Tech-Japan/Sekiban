using Microsoft.Data.Sqlite;
using ResultBoxes;
using Sekiban.Dcb.ColdEvents;

namespace DcbOrleans.WithoutResult.ApiService.ColdEvents;

public sealed class SqliteColdObjectStorage : IColdObjectStorage
{
    private readonly string _connectionString;

    public SqliteColdObjectStorage(string databasePath)
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
                                  data BLOB NOT NULL,
                                  etag TEXT NOT NULL,
                                  updated_at TEXT NOT NULL
                              );
                              """;
        command.ExecuteNonQuery();
    }

    public async Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT data, etag FROM cold_objects WHERE path = $path";
            command.Parameters.AddWithValue("$path", ColdStoragePath.Normalize(path));

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return ResultBox.Error<ColdStorageObject>(new KeyNotFoundException($"Cold object not found: {path}"));
            }

            var data = (byte[])reader[0];
            var etag = reader.GetString(1);
            return ResultBox.FromValue(new ColdStorageObject(data, etag));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ColdStorageObject>(ex);
        }
    }

    public async Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
    {
        var normalizedPath = ColdStoragePath.Normalize(path);

        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);
            using var tx = connection.BeginTransaction();

            string? currentEtag = null;
            await using (var read = connection.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT etag FROM cold_objects WHERE path = $path";
                read.Parameters.AddWithValue("$path", normalizedPath);
                var scalar = await read.ExecuteScalarAsync(ct);
                if (scalar is not null && scalar is not DBNull)
                {
                    currentEtag = (string)scalar;
                }
            }

            if (expectedETag is not null)
            {
                if (currentEtag is null)
                {
                    tx.Rollback();
                    return ResultBox.Error<bool>(new InvalidOperationException($"Conditional write failed: {path} does not exist"));
                }

                if (!string.Equals(currentEtag, expectedETag, StringComparison.Ordinal))
                {
                    tx.Rollback();
                    return ResultBox.Error<bool>(new InvalidOperationException($"ETag mismatch at {path}: expected={expectedETag}, actual={currentEtag}"));
                }
            }

            await using var upsert = connection.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText = """
                                 INSERT INTO cold_objects(path, data, etag, updated_at)
                                 VALUES ($path, $data, $etag, $updated)
                                 ON CONFLICT(path) DO UPDATE SET
                                     data = excluded.data,
                                     etag = excluded.etag,
                                     updated_at = excluded.updated_at;
                                 """;
            upsert.Parameters.AddWithValue("$path", normalizedPath);
            upsert.Parameters.AddWithValue("$data", data);
            upsert.Parameters.AddWithValue("$etag", ColdStoragePath.ComputeEtag(data));
            upsert.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            await upsert.ExecuteNonQueryAsync(ct);

            tx.Commit();
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct)
    {
        try
        {
            var normalizedPrefix = ColdStoragePath.Normalize(prefix);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT path FROM cold_objects WHERE path LIKE $prefix ORDER BY path";
            command.Parameters.AddWithValue("$prefix", normalizedPrefix + "%");

            await using var reader = await command.ExecuteReaderAsync(ct);
            var list = new List<string>();
            while (await reader.ReadAsync(ct))
            {
                list.Add(reader.GetString(0));
            }
            return ResultBox.FromValue<IReadOnlyList<string>>(list);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IReadOnlyList<string>>(ex);
        }
    }

    public async Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM cold_objects WHERE path = $path";
            command.Parameters.AddWithValue("$path", ColdStoragePath.Normalize(path));
            var affected = await command.ExecuteNonQueryAsync(ct);
            return ResultBox.FromValue(affected > 0);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }
}
