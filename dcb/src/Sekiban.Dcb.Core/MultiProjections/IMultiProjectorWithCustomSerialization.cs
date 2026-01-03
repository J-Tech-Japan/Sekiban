using Sekiban.Dcb.Domains;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Core interface for multi-projectors that require custom serialization logic (ResultBox-based).
///     Implementing classes must provide static methods for serialization and deserialization.
///     This interface extends ICoreMultiProjector<TSelf> to include all projection capabilities.
/// </summary>
/// <typeparam name="TSelf">The implementing type itself (CRTP pattern)</typeparam>
public interface ICoreMultiProjectorWithCustomSerialization<TSelf> : ICoreMultiProjector<TSelf>
    where TSelf : ICoreMultiProjectorWithCustomSerialization<TSelf>
{
    /// <summary>
    ///     Serializes the projector payload to bytes with size information.
    ///     This method must be implemented as a static method in the implementing class.
    ///     Custom serializers control their own compression.
    /// </summary>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="safeWindowThreshold">Safe window threshold (SortableUniqueId string) used to build safe view; callers MUST supply</param>
    /// <param name="payload">The payload instance to serialize</param>
    /// <returns>SerializationResult containing serialized data and size information</returns>
    static abstract SerializationResult Serialize(DcbDomainTypes domainTypes, string safeWindowThreshold, TSelf payload);

    /// <summary>
    ///     Deserializes bytes back to the projector payload.
    ///     This method must be implemented as a static method in the implementing class.
    ///     Custom serializers control their own decompression.
    /// </summary>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="safeWindowThreshold">Safe window threshold (SortableUniqueId string) used to build safe view; callers MUST supply</param>
    /// <param name="data">Binary serialized bytes</param>
    /// <returns>Deserialized payload instance</returns>
    static abstract TSelf Deserialize(DcbDomainTypes domainTypes, string safeWindowThreshold, ReadOnlySpan<byte> data);
}