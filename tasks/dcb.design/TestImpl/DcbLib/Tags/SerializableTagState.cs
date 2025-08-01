namespace DcbLib.Tags;

public record SerializableTagState(
    byte[] Payload,
    int Version,
    int LastSortedUniqueId,
    string TagGroup,
    string TagContent,
    string TagProjector,
    string TagPayloadName
);