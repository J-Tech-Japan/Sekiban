using System.IO.Compression;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Utility class for compressing and decompressing data for Dapr storage
/// </summary>
public static class DaprCompressionUtility
{
    /// <summary>
    /// Compresses data using GZip compression
    /// </summary>
    /// <param name="data">Data to compress</param>
    /// <returns>Compressed data</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(data);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Compresses string data using GZip compression
    /// </summary>
    /// <param name="text">Text to compress</param>
    /// <returns>Compressed data</returns>
    public static byte[] CompressString(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<byte>();
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return Compress(bytes);
    }

    /// <summary>
    /// Decompresses data that was compressed with GZip
    /// </summary>
    /// <param name="compressedData">Compressed data</param>
    /// <returns>Decompressed data</returns>
    public static byte[] Decompress(byte[] compressedData)
    {
        if (compressedData == null || compressedData.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var input = new MemoryStream(compressedData);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Decompresses data to string
    /// </summary>
    /// <param name="compressedData">Compressed data</param>
    /// <returns>Decompressed string</returns>
    public static string DecompressToString(byte[] compressedData)
    {
        if (compressedData == null || compressedData.Length == 0)
        {
            return string.Empty;
        }

        var bytes = Decompress(compressedData);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}