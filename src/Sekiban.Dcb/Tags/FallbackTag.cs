namespace Sekiban.Dcb.Tags;

/// <summary>
/// フォールバック用の汎用タグを表すレコードです。
/// </summary>
public sealed record FallbackTag(string TagGroup, string TagContent) : ITag
{
    /// <inheritdoc />
    public bool IsConsistencyTag() => false;

    /// <inheritdoc />
    public string GetTagGroup() => TagGroup;

    /// <inheritdoc />
    public string GetTagContent() => TagContent;
}
