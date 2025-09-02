using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Text;

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
}
