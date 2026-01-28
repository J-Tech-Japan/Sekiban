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
