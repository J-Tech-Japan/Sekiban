namespace Sekiban.Dcb.Tags;

/// <summary>
///     A wrapper that ensures any ITag is treated as a non-consistency tag
/// </summary>
public record NonConsistencyTag(ITagCommon InnerTag) : ITagCommon
{
    public bool IsConsistencyTag() => false;

    public string GetTagGroup() => InnerTag.GetTagGroup();

    public string GetTagContent() => InnerTag.GetTagContent();

    /// <summary>
    ///     Creates a non-consistency tag from any tag
    /// </summary>
    public static NonConsistencyTag From(ITagCommon tag) => new(tag);
}
