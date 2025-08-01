namespace DcbLib.Tags;

/// <summary>
/// A wrapper that ensures any ITag is treated as a consistency tag
/// </summary>
public record ConsistencyTag(ITag InnerTag) : ITag
{
    public bool IsConsistencyTag() => true;

    public string GetTagGroup() => InnerTag.GetTagGroup();

    public string GetTag() => InnerTag.GetTag();

    /// <summary>
    /// Creates a consistency tag from any tag
    /// </summary>
    public static ConsistencyTag From(ITag tag) => new(tag);
}