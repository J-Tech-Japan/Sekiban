using ResultBoxes;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Tag projection execution runtime.
/// </summary>
public interface ITagProjectionRuntime
{
    ResultBox<ITagProjector> GetProjector(string tagProjectorName);
    ResultBox<string> GetProjectorVersion(string tagProjectorName);
    IReadOnlyList<string> GetAllProjectorNames();
    string? TryGetProjectorForTagGroup(string tagGroupName);
    ITag ResolveTag(string tagString);
    ResultBox<byte[]> SerializePayload(ITagStatePayload payload);
    ResultBox<ITagStatePayload> DeserializePayload(string payloadName, byte[] data);
}
