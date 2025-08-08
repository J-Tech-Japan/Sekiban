namespace Sekiban.Dcb.Tags;

/// <summary>
///     Strongly typed version of TagState for easier use
/// </summary>
/// <typeparam name="TPayload">The type of the state payload</typeparam>
public record TagStateTyped<TPayload>(ITag Tag, TPayload Payload, long Version, DateTimeOffset LastModified)
    where TPayload : ITagStatePayload
{
    /// <summary>
    ///     Converts this typed state to a general TagState
    /// </summary>
    /// <returns>A general TagState</returns>
    public TagState ToTagState() => new(
        Payload,
        (int)Version,
        "", // LastSortedUniqueId - needs to be provided
        Tag.GetTagGroup(),
        Tag.GetTag().Replace($"{Tag.GetTagGroup()}:", ""),
        string.Empty, // TagProjector - needs to be provided
        string.Empty // ProjectorVersion - needs to be provided
    );
}
