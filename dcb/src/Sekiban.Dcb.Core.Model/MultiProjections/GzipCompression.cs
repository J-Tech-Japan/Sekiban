using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     GZip compression helper (CompressionLevel.Fastest) for multi projector binary serialization.
/// </summary>
public static class GzipCompression
{
    /// <summary>
    ///     Compress raw bytes with GZip.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            if (!data.IsEmpty)
            {
                var rented = ArrayPool<byte>.Shared.Rent(data.Length);
                try
                {
                    data.CopyTo(rented);
                    gz.Write(rented, 0, data.Length);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    ///     Compress UTF8 string content with GZip.
    /// </summary>
    public static byte[] CompressString(string text) => Compress(Encoding.UTF8.GetBytes(text));

    /// <summary>
    ///     Serialize JSON directly into a GZip stream and return the compressed bytes
    ///     together with the uncompressed byte count.
    /// </summary>
    public static (byte[] CompressedBytes, long OriginalSizeBytes) CompressJson(
        object value,
        Type inputType,
        JsonSerializerOptions options)
    {
        using var output = new MemoryStream();
        long originalSizeBytes;
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var counting = new CountingWriteStream(gzip))
        {
            JsonSerializer.Serialize(counting, value, inputType, options);
            counting.Flush();
            gzip.Flush();
            originalSizeBytes = counting.BytesWritten;
        }

        return (output.ToArray(), originalSizeBytes);
    }

    /// <summary>
    ///     Serialize JSON directly into a GZip stream wrapping <paramref name="destination"/>.
    ///     Returns the uncompressed byte count. The compressed bytes are written straight to the
    ///     provided destination, avoiding a large intermediate byte[] for the final payload.
    /// </summary>
    public static long CompressJsonToStream(
        Stream destination,
        object value,
        Type inputType,
        JsonSerializerOptions options)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        long originalSizeBytes;
        using (var gzip = new GZipStream(destination, CompressionLevel.Fastest, leaveOpen: true))
        using (var counting = new CountingWriteStream(gzip))
        {
            JsonSerializer.Serialize(counting, value, inputType, options);
            counting.Flush();
            gzip.Flush();
            originalSizeBytes = counting.BytesWritten;
        }

        return originalSizeBytes;
    }

    /// <summary>
    ///     Async overload of <see cref="CompressJsonToStream(Stream, object, Type, JsonSerializerOptions)"/>.
    /// </summary>
    public static async Task<long> CompressJsonToStreamAsync(
        Stream destination,
        object value,
        Type inputType,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        long originalSizeBytes;
        var gzip = new GZipStream(destination, CompressionLevel.Fastest, leaveOpen: true);
        await using (gzip.ConfigureAwait(false))
        {
            var counting = new CountingWriteStream(gzip);
            await using (counting.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(counting, value, inputType, options, cancellationToken)
                    .ConfigureAwait(false);
                await counting.FlushAsync(cancellationToken).ConfigureAwait(false);
                await gzip.FlushAsync(cancellationToken).ConfigureAwait(false);
                originalSizeBytes = counting.BytesWritten;
            }
        }

        return originalSizeBytes;
    }

    /// <summary>
    ///     AOT-friendly overload of CompressJson using JsonTypeInfo.
    /// </summary>
    public static (byte[] CompressedBytes, long OriginalSizeBytes) CompressJson<T>(
        T value,
        JsonTypeInfo<T> typeInfo)
    {
        using var output = new MemoryStream();
        long originalSizeBytes;
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var counting = new CountingWriteStream(gzip))
        {
            JsonSerializer.Serialize(counting, value, typeInfo);
            counting.Flush();
            gzip.Flush();
            originalSizeBytes = counting.BytesWritten;
        }
        return (output.ToArray(), originalSizeBytes);
    }

    /// <summary>
    ///     AOT-friendly overload that writes the compressed JSON directly to a destination stream.
    /// </summary>
    public static long CompressJsonToStream<T>(
        Stream destination,
        T value,
        JsonTypeInfo<T> typeInfo)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        long originalSizeBytes;
        using (var gzip = new GZipStream(destination, CompressionLevel.Fastest, leaveOpen: true))
        using (var counting = new CountingWriteStream(gzip))
        {
            JsonSerializer.Serialize(counting, value, typeInfo);
            counting.Flush();
            gzip.Flush();
            originalSizeBytes = counting.BytesWritten;
        }
        return originalSizeBytes;
    }

    /// <summary>
    ///     Decompress GZip bytes to a raw byte array.
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(compressed.Length);
        try
        {
            compressed.CopyTo(rented);
            using var input = new MemoryStream(rented, 0, compressed.Length, writable: false);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            gz.CopyTo(outMs);
            return outMs.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    ///     Decompress GZip bytes into a UTF8 string.
    /// </summary>
    public static string DecompressToString(ReadOnlySpan<byte> compressed)
    {
        var bytes = Decompress(compressed);
        return Encoding.UTF8.GetString(bytes);
    }

    private sealed class CountingWriteStream : Stream
    {
        private readonly Stream _inner;

        public CountingWriteStream(Stream inner)
        {
            _inner = inner;
        }

        public long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _inner.Write(buffer);
            BytesWritten += buffer.Length;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            BytesWritten += buffer.Length;
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            BytesWritten += count;
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }
    }
}
