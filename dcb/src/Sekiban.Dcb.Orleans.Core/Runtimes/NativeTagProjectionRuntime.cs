using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Native C# implementation of ITagProjectionRuntime.
///     Delegates to DcbDomainTypes for tag projector management.
/// </summary>
public class NativeTagProjectionRuntime : ITagProjectionRuntime
{
    private readonly ITagProjectorTypes _tagProjectorTypes;
    private readonly ITagTypes _tagTypes;
    private readonly ITagStatePayloadTypes _tagStatePayloadTypes;

    public NativeTagProjectionRuntime(DcbDomainTypes domainTypes)
    {
        _tagProjectorTypes = domainTypes.TagProjectorTypes;
        _tagTypes = domainTypes.TagTypes;
        _tagStatePayloadTypes = domainTypes.TagStatePayloadTypes;
    }

    public ResultBox<ITagProjector> GetProjector(string tagProjectorName)
    {
        var funcResult = _tagProjectorTypes.GetProjectorFunction(tagProjectorName);
        if (!funcResult.IsSuccess)
        {
            return ResultBox.Error<ITagProjector>(funcResult.GetException());
        }

        return ResultBox.FromValue<ITagProjector>(new NativeTagProjector(funcResult.GetValue()));
    }

    public ResultBox<string> GetProjectorVersion(string tagProjectorName) =>
        _tagProjectorTypes.GetProjectorVersion(tagProjectorName);

    public IReadOnlyList<string> GetAllProjectorNames() =>
        _tagProjectorTypes.GetAllProjectorNames();

    public string? TryGetProjectorForTagGroup(string tagGroupName) =>
        _tagProjectorTypes.TryGetProjectorForTagGroup(tagGroupName);

    public ITag ResolveTag(string tagString) =>
        _tagTypes.GetTag(tagString);

    public ResultBox<byte[]> SerializePayload(ITagStatePayload payload) =>
        _tagStatePayloadTypes.SerializePayload(payload);

    public ResultBox<ITagStatePayload> DeserializePayload(string payloadName, byte[] data) =>
        _tagStatePayloadTypes.DeserializePayload(payloadName, data);
}
