using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     Shared helper used by <see cref="ICoreMultiProjectorTypes" /> implementations to
///     emit a serialized multi-projection payload to a destination stream without
///     duplicating boilerplate (argument validation, custom-serializer fallback, seek-based
///     size measurement) across every registry.
///     Centralizing this logic keeps the stream-first persistence path uniform across
///     <c>SimpleMultiProjectorTypes</c>, AOT registries, and custom implementations.
/// </summary>
public static class MultiProjectorStreamSerializationHelper
{
    /// <summary>
    ///     Validate common arguments for the stream-first serialize path.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="safeWindowThreshold" /> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="destination" /> is null.</exception>
    public static void ValidateCommonArguments(
        string safeWindowThreshold,
        Stream destination)
    {
        if (string.IsNullOrWhiteSpace(safeWindowThreshold))
        {
            throw new ArgumentException("safeWindowThreshold must be supplied");
        }
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }
    }

    /// <summary>
    ///     Copy a pre-materialized <see cref="SerializationResult" /> (produced by a custom
    ///     serializer registration) to the destination stream and return the size info.
    /// </summary>
    public static SerializationSizeInfo CopyCustomResultToStream(
        Stream destination,
        SerializationResult customResult)
    {
        if (customResult.Data is { Length: > 0 } data)
        {
            destination.Write(data, 0, data.Length);
        }
        return new SerializationSizeInfo(
            customResult.OriginalSizeBytes,
            customResult.CompressedSizeBytes);
    }

    /// <summary>
    ///     Streams the payload as gzipped JSON directly into the destination stream and
    ///     returns the original (pre-compression) and compressed byte counts.
    /// </summary>
    public static SerializationSizeInfo WriteGzipJsonToStream(
        Stream destination,
        IMultiProjectionPayload payload,
        JsonSerializerOptions jsonOptions)
    {
        var startPosition = destination.CanSeek ? destination.Position : 0L;
        var originalSize = GzipCompression.CompressJsonToStream(
            destination,
            payload,
            payload.GetType(),
            jsonOptions);
        var compressedSize = destination.CanSeek
            ? Math.Max(0, destination.Position - startPosition)
            : originalSize;
        return new SerializationSizeInfo(originalSize, compressedSize);
    }

    /// <summary>
    ///     AOT-friendly overload that streams gzipped JSON via a strongly-typed
    ///     <see cref="JsonTypeInfo{T}" />.
    /// </summary>
    public static SerializationSizeInfo WriteGzipJsonToStream<T>(
        Stream destination,
        T payload,
        JsonTypeInfo<T> typeInfo)
    {
        var startPosition = destination.CanSeek ? destination.Position : 0L;
        var originalSize = GzipCompression.CompressJsonToStream(destination, payload, typeInfo);
        var compressedSize = destination.CanSeek
            ? Math.Max(0, destination.Position - startPosition)
            : originalSize;
        return new SerializationSizeInfo(originalSize, compressedSize);
    }
}
