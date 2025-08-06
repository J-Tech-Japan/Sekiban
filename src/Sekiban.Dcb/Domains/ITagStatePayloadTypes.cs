using Sekiban.Dcb.Tags;
using ResultBoxes;

namespace Sekiban.Dcb.Domains;

/// <summary>
/// Interface for managing tag state payload types
/// Provides type information and deserialization capabilities for tag state payloads
/// </summary>
public interface ITagStatePayloadTypes
{
    /// <summary>
    /// Gets the Type for a tag state payload by name
    /// </summary>
    /// <param name="payloadName">The name of the payload type</param>
    /// <returns>The Type of the payload, or null if not found</returns>
    Type? GetPayloadType(string payloadName);
    
    /// <summary>
    /// Deserializes a tag state payload from JSON bytes
    /// </summary>
    /// <param name="payloadName">The name of the payload type</param>
    /// <param name="jsonBytes">The JSON bytes to deserialize</param>
    /// <returns>ResultBox containing the deserialized payload or error</returns>
    ResultBox<ITagStatePayload> DeserializePayload(string payloadName, byte[] jsonBytes);
}