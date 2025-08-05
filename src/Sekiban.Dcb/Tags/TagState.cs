namespace Sekiban.Dcb.Tags;

public record TagState(
    ITagStatePayload Payload,
    int Version,
    string LastSortedUniqueId,
    string TagGroup,
    string TagContent,
    string TagProjector
);