using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Shared by the generic tag multi-projectors when they restore a snapshot.
/// </summary>
internal static class GenericTagPayloadReader
{
    /// <summary>
    ///     Deserializes one snapshot item's tag-state payload, or returns null when it cannot be read. A snapshot item
    ///     that cannot be read is skipped, not fatal: the projector keeps every item it can restore.
    /// </summary>
    internal static ITagStatePayload? TryDeserialize(DcbDomainTypes domainTypes, string payloadType, byte[] payloadBytes)
    {
        try
        {
            return domainTypes.TagStatePayloadTypes.DeserializePayload(payloadType, payloadBytes);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
