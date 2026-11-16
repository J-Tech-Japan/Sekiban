using DuckDB.NET.Data;

namespace Sekiban.Dcb.ColdEvents;

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

    public Task<ColdStorageObject?> GetAsync(string path, CancellationToken ct)
        => ExecuteAsync<ColdStorageObject?>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT data, etag FROM cold_objects WHERE path = ?";
            command.Parameters.Add(new DuckDBParameter { Value = ColdStoragePath.Normalize(path) });
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            var raw = reader.GetValue(0);
            var data = raw switch
            {
                byte[] bytes => bytes,
                Stream stream => await ReadAllBytesAsync(stream, ct),
                _ => throw new InvalidDataException(
                    $"Unsupported DuckDB BLOB value type: {raw.GetType().FullName}")
            };
            var etag = reader.GetString(1);
            return new ColdStorageObject(data, etag);
        });

    public Task<bool> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
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
                    throw new InvalidOperationException($"Conditional write failed: {path} does not exist");
                }

                if (!string.Equals(currentEtag, expectedETag, StringComparison.Ordinal))
                {
                    tx.Rollback();
                    throw new InvalidOperationException($"ETag mismatch at {path}: expected={expectedETag}, actual={currentEtag}");
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
            return true;
        });

    public async Task<bool> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        if (data.CanSeek)
        {
            data.Position = 0;
        }

        await data.CopyToAsync(ms, ct);
        return await PutAsync(path, ms.ToArray(), expectedETag, ct);
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct)
        => ExecuteAsync<IReadOnlyList<string>>(async connection =>
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

            return list;
        });

    public Task<bool> DeleteAsync(string path, CancellationToken ct)
        => ExecuteAsync(async connection =>
        {
            var normalizedPath = ColdStoragePath.Normalize(path);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM cold_objects WHERE path = ?";
            command.Parameters.Add(new DuckDBParameter { Value = normalizedPath });
            await command.ExecuteNonQueryAsync(ct);

            await using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT COUNT(*) FROM cold_objects WHERE path = ?";
            verify.Parameters.Add(new DuckDBParameter { Value = normalizedPath });
            var remaining = await verify.ExecuteScalarAsync(ct);
            var count = remaining is null or DBNull ? 0L : Convert.ToInt64(remaining);
            return count == 0;
        });

    private async Task<T> ExecuteAsync<T>(Func<DuckDBConnection, Task<T>> action)
    {
        await using var connection = new DuckDBConnection(_connectionString);
        await connection.OpenAsync();
        return await action(connection);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}
