using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.ColdEvents;

public sealed class SqliteColdSegmentFormatHandler : IColdSegmentFormatHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string BuildSegmentPath(string serviceId, string fromSortableUniqueId, string toSortableUniqueId)
        => ColdStoragePaths.SegmentPath(serviceId, fromSortableUniqueId, toSortableUniqueId, ".sqlite");

    public Task<IColdSegmentFileBuilder> CreateBuilderAsync(SerializableEvent firstEvent, CancellationToken ct)
        => SqliteColdSegmentFileBuilder.CreateAsync(this, firstEvent, ct);

    public async Task<ResultBox<ColdSegmentStreamResult>> StreamSegmentAsync(
        Stream data,
        SortableUniqueId? since,
        int? maxCount,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"sekiban-cold-read-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using (var file = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (data.CanSeek)
                {
                    data.Position = 0;
                }

                await data.CopyToAsync(file, ct);
            }

            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = tempPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private
            }.ToString());
            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT sortable_unique_id, event_json_gzip
                FROM cold_events
                WHERE ($since IS NULL OR sortable_unique_id > $since)
                ORDER BY sortable_unique_id
                LIMIT $maxCount;
                """;
            command.Parameters.AddWithValue("$since", since?.Value ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$maxCount", maxCount ?? int.MaxValue);

            await using var reader = await command.ExecuteReaderAsync(ct);
            var eventsRead = 0;
            string? lastSortableUniqueId = null;
            while (await reader.ReadAsync(ct))
            {
                var sortableUniqueId = reader.GetString(0);
                await using var compressedStream = reader.GetStream(1);
                var eventJson = DecompressJson(await ReadAllBytesAsync(compressedStream, ct));
                var evt = JsonSerializer.Deserialize<SerializableEvent>(eventJson, JsonOptions);
                if (evt is null)
                {
                    return ResultBox.Error<ColdSegmentStreamResult>(
                        new InvalidDataException("Cold SQLite segment row deserialized to null."));
                }

                await onEvent(evt);
                eventsRead++;
                lastSortableUniqueId = sortableUniqueId;
            }

            var reachedEndOfSegment = !maxCount.HasValue || eventsRead < maxCount.Value;
            return ResultBox.FromValue(
                new ColdSegmentStreamResult(eventsRead, lastSortableUniqueId, reachedEndOfSegment));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ColdSegmentStreamResult>(ex);
        }
        finally
        {
            DeleteFileIfExists(tempPath);
        }
    }

    private static string DecompressJson(byte[] compressedJson)
    {
        using var input = new MemoryStream(compressedJson);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        return memory.ToArray();
    }

    private static bool DeleteFileIfExists(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class SqliteColdSegmentFileBuilder : IColdSegmentFileBuilder
    {
        private readonly SqliteColdSegmentFormatHandler _owner;
        private readonly string _filePath;
        private readonly SqliteConnection _connection;
        private readonly SqliteTransaction _transaction;
        private long _payloadBytes;
        private bool _completed;

        private SqliteColdSegmentFileBuilder(
            SqliteColdSegmentFormatHandler owner,
            string filePath,
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            _owner = owner;
            _filePath = filePath;
            _connection = connection;
            _transaction = transaction;
        }

        public int EventCount { get; private set; }
        public string? FromSortableUniqueId { get; private set; }
        public string? ToSortableUniqueId { get; private set; }

        public static async Task<IColdSegmentFileBuilder> CreateAsync(
            SqliteColdSegmentFormatHandler owner,
            SerializableEvent firstEvent,
            CancellationToken ct)
        {
            var filePath = Path.Combine(Path.GetTempPath(), $"sekiban-cold-segment-{Guid.NewGuid():N}.sqlite");
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = filePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private
            }.ToString());
            await connection.OpenAsync(ct);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    PRAGMA journal_mode=OFF;
                    PRAGMA synchronous=OFF;
                    PRAGMA temp_store=MEMORY;
                    CREATE TABLE cold_events (
                        sortable_unique_id TEXT PRIMARY KEY,
                        event_json_gzip BLOB NOT NULL
                    );
                    """;
                await command.ExecuteNonQueryAsync(ct);
            }

            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
            var builder = new SqliteColdSegmentFileBuilder(owner, filePath, connection, transaction);
            await builder.AppendAsync(firstEvent, ct);
            return builder;
        }

        public bool CanAppend(SerializableEvent evt, ColdEventStoreOptions options)
            => EventCount < options.SegmentMaxEvents
               && _payloadBytes + evt.Payload.Length <= options.SegmentMaxBytes;

        public async Task AppendAsync(SerializableEvent evt, CancellationToken ct)
        {
            var eventJson = JsonSerializer.Serialize(evt, JsonOptions);
            var compressedJson = CompressJson(eventJson);

            await using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText =
                """
                INSERT INTO cold_events(sortable_unique_id, event_json_gzip)
                VALUES ($sortableUniqueId, $eventJsonGzip);
                """;
            command.Parameters.AddWithValue("$sortableUniqueId", evt.SortableUniqueIdValue);
            command.Parameters.AddWithValue("$eventJsonGzip", compressedJson);
            await command.ExecuteNonQueryAsync(ct);

            EventCount++;
            _payloadBytes += evt.Payload.Length;
            FromSortableUniqueId ??= evt.SortableUniqueIdValue;
            ToSortableUniqueId = evt.SortableUniqueIdValue;
        }

        public async Task<ColdSegmentArtifact> CompleteAsync(string serviceId, CancellationToken ct)
        {
            if (_completed)
            {
                throw new InvalidOperationException("Segment builder is already completed.");
            }

            _completed = true;
            await CloseOpenHandlesAsync(commit: true, ct);

            var sizeBytes = new FileInfo(_filePath).Length;
            var sha256 = await ComputeSha256Async(_filePath, ct);
            var path = _owner.BuildSegmentPath(serviceId, FromSortableUniqueId!, ToSortableUniqueId!);
            var info = new ColdSegmentInfo(
                Path: path,
                FromSortableUniqueId: FromSortableUniqueId!,
                ToSortableUniqueId: ToSortableUniqueId!,
                EventCount: EventCount,
                SizeBytes: sizeBytes,
                Sha256: sha256,
                CreatedAtUtc: DateTimeOffset.UtcNow);
            return new ColdSegmentArtifact(_filePath, info);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed)
            {
                await CloseOpenHandlesAsync(commit: false, CancellationToken.None);
            }

            DeleteFileIfExists(_filePath);
        }

        private async Task CloseOpenHandlesAsync(bool commit, CancellationToken ct)
        {
            if (commit)
            {
                await _transaction.CommitAsync(ct);
            }
            else
            {
                await _transaction.RollbackAsync(ct);
            }

            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static byte[] CompressJson(string eventJson)
        {
            var utf8 = Encoding.UTF8.GetBytes(eventJson);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(utf8, 0, utf8.Length);
            }

            return output.ToArray();
        }
    }
}
