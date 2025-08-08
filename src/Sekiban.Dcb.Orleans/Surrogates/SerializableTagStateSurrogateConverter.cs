using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Surrogates;

[RegisterConverter]
public sealed class SerializableTagStateSurrogateConverter : IConverter<SerializableTagState, SerializableTagStateSurrogate>
{
    public SerializableTagState ConvertFromSurrogate(in SerializableTagStateSurrogate surrogate)
    {
        return new SerializableTagState(
            surrogate.Payload,
            surrogate.Version,
            surrogate.LastSortedUniqueId,
            surrogate.TagGroup,
            surrogate.TagContent,
            surrogate.TagProjector,
            surrogate.TagPayloadName,
            surrogate.ProjectorVersion);
    }

    public SerializableTagStateSurrogate ConvertToSurrogate(in SerializableTagState value)
    {
        return new SerializableTagStateSurrogate
        {
            Payload = value.Payload,
            Version = value.Version,
            LastSortedUniqueId = value.LastSortedUniqueId,
            TagGroup = value.TagGroup,
            TagContent = value.TagContent,
            TagProjector = value.TagProjector,
            TagPayloadName = value.TagPayloadName,
            ProjectorVersion = value.ProjectorVersion
        };
    }
}