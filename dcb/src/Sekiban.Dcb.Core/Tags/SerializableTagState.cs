namespace Sekiban.Dcb.Tags;

public record SerializableTagState(
    byte[] Payload,
    int Version,
    string LastSortedUniqueId,
    string TagGroup,
    string TagContent,
    string TagProjector,
    string TagPayloadName,
    string ProjectorVersion,
    string? ActualPayloadName = null)
{
    public string ResolvedPayloadName =>
        string.IsNullOrWhiteSpace(ActualPayloadName)
            ? TagPayloadName
            : ActualPayloadName;
}
