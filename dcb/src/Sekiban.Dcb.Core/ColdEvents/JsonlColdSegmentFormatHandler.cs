using System.Buffers;
using System.IO.Pipelines;
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
        PipeReader? reader = null;
        try
        {
            reader = PipeReader.Create(
                data,
                new StreamPipeReaderOptions(
                    bufferSize: 1024 * 64,
                    leaveOpen: true));
            var eventsRead = 0;
            string? lastSortableUniqueId = null;
            while (true)
            {
                ReadResult readResult = await reader.ReadAsync(ct).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = readResult.Buffer;
                while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                {
                    ReadOnlySequence<byte> trimmedLine = TrimLine(line);
                    if (IsBlankLine(trimmedLine))
                    {
                        continue;
                    }

                    SerializableEvent? evt = DeserializeLine(trimmedLine);
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
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        await reader.CompleteAsync().ConfigureAwait(false);
                        reader = null;
                        return ResultBox.FromValue(
                            new ColdSegmentStreamResult(eventsRead, lastSortableUniqueId, ReachedEndOfSegment: false));
                    }
                }

                if (readResult.IsCompleted)
                {
                    ReadOnlySequence<byte> trimmedRemainder = TrimLine(buffer);
                    if (!IsBlankLine(trimmedRemainder))
                    {
                        SerializableEvent? evt = DeserializeLine(trimmedRemainder);
                        if (evt is null)
                        {
                            return ResultBox.Error<ColdSegmentStreamResult>(
                                new InvalidDataException("Cold JSONL segment line deserialized to null."));
                        }

                        if (since is null
                            || string.Compare(evt.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0)
                        {
                            await onEvent(evt);
                            eventsRead++;
                            lastSortableUniqueId = evt.SortableUniqueIdValue;
                        }
                    }

                    reader.AdvanceTo(buffer.End);
                    break;
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
            }

            if (reader is not null)
            {
                await reader.CompleteAsync().ConfigureAwait(false);
                reader = null;
            }

            return ResultBox.FromValue(
                new ColdSegmentStreamResult(eventsRead, lastSortableUniqueId, ReachedEndOfSegment: true));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ColdSegmentStreamResult>(ex);
        }
        finally
        {
            if (reader is not null)
            {
                await reader.CompleteAsync().ConfigureAwait(false);
            }
        }
    }

    private static SerializableEvent? DeserializeLine(ReadOnlySequence<byte> line)
    {
        if (line.IsSingleSegment)
        {
            return JsonSerializer.Deserialize<SerializableEvent>(line.FirstSpan, ColdEventJsonOptions.Default);
        }

        byte[] buffer = line.ToArray();
        return JsonSerializer.Deserialize<SerializableEvent>(buffer, ColdEventJsonOptions.Default);
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        SequencePosition? position = buffer.PositionOf((byte)'\n');
        if (position is null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }

    private static ReadOnlySequence<byte> TrimLine(ReadOnlySequence<byte> line)
    {
        while (!line.IsEmpty)
        {
            byte trailing = line.Slice(line.Length - 1, 1).FirstSpan[0];
            if (!IsAsciiWhitespace(trailing))
            {
                break;
            }

            line = line.Slice(0, line.Length - 1);
        }

        while (!line.IsEmpty)
        {
            byte leading = line.FirstSpan[0];
            if (!IsAsciiWhitespace(leading))
            {
                break;
            }

            line = line.Slice(1);
        }

        return line;
    }

    private static bool IsBlankLine(ReadOnlySequence<byte> line)
    {
        foreach (ReadOnlyMemory<byte> segment in line)
        {
            foreach (byte value in segment.Span)
            {
                if (!IsAsciiWhitespace(value))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsAsciiWhitespace(byte value)
        => value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

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
