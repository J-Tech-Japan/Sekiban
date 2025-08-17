using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Surrogates;

[RegisterConverter]
public sealed class TagStateSurrogateConverter : IConverter<TagState, TagStateSurrogate>
{
    public TagState ConvertFromSurrogate(in TagStateSurrogate surrogate) =>
        // Note: We're casting the payload back to ITagStatePayload
        // This assumes the object was properly serialized
        new(
            (ITagStatePayload)surrogate.Payload!,
            surrogate.Version,
            surrogate.LastSortedUniqueId,
            surrogate.TagGroup,
            surrogate.TagContent,
            surrogate.TagProjector,
            surrogate.ProjectorVersion);

    public TagStateSurrogate ConvertToSurrogate(in TagState value) =>
        new()
        {
            Payload = value.Payload,
            Version = value.Version,
            LastSortedUniqueId = value.LastSortedUniqueId,
            TagGroup = value.TagGroup,
            TagContent = value.TagContent,
            TagProjector = value.TagProjector,
            ProjectorVersion = value.ProjectorVersion
        };
}
