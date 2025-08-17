namespace Sekiban.Dcb.Tags;

/// <summary>
///     ITag コレクションおよび単体向け拡張メソッドです。
/// </summary>
public static class TagExtensions
{
    /// <summary>
    ///     指定したタグ型が含まれているか確認します。
    /// </summary>
    public static bool HasTag<TTag>(this IEnumerable<ITag> tags) where TTag : ITag => tags.Any(t => t is TTag);

    /// <summary>
    ///     指定したタグ型の最初のインスタンスを取得します。
    /// </summary>
    public static bool TryGetTag<TTag>(this IEnumerable<ITag> tags, out TTag? tag) where TTag : class, ITag
    {
        foreach (var t in tags)
        {
            if (t is TTag matched)
            {
                tag = matched;
                return true;
            }
        }
        tag = null;
        return false;
    }

    /// <summary>
    ///     group:content 形式の文字列表現を取得します。
    /// </summary>
    public static string GetTag(this ITag tag) => $"{tag.GetTagGroup()}:{tag.GetTagContent()}";

    /// <summary>
    ///     指定したタグ型のすべてのインスタンスを列挙します。
    /// </summary>
    public static IEnumerable<TTag> GetTagGroups<TTag>(this IEnumerable<ITag> tags) where TTag : ITag =>
        tags.OfType<TTag>();
}
