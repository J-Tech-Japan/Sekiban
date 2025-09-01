using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
using System.Text.Json;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Registry and dispatcher for multi projectors in DCB.
/// </summary>
public interface IMultiProjectorTypes
{
    ResultBox<IMultiProjectionPayload> Project(
        string multiProjectorName,
        IMultiProjectionPayload payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold);

    ResultBox<string> GetProjectorVersion(string multiProjectorName);

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
    /// </summary>
    /// <param name="projectorName">Name of the projector</param>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="payload">The payload to serialize</param>
    /// <returns>Serialized JSON string</returns>
    ResultBox<string> Serialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        IMultiProjectionPayload payload);
    
    /// <summary>
    ///     Deserializes a JSON string to a multi-projection payload.
    ///     Uses custom deserialization if registered, otherwise falls back to standard JSON deserialization.
    /// </summary>
    /// <param name="projectorName">Name of the projector</param>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="json">JSON string to deserialize</param>
    /// <returns>Deserialized payload</returns>
    ResultBox<IMultiProjectionPayload> Deserialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string json);
    
    /// <summary>
    ///     Registers a projector with custom serialization support.
    ///     The projector must implement IMultiProjectorWithCustomSerialization interface.
    ///     The projector name is obtained from T.MultiProjectorName.
    /// </summary>
    /// <typeparam name="T">The projector type with custom serialization</typeparam>
    /// <returns>Success or error result</returns>
    ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
        where T : IMultiProjectorWithCustomSerialization<T>, new();
}
