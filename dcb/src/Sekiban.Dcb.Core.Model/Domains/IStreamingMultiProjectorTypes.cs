using ResultBoxes;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     Optional capability implemented by multi-projector registries that can restore an individual projector directly
///     from a payload stream. It deliberately lives beside, rather than on, <see cref="ICoreMultiProjectorTypes" /> so
///     registries compiled against earlier Sekiban versions remain binary compatible and use the buffered fallback.
/// </summary>
/// <remarks>
///     Support is intentionally queried per projector. A registry can stream its JSON projectors while leaving a custom
///     binary projector on the compatibility path until that projector opts into
///     <see cref="ICoreMultiProjectorWithStreamDeserialization" />.
/// </remarks>
public interface IStreamingMultiProjectorTypes
{
    /// <summary>Returns whether this exact projector can deserialize directly from a payload stream.</summary>
    bool SupportsStreamDeserialization(string projectorName);

    /// <summary>
    ///     Deserializes the projector payload from <paramref name="source" />. The caller retains ownership of the
    ///     stream; implementations must neither dispose it nor seek it, and must honor <paramref name="cancellationToken" />.
    /// </summary>
    Task<ResultBox<IMultiProjectionPayload>> DeserializeFromStreamAsync(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        Stream source,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Optional per-projector capability for custom serialization registrations. Implement it on a custom projector to
///     let a supporting registry avoid the compatibility byte-array restore path for that projector alone.
/// </summary>
/// <remarks>
///     The stream is positioned at the start of the serialized payload (or at its current caller-selected position),
///     can be non-seekable, and remains owned by the caller. Implementations must not dispose it.
/// </remarks>
public interface ICoreMultiProjectorWithStreamDeserialization
{
    Task<IMultiProjectionPayload> DeserializeFromStreamAsync(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        Stream source,
        CancellationToken cancellationToken = default);
}
