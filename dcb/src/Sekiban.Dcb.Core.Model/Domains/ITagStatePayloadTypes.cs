using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Interface for managing tag state payload types
///     Provides type information and deserialization capabilities for tag state payloads
/// </summary>
public interface ITagStatePayloadTypes
{
    /// <summary>
    ///     Gets the registered payload name for the supplied tag state payload
    /// </summary>
    /// <param name="payload">The payload instance</param>
    /// <returns>The registered payload name</returns>
    /// <exception cref="Exception">Thrown when the payload type is not registered.</exception>
    string GetPayloadName(ITagStatePayload payload);

    /// <summary>
    ///     Gets the Type for a tag state payload by name
    /// </summary>
    /// <param name="payloadName">The name of the payload type</param>
    /// <returns>The Type of the payload, or null if not found</returns>
    Type? GetPayloadType(string payloadName);

    /// <summary>
    ///     Deserializes a tag state payload from JSON bytes
    /// </summary>
    /// <param name="payloadName">The name of the payload type</param>
    /// <param name="jsonBytes">The JSON bytes to deserialize</param>
    /// <returns>The deserialized payload</returns>
    /// <exception cref="Exception">Thrown when the payload type is unknown or the bytes cannot be deserialized.</exception>
    ITagStatePayload DeserializePayload(string payloadName, byte[] jsonBytes);

    /// <summary>
    ///     Serializes a tag state payload to JSON bytes
    /// </summary>
    /// <param name="payload">The payload to serialize</param>
    /// <returns>The serialized JSON bytes</returns>
    /// <exception cref="Exception">Thrown when the payload type is unknown or serialization fails.</exception>
    byte[] SerializePayload(ITagStatePayload payload);
}
