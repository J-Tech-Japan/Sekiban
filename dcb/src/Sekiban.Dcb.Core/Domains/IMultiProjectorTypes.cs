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
    ///     Serializes a multi-projection payload to JSON string.
    ///     Uses custom serialization if registered, otherwise falls back to standard JSON serialization.
    ///     Caller MUST always supply safeWindowThreshold; passing empty or null is treated as error.
    /// </summary>
    /// <param name="projectorName">Name of the projector</param>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="safeWindowThreshold">Safe window threshold (SortableUniqueId string) used when projector needs safe-only construction</param>
    /// <param name="payload">The payload to serialize</param>
    /// <returns>Serialized JSON string</returns>
    ResultBox<byte[]> Serialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload);

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
    ///     Registers a projector with custom serialization support.
    ///     The projector must implement IMultiProjectorWithCustomSerialization interface.
    ///     The projector name is obtained from T.MultiProjectorName.
    /// </summary>
    /// <typeparam name="T">The projector type with custom serialization</typeparam>
    /// <returns>Success or error result</returns>
    ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new();
}
