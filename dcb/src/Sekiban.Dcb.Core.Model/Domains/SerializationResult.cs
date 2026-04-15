namespace Sekiban.Dcb.Domains;

/// <summary>
///     Serialization result containing data and size information.
///     Used by custom serializers to report both original and compressed sizes.
/// </summary>
/// <param name="Data">Serialized data (compression controlled by serializer)</param>
/// <param name="OriginalSizeBytes">Size before compression</param>
/// <param name="CompressedSizeBytes">Size after compression (same as OriginalSizeBytes if not compressed)</param>
public record SerializationResult(
    byte[] Data,
    long OriginalSizeBytes,
    long CompressedSizeBytes)
{
    /// <summary>
    ///     Compression ratio (0.0 to 1.0, lower is better compression)
    /// </summary>
    public double CompressionRatio => OriginalSizeBytes > 0
        ? (double)CompressedSizeBytes / OriginalSizeBytes
        : 1.0;
}

/// <summary>
///     Size information produced by a stream-oriented serialization pass.
///     Used by the stream-first multi-projection persistence path so callers can
///     report both uncompressed and compressed sizes without holding the serialized
///     <see cref="byte"/>[] in managed memory.
/// </summary>
/// <param name="OriginalSizeBytes">Size before compression (number of bytes written to the counting writer)</param>
/// <param name="CompressedSizeBytes">Size after compression (number of bytes written to the destination stream)</param>
public record SerializationSizeInfo(long OriginalSizeBytes, long CompressedSizeBytes)
{
    public double CompressionRatio => OriginalSizeBytes > 0
        ? (double)CompressedSizeBytes / OriginalSizeBytes
        : 1.0;
}
