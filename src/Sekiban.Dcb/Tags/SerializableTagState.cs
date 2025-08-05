namespace Sekiban.Dcb.Tags;

public record SerializableTagState(
    byte[] Payload,
    int Version,
    string LastSortedUniqueId,
    string TagGroup,
    string TagContent,
    string TagProjector,
    string TagPayloadName
);