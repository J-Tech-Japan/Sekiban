using DuckDB.NET.Data;
using ResultBoxes;
using Sekiban.Dcb.ColdEvents;

namespace DcbOrleans.WithoutResult.ApiService.ColdEvents;

public sealed class DuckDbColdObjectStorage : IColdObjectStorage
{
    private readonly string _connectionString;

    public DuckDbColdObjectStorage(string databasePath)
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
                                  data BLOB NOT NULL,
                                  etag VARCHAR NOT NULL,
                                  updated_at TIMESTAMP NOT NULL
                              );
                              """;
        command.ExecuteNonQuery();
    }

    public Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct)
        => ExecuteAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT data, etag FROM cold_objects WHERE path = ?";
            command.Parameters.Add(new DuckDBParameter { Value = ColdStoragePath.Normalize(path) });
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return ResultBox.Error<ColdStorageObject>(new KeyNotFoundException($"Cold object not found: {path}"));
            }

            var data = (byte[])reader.GetValue(0);
            var etag = reader.GetString(1);
            return ResultBox.FromValue(new ColdStorageObject(data, etag));
        });

    public Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
        => ExecuteAsync(async connection =>
        {
            var normalizedPath = ColdStoragePath.Normalize(path);

            using var tx = connection.BeginTransaction();
            string? currentEtag;
            await using (var read = connection.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT etag FROM cold_objects WHERE path = ?";
                read.Parameters.Add(new DuckDBParameter { Value = normalizedPath });
                var value = await read.ExecuteScalarAsync(ct);
                currentEtag = value?.ToString();
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
                                 VALUES (?, ?, ?, ?)
                                 ON CONFLICT(path) DO UPDATE SET
                                     data = EXCLUDED.data,
                                     etag = EXCLUDED.etag,
                                     updated_at = EXCLUDED.updated_at;
                                 """;
            upsert.Parameters.Add(new DuckDBParameter { Value = normalizedPath });
            upsert.Parameters.Add(new DuckDBParameter { Value = data });
            upsert.Parameters.Add(new DuckDBParameter { Value = ColdStoragePath.ComputeEtag(data) });
            upsert.Parameters.Add(new DuckDBParameter { Value = DateTime.UtcNow });
            await upsert.ExecuteNonQueryAsync(ct);
            tx.Commit();
            return ResultBox.FromValue(true);
        });

    public Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct)
        => ExecuteAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT path FROM cold_objects WHERE path LIKE ? ORDER BY path";
            command.Parameters.Add(new DuckDBParameter { Value = ColdStoragePath.Normalize(prefix) + "%" });

            await using var reader = await command.ExecuteReaderAsync(ct);
            var list = new List<string>();
            while (await reader.ReadAsync(ct))
            {
                list.Add(reader.GetString(0));
            }

            return ResultBox.FromValue<IReadOnlyList<string>>(list);
        });

    public Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct)
        => ExecuteAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM cold_objects WHERE path = ?";
            command.Parameters.Add(new DuckDBParameter { Value = ColdStoragePath.Normalize(path) });
            var affected = await command.ExecuteNonQueryAsync(ct);
            return ResultBox.FromValue(affected > 0);
        });

    private async Task<ResultBox<T>> ExecuteAsync<T>(Func<DuckDBConnection, Task<ResultBox<T>>> action)
        where T : notnull
    {
        try
        {
            await using var connection = new DuckDBConnection(_connectionString);
            connection.Open();
            return await action(connection);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<T>(ex);
        }
    }
}
