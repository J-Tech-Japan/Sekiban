namespace Sekiban.Dcb.Tags;

public record TagState(
    ITagStatePayload Payload,
    int Version,
    string LastSortedUniqueId,
    string TagGroup,
    string TagContent,
    string TagProjector,
    string ProjectorVersion = "")
{
    /// <summary>
    /// Creates an empty TagState with EmptyTagStatePayload
    /// </summary>
    public static TagState GetEmpty(TagStateId tagStateId) =>
        new(
            new EmptyTagStatePayload(),
            0,
            "",
            tagStateId.TagGroup,
            tagStateId.TagContent,
            tagStateId.TagProjectorName,
            ""
        );
}
