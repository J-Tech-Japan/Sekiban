using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Shared non-buffering JSON snapshot restore helpers used by optional streaming projector registries.
///     They inspect only the two-byte gzip marker and then let <see cref="JsonSerializer" /> consume the supplied
///     stream directly. The source stream is never disposed by this helper.
/// </summary>
public static class StreamSnapshotPayloadDeserializer
{
    /// <summary>Deserializes raw or gzip-compressed JSON into a reflection-based projector payload.</summary>
    public static async Task<object?> DeserializeJsonAsync(
        Stream source,
        Type payloadType,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(payloadType);
        ArgumentNullException.ThrowIfNull(options);

        await using var prefixed = await PrefixBufferedStream.CreateAsync(source, cancellationToken)
            .ConfigureAwait(false);
        if (prefixed.IsGzip)
        {
            await using var gzip = new GZipStream(prefixed, CompressionMode.Decompress, leaveOpen: true);
            return await JsonSerializer.DeserializeAsync(gzip, payloadType, options, cancellationToken)
                .ConfigureAwait(false);
        }

        return await JsonSerializer.DeserializeAsync(prefixed, payloadType, options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Deserializes raw or gzip-compressed JSON through an AOT <see cref="JsonTypeInfo{T}" />.</summary>
    public static async Task<T?> DeserializeJsonAsync<T>(
        Stream source,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(typeInfo);

        await using var prefixed = await PrefixBufferedStream.CreateAsync(source, cancellationToken)
            .ConfigureAwait(false);
        if (prefixed.IsGzip)
        {
            await using var gzip = new GZipStream(prefixed, CompressionMode.Decompress, leaveOpen: true);
            return await JsonSerializer.DeserializeAsync(gzip, typeInfo, cancellationToken).ConfigureAwait(false);
        }

        return await JsonSerializer.DeserializeAsync(prefixed, typeInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     A two-byte replay wrapper that preserves a non-seekable source's current position. Disposal intentionally
    ///     leaves the underlying source open because stream ownership belongs to the resolver caller.
    /// </summary>
    private sealed class PrefixBufferedStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly Stream _inner;
        private int _prefixPosition;

        private PrefixBufferedStream(byte[] prefix, Stream inner)
        {
            _prefix = prefix;
            _inner = inner;
        }

        public bool IsGzip => _prefix.Length >= 2 && _prefix[0] == 0x1f && _prefix[1] == 0x8b;

        public static async Task<PrefixBufferedStream> CreateAsync(Stream source, CancellationToken cancellationToken)
        {
            var prefix = new byte[2];
            var count = 0;
            while (count < prefix.Length)
            {
                var read = await source.ReadAsync(prefix.AsMemory(count, prefix.Length - count), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                count += read;
            }

            return new PrefixBufferedStream(count == prefix.Length ? prefix : prefix[..count], source);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException("Snapshot restore streams are non-seekable.");
        public override long Position
        {
            get => throw new NotSupportedException("Snapshot restore streams are non-seekable.");
            set => throw new NotSupportedException("Snapshot restore streams are non-seekable.");
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var copied = CopyPrefix(buffer);
            return copied == buffer.Length
                ? copied
                : copied + _inner.Read(buffer[copied..]);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var copied = CopyPrefix(buffer.Span);
            if (copied == buffer.Length)
            {
                return copied;
            }

            return copied + await _inner.ReadAsync(buffer[copied..], cancellationToken).ConfigureAwait(false);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException("Snapshot restore streams are non-seekable.");
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // Deliberately do not dispose _inner. The resolver caller owns the opened payload stream.
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            // Deliberately do not dispose _inner. The resolver caller owns the opened payload stream.
            return base.DisposeAsync();
        }

        private int CopyPrefix(Span<byte> destination)
        {
            if (_prefixPosition >= _prefix.Length || destination.Length == 0)
            {
                return 0;
            }

            var count = Math.Min(destination.Length, _prefix.Length - _prefixPosition);
            _prefix.AsSpan(_prefixPosition, count).CopyTo(destination);
            _prefixPosition += count;
            return count;
        }
    }
}
