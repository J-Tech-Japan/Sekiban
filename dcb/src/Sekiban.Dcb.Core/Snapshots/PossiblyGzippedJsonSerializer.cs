using System.IO.Compression;
using System.Text.Json;

namespace Sekiban.Dcb.Snapshots;

public static class PossiblyGzippedJsonSerializer
{
    public static async Task<T?> DeserializeAsync<T>(
        Stream source,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        var prefix = new byte[2];
        var read = await source.ReadAsync(prefix.AsMemory(0, 2), cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            return default;
        }

        await using var prefixed = new PrefixBufferedStream(prefix.AsMemory(0, read).ToArray(), source);
        if (read >= 2 && prefix[0] == 0x1f && prefix[1] == 0x8b)
        {
            await using var gzip = new GZipStream(prefixed, CompressionMode.Decompress, leaveOpen: false);
            return await JsonSerializer.DeserializeAsync<T>(gzip, options, cancellationToken).ConfigureAwait(false);
        }

        return await JsonSerializer.DeserializeAsync<T>(prefixed, options, cancellationToken).ConfigureAwait(false);
    }

    private sealed class PrefixBufferedStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly Stream _inner;
        private int _position;

        public PrefixBufferedStream(byte[] prefix, Stream inner)
        {
            _prefix = prefix;
            _inner = inner;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var copied = CopyPrefix(buffer.AsSpan(offset, count));
            if (copied == count)
            {
                return copied;
            }

            return copied + _inner.Read(buffer, offset + copied, count - copied);
        }

        public override int Read(Span<byte> buffer)
        {
            var copied = CopyPrefix(buffer);
            if (copied == buffer.Length)
            {
                return copied;
            }

            return copied + _inner.Read(buffer[copied..]);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var copied = CopyPrefix(buffer.Span);
            if (copied == buffer.Length)
            {
                return copied;
            }

            return copied + await _inner.ReadAsync(buffer[copied..], cancellationToken).ConfigureAwait(false);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int CopyPrefix(Span<byte> destination)
        {
            if (_position >= _prefix.Length || destination.Length == 0)
            {
                return 0;
            }

            var remaining = _prefix.Length - _position;
            var toCopy = Math.Min(destination.Length, remaining);
            _prefix.AsSpan(_position, toCopy).CopyTo(destination);
            _position += toCopy;
            return toCopy;
        }
    }
}
