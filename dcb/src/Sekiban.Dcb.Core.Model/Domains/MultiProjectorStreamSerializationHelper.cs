using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ResultBoxes;
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
    public static ResultBox<SerializationSizeInfo>? ValidateCommonArguments(
        string safeWindowThreshold,
        Stream destination)
    {
        if (string.IsNullOrWhiteSpace(safeWindowThreshold))
        {
            return ResultBox.Error<SerializationSizeInfo>(
                new ArgumentException("safeWindowThreshold must be supplied"));
        }
        if (destination is null)
        {
            return ResultBox.Error<SerializationSizeInfo>(new ArgumentNullException(nameof(destination)));
        }
        return null;
    }

    /// <summary>
    ///     Copy a pre-materialized <see cref="SerializationResult" /> (produced by a custom
    ///     serializer registration) to the destination stream and return the size info.
    /// </summary>
    public static ResultBox<SerializationSizeInfo> CopyCustomResultToStream(
        Stream destination,
        SerializationResult customResult)
    {
        try
        {
            if (customResult.Data is { Length: > 0 } data)
            {
                destination.Write(data, 0, data.Length);
            }
            return ResultBox.FromValue(new SerializationSizeInfo(
                customResult.OriginalSizeBytes,
                customResult.CompressedSizeBytes));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializationSizeInfo>(ex);
        }
    }

    /// <summary>
    ///     Streams the payload as gzipped JSON directly into the destination stream and
    ///     returns the original (pre-compression) and compressed byte counts.
    /// </summary>
    public static ResultBox<SerializationSizeInfo> WriteGzipJsonToStream(
        Stream destination,
        IMultiProjectionPayload payload,
        JsonSerializerOptions jsonOptions)
    {
        try
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
            return ResultBox.FromValue(new SerializationSizeInfo(originalSize, compressedSize));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializationSizeInfo>(ex);
        }
    }

    /// <summary>
    ///     AOT-friendly overload that streams gzipped JSON via a strongly-typed
    ///     <see cref="JsonTypeInfo{T}" />.
    /// </summary>
    public static ResultBox<SerializationSizeInfo> WriteGzipJsonToStream<T>(
        Stream destination,
        T payload,
        JsonTypeInfo<T> typeInfo)
    {
        try
        {
            var startPosition = destination.CanSeek ? destination.Position : 0L;
            var originalSize = GzipCompression.CompressJsonToStream(destination, payload, typeInfo);
            var compressedSize = destination.CanSeek
                ? Math.Max(0, destination.Position - startPosition)
                : originalSize;
            return ResultBox.FromValue(new SerializationSizeInfo(originalSize, compressedSize));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializationSizeInfo>(ex);
        }
    }
}
