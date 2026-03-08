using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.ColdEvents;

public sealed class JsonlColdSegmentFormatHandler : IColdSegmentFormatHandler
{
    public string BuildSegmentPath(string serviceId, string fromSortableUniqueId, string toSortableUniqueId)
        => ColdStoragePaths.SegmentPath(serviceId, fromSortableUniqueId, toSortableUniqueId, ".jsonl");

    public Task<IColdSegmentFileBuilder> CreateBuilderAsync(SerializableEvent firstEvent, CancellationToken ct)
        => JsonlColdSegmentFileBuilder.CreateAsync(this, firstEvent, ct);

    public async Task<ResultBox<ColdSegmentStreamResult>> StreamSegmentAsync(
        Stream data,
        SortableUniqueId? since,
        int? maxCount,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(
                data,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var eventsRead = 0;
            string? lastSortableUniqueId = null;
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var evt = JsonSerializer.Deserialize<SerializableEvent>(line, ColdEventJsonOptions.Default);
                if (evt is null)
                {
                    return ResultBox.Error<ColdSegmentStreamResult>(
                        new InvalidDataException("Cold JSONL segment line deserialized to null."));
                }

                if (since is not null
                    && string.Compare(evt.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) <= 0)
                {
                    continue;
                }

                await onEvent(evt);
                eventsRead++;
                lastSortableUniqueId = evt.SortableUniqueIdValue;
                if (maxCount.HasValue && eventsRead >= maxCount.Value)
                {
                    return ResultBox.FromValue(
                        new ColdSegmentStreamResult(eventsRead, lastSortableUniqueId, ReachedEndOfSegment: false));
                }
            }

            return ResultBox.FromValue(
                new ColdSegmentStreamResult(eventsRead, lastSortableUniqueId, ReachedEndOfSegment: true));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ColdSegmentStreamResult>(ex);
        }
    }

    private sealed class JsonlColdSegmentFileBuilder : IColdSegmentFileBuilder
    {
        private readonly JsonlColdSegmentFormatHandler _owner;
        private readonly string _filePath;
        private readonly FileStream _stream;
        private readonly StreamWriter _writer;
        private long _payloadBytes;
        private bool _completed;

        private JsonlColdSegmentFileBuilder(
            JsonlColdSegmentFormatHandler owner,
            string filePath,
            FileStream stream,
            StreamWriter writer)
        {
            _owner = owner;
            _filePath = filePath;
            _stream = stream;
            _writer = writer;
        }

        public int EventCount { get; private set; }
        public string? FromSortableUniqueId { get; private set; }
        public string? ToSortableUniqueId { get; private set; }

        public static async Task<IColdSegmentFileBuilder> CreateAsync(
            JsonlColdSegmentFormatHandler owner,
            SerializableEvent firstEvent,
            CancellationToken ct)
        {
            var filePath = Path.Combine(Path.GetTempPath(), $"sekiban-cold-segment-{Guid.NewGuid():N}.jsonl");
            var stream = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
            var builder = new JsonlColdSegmentFileBuilder(owner, filePath, stream, writer);
            await builder.AppendAsync(firstEvent, ct);
            return builder;
        }

        public bool CanAppend(SerializableEvent evt, ColdEventStoreOptions options)
            => EventCount < options.SegmentMaxEvents
               && _payloadBytes + evt.Payload.Length <= options.SegmentMaxBytes;

        public async Task AppendAsync(SerializableEvent evt, CancellationToken ct)
        {
            await JsonlSegmentWriter.WriteEventAsync(evt, _writer);
            EventCount++;
            _payloadBytes += evt.Payload.Length;
            FromSortableUniqueId ??= evt.SortableUniqueIdValue;
            ToSortableUniqueId = evt.SortableUniqueIdValue;
            ct.ThrowIfCancellationRequested();
        }

        public async Task<ColdSegmentArtifact> CompleteAsync(string serviceId, CancellationToken ct)
        {
            if (_completed)
            {
                throw new InvalidOperationException("Segment builder is already completed.");
            }

            _completed = true;
            await CloseOpenHandlesAsync(ct);

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
                await CloseOpenHandlesAsync(CancellationToken.None);
            }

            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch
            {
            }
        }

        private async Task CloseOpenHandlesAsync(CancellationToken ct)
        {
            await _writer.FlushAsync(ct);
            await _stream.FlushAsync(ct);
            await _writer.DisposeAsync();
            await _stream.DisposeAsync();
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
    }
}
