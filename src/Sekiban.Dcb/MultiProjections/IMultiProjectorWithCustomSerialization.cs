using Sekiban.Dcb.Domains;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Interface for multi-projectors that require custom serialization logic.
///     Implementing classes must provide static methods for serialization and deserialization.
///     This interface extends IMultiProjector<TSelf> to include all projection capabilities.
/// </summary>
/// <typeparam name="TSelf">The implementing type itself (CRTP pattern)</typeparam>
public interface IMultiProjectorWithCustomSerialization<TSelf> : IMultiProjector<TSelf>
    where TSelf : IMultiProjectorWithCustomSerialization<TSelf>
{
    /// <summary>
    ///     Serializes the projector payload to a JSON string.
    ///     This method must be implemented as a static method in the implementing class.
    /// </summary>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="payload">The payload instance to serialize</param>
    /// <param name="safeWindowThreshold">Safe window threshold (SortableUniqueId string) used to build safe view; callers MUST supply</param>
    /// <returns>Binary serialized representation of the payload</returns>
    static abstract byte[] Serialize(DcbDomainTypes domainTypes, string safeWindowThreshold, TSelf payload);
    
    /// <summary>
    ///     Deserializes a JSON string back to the projector payload.
    ///     This method must be implemented as a static method in the implementing class.
    /// </summary>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="data">Binary serialized bytes</param>
    /// <returns>Deserialized payload instance</returns>
    static abstract TSelf Deserialize(DcbDomainTypes domainTypes, ReadOnlySpan<byte> data);
}

/// <summary>
///     Interface for multi-projectors that require custom serialization logic.
///     Implementing classes must provide static methods for serialization and deserialization.
///     This interface extends IMultiProjector<TSelf> to include all projection capabilities.
/// </summary>
/// <typeparam name="TSelf">The implementing type itself (CRTP pattern)</typeparam>
public interface IMultiProjectorWithCustomSerializationWithoutResult<TSelf> : IMultiProjectorWithoutResult<TSelf>
    where TSelf : IMultiProjectorWithCustomSerializationWithoutResult<TSelf>
{
    /// <summary>
    ///     Serializes the projector payload to a JSON string.
    ///     This method must be implemented as a static method in the implementing class.
    /// </summary>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="payload">The payload instance to serialize</param>
    /// <param name="safeWindowThreshold">Safe window threshold (SortableUniqueId string) used to build safe view; callers MUST supply</param>
    /// <returns>Binary serialized representation of the payload</returns>
    static abstract byte[] Serialize(DcbDomainTypes domainTypes, string safeWindowThreshold, TSelf payload);
    
    /// <summary>
    ///     Deserializes a JSON string back to the projector payload.
    ///     This method must be implemented as a static method in the implementing class.
    /// </summary>
    /// <param name="domainTypes">Domain types containing serialization options</param>
    /// <param name="data">Binary serialized bytes</param>
    /// <returns>Deserialized payload instance</returns>
    static abstract TSelf Deserialize(DcbDomainTypes domainTypes, ReadOnlySpan<byte> data);
}