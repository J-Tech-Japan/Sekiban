using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
using System.Text.Json;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Core registry and dispatcher for multi projectors in DCB (ResultBox-based).
/// </summary>
public interface ICoreMultiProjectorTypes
{
    ResultBox<IMultiProjectionPayload> Project(
        string multiProjectorName,
        IMultiProjectionPayload payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold);

    ResultBox<string> GetProjectorVersion(string multiProjectorName);

    /// <summary>
    ///     Gets all registered projector names.
    /// </summary>
    IReadOnlyList<string> GetAllProjectorNames();

    ResultBox<Func<IMultiProjectionPayload>> GetInitialPayloadGenerator(string multiProjectorName);

    ResultBox<Type> GetProjectorType(string multiProjectorName);

    ResultBox<IMultiProjectionPayload> GenerateInitialPayload(string multiProjectorName);

    ResultBox<IMultiProjectionPayload> Deserialize(
        byte[] data,
        string multiProjectorName,
        JsonSerializerOptions jsonOptions);

    /// <summary>
    ///     Serializes a multi-projection payload to bytes with size information.
    ///     Uses custom serialization if registered, otherwise falls back to JSON + Gzip.
    ///     Caller MUST always supply safeWindowThreshold; passing empty or null is treated as error.
    /// </summary>
    /// <param name="projectorName">Name of the projector</param>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="safeWindowThreshold">Safe window threshold (SortableUniqueId string) used when projector needs safe-only construction</param>
    /// <param name="payload">The payload to serialize</param>
    /// <returns>SerializationResult containing serialized data and size information</returns>
    ResultBox<SerializationResult> Serialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload);

    /// <summary>
    ///     Stream-oriented variant of <see cref="Serialize" />. Writes the serialized
    ///     (and optionally compressed) payload bytes directly to <paramref name="destination" />
    ///     without materializing a <see cref="byte" />[] in managed memory.
    ///     The default implementation delegates to <see cref="Serialize" /> and copies the
    ///     resulting bytes to the destination stream for backward compatibility; registries
    ///     that own their serialization pipeline (such as
    ///     <see cref="Sekiban.Dcb.MultiProjections.GzipCompression" />-based JSON serializers)
    ///     should override this method with a fully streaming implementation.
    /// </summary>
    /// <param name="projectorName">Name of the projector</param>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="safeWindowThreshold">Safe window threshold (SortableUniqueId string)</param>
    /// <param name="payload">The payload to serialize</param>
    /// <param name="destination">Writable stream that will receive the serialized payload</param>
    /// <returns>
    ///     <see cref="SerializationSizeInfo" /> describing the original and compressed byte
    ///     counts written to <paramref name="destination" />.
    /// </returns>
    ResultBox<SerializationSizeInfo> SerializeToStream(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload,
        Stream destination)
    {
        var result = Serialize(projectorName, domainTypes, safeWindowThreshold, payload);
        if (!result.IsSuccess)
        {
            return ResultBox.Error<SerializationSizeInfo>(result.GetException());
        }

        var value = result.GetValue();
        if (value.Data is { Length: > 0 })
        {
            destination.Write(value.Data, 0, value.Data.Length);
        }

        return ResultBox.FromValue(
            new SerializationSizeInfo(value.OriginalSizeBytes, value.CompressedSizeBytes));
    }

    /// <summary>
    ///     Deserializes a JSON string to a multi-projection payload.
    ///     Uses custom deserialization if registered, otherwise falls back to standard JSON deserialization.
    ///     Caller MUST always supply safeWindowThreshold; passing empty or null is treated as error.
    /// </summary>
    /// <param name="projectorName">Name of the projector</param>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="safeWindowThreshold">Safe window threshold (SortableUniqueId string) to allow projector building safe internals if needed</param>
    /// <param name="json">JSON string to deserialize</param>
    /// <returns>Deserialized payload</returns>
    ResultBox<IMultiProjectionPayload> Deserialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        byte[] data);

    /// <summary>
    ///     Deserializes raw JSON into a projector payload.
    ///     Default implementation uses GetProjectorType + JsonSerializer and works for reflection-based registries.
    ///     AOT registries should override this to use source-generated JsonTypeInfo instead.
    /// </summary>
    ResultBox<IMultiProjectionPayload> DeserializeJson(
        string projectorName,
        string json,
        DcbDomainTypes domainTypes)
    {
        ResultBox<Type> projectorTypeResult = GetProjectorType(projectorName);
        if (!projectorTypeResult.IsSuccess)
        {
            return ResultBox.Error<IMultiProjectionPayload>(projectorTypeResult.GetException());
        }

        try
        {
            object? deserialized = JsonSerializer.Deserialize(
                json,
                projectorTypeResult.GetValue(),
                domainTypes.JsonSerializerOptions);
            if (deserialized is IMultiProjectionPayload payload)
            {
                return ResultBox.FromValue(payload);
            }

            return ResultBox.Error<IMultiProjectionPayload>(
                new InvalidOperationException(
                    $"JSON for projector '{projectorName}' could not be deserialized to '{projectorTypeResult.GetValue().FullName}'."));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectionPayload>(ex);
        }
    }

    /// <summary>
    ///     Registers a projector with custom serialization support.
    ///     The projector must implement IMultiProjectorWithCustomSerialization interface.
    ///     The projector name is obtained from T.MultiProjectorName.
    /// </summary>
    /// <typeparam name="T">The projector type with custom serialization</typeparam>
    /// <returns>Success or error result</returns>
    ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new();
}
