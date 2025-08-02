namespace DcbLib.Tags;

public record TagState(
    ITagStatePayload Payload,
    int Version,
    int LastSortedUniqueId,
    string TagGroup,
    string TagContent,
    string TagProjector
);