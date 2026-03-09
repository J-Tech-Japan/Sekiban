using System.Security.Cryptography;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DuckDB.NET.Data;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.ColdEvents;

public sealed class DuckDbColdSegmentFormatHandler : IColdSegmentFormatHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string BuildSegmentPath(string serviceId, string fromSortableUniqueId, string toSortableUniqueId)
        => ColdStoragePaths.SegmentPath(serviceId, fromSortableUniqueId, toSortableUniqueId, ".duckdb");

    public Task<IColdSegmentFileBuilder> CreateBuilderAsync(SerializableEvent firstEvent, CancellationToken ct)
        => DuckDbColdSegmentFileBuilder.CreateAsync(this, firstEvent, ct);

    public async Task<ResultBox<ColdSegmentStreamResult>> StreamSegmentAsync(
        Stream data,
        SortableUniqueId? since,
        int? maxCount,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"sekiban-cold-read-{Guid.NewGuid():N}.duckdb");
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

            await using var connection = new DuckDBConnection($"Data Source={tempPath}");
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT sortable_unique_id, event_json_gzip
                FROM cold_events
                WHERE (? IS NULL OR sortable_unique_id > ?)
                ORDER BY sortable_unique_id
                LIMIT ?
                """;
            object sinceValue = since is null ? DBNull.Value : since.Value;
            command.Parameters.Add(new DuckDBParameter { Value = sinceValue });
            command.Parameters.Add(new DuckDBParameter { Value = sinceValue });
            command.Parameters.Add(new DuckDBParameter { Value = maxCount ?? int.MaxValue });

            await using var reader = await command.ExecuteReaderAsync(ct);
            var eventsRead = 0;
            string? lastSortableUniqueId = null;
            while (await reader.ReadAsync(ct))
            {
                var sortableUniqueId = reader.GetString(0);
                var eventJson = ReadStoredEventJson(reader.GetValue(1));
                var evt = JsonSerializer.Deserialize<SerializableEvent>(eventJson, JsonOptions);
                if (evt is null)
                {
                    return ResultBox.Error<ColdSegmentStreamResult>(
                        new InvalidDataException("Cold DuckDB segment row deserialized to null."));
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
            DeleteTempFileIfExists(tempPath);
        }
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

    private static string ReadStoredEventJson(object value) =>
        value switch
        {
            byte[] bytes => DecompressJson(bytes),
            ReadOnlyMemory<byte> memory => DecompressJson(memory.ToArray()),
            Memory<byte> memory => DecompressJson(memory.ToArray()),
            UnmanagedMemoryStream stream => DecompressJson(ReadAllBytes(stream)),
            MemoryStream stream => DecompressJson(stream.ToArray()),
            Stream stream => DecompressJson(ReadAllBytes(stream)),
            string text when text.StartsWith("{", StringComparison.Ordinal) => text,
            string text => DecompressJson(Convert.FromBase64String(text)),
            _ => throw new InvalidDataException(
                $"Unsupported DuckDB event_json_gzip value type: {value.GetType().FullName}")
        };

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string DecompressJson(byte[] compressedJson)
    {
        using var input = new MemoryStream(compressedJson);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static void DeleteTempFileIfExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
            // Temp DuckDB cleanup is best-effort; ignore files that are already gone or still locked.
        }
        catch (UnauthorizedAccessException)
        {
            // Temp DuckDB cleanup is best-effort; ignore files that are still owned by another handle.
        }
    }

    private sealed class DuckDbColdSegmentFileBuilder : IColdSegmentFileBuilder
    {
        private readonly DuckDbColdSegmentFormatHandler _owner;
        private readonly string _filePath;
        private DuckDBConnection? _connection;
        private DuckDBAppender? _appender;
        private long _payloadBytes;
        private bool _completed;

        private DuckDbColdSegmentFileBuilder(
            DuckDbColdSegmentFormatHandler owner,
            string filePath,
            DuckDBConnection connection,
            DuckDBAppender appender)
        {
            _owner = owner;
            _filePath = filePath;
            _connection = connection;
            _appender = appender;
        }

        public int EventCount { get; private set; }
        public string? FromSortableUniqueId { get; private set; }
        public string? ToSortableUniqueId { get; private set; }

        public static async Task<IColdSegmentFileBuilder> CreateAsync(
            DuckDbColdSegmentFormatHandler owner,
            SerializableEvent firstEvent,
            CancellationToken ct)
        {
            var filePath = Path.Combine(Path.GetTempPath(), $"sekiban-cold-segment-{Guid.NewGuid():N}.duckdb");
            var connection = new DuckDBConnection($"Data Source={filePath}");
            await connection.OpenAsync(ct);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    PRAGMA preserve_insertion_order=false;
                    CREATE TABLE cold_events (
                        sortable_unique_id VARCHAR PRIMARY KEY,
                        event_json_gzip BLOB NOT NULL
                    );
                    """;
                await command.ExecuteNonQueryAsync(ct);
            }

            var appender = connection.CreateAppender("cold_events");
            var builder = new DuckDbColdSegmentFileBuilder(owner, filePath, connection, appender);
            await builder.AppendAsync(firstEvent, ct);
            return builder;
        }

        public bool CanAppend(SerializableEvent evt, ColdEventStoreOptions options)
            => EventCount < options.SegmentMaxEvents
               && _payloadBytes + evt.Payload.Length <= options.SegmentMaxBytes;

        public Task AppendAsync(SerializableEvent evt, CancellationToken ct)
        {
            var appender = _appender ?? throw new ObjectDisposedException(nameof(DuckDbColdSegmentFileBuilder));
            var eventJson = JsonSerializer.Serialize(evt, JsonOptions);
            var compressedJson = DuckDbColdSegmentFormatHandler.CompressJson(eventJson);
            appender
                .CreateRow()
                .AppendValue(evt.SortableUniqueIdValue)
                .AppendValue(compressedJson)
                .EndRow();
            ct.ThrowIfCancellationRequested();

            EventCount++;
            _payloadBytes += evt.Payload.Length;
            FromSortableUniqueId ??= evt.SortableUniqueIdValue;
            ToSortableUniqueId = evt.SortableUniqueIdValue;
            return Task.CompletedTask;
        }

        public async Task<ColdSegmentArtifact> CompleteAsync(string serviceId, CancellationToken ct)
        {
            if (_completed)
            {
                throw new InvalidOperationException("Segment builder is already completed.");
            }

            await ReleaseHandlesAsync(closeAppender: true);
            _completed = true;

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
            await ReleaseHandlesAsync(closeAppender: !_completed);
            DeleteTempFileIfExists(_filePath);
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
            var hash = await SHA256.HashDataAsync(stream, ct);
            return Convert.ToHexStringLower(hash);
        }

        private async Task ReleaseHandlesAsync(bool closeAppender)
        {
            Exception? cleanupException = null;

            var appender = _appender;
            _appender = null;
            if (appender is not null)
            {
                if (closeAppender)
                {
                    try
                    {
                        appender.Close();
                    }
                    catch (Exception ex)
                    {
                        cleanupException ??= ex;
                    }
                }

                try
                {
                    appender.Dispose();
                }
                catch (Exception ex)
                {
                    cleanupException ??= ex;
                }
            }

            var connection = _connection;
            _connection = null;
            if (connection is not null)
            {
                try
                {
                    await connection.CloseAsync();
                }
                catch (Exception ex)
                {
                    cleanupException ??= ex;
                }

                try
                {
                    await connection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    cleanupException ??= ex;
                }
            }

            if (cleanupException is not null)
            {
                throw cleanupException;
            }
        }
    }
}
