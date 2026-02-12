using ResultBoxes;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Primitive abstraction for TagState projection.
///     Input/output contracts are fully serialized to support Native and WASM interchangeable implementations.
/// </summary>
public interface ITagStateProjectionPrimitive
{
    Task<ResultBox<SerializableTagState>> ProjectAsync(
        TagStateProjectionRequest request,
        CancellationToken cancellationToken = default);
}
