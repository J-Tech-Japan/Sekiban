using Sekiban.Dcb.Common;
namespace Sekiban.Dcb.Tags;

/// <summary>
///     A wrapper that ensures any ITag is treated as a consistency tag
/// </summary>
public record ConsistencyTag(ITag InnerTag) : ITag
{
    /// <summary>
    ///     The sortable unique id the caller observed for the tag, or <c>null</c> when none was supplied.
    /// </summary>
    public SortableUniqueId? SortableUniqueId { get; init; }

    public bool IsConsistencyTag() => true;

    public string GetTagGroup() => InnerTag.GetTagGroup();

    public string GetTagContent() => InnerTag.GetTagContent();

    /// <summary>
    ///     Creates a consistency tag from any tag
    /// </summary>
    public static ConsistencyTag From(ITag tag) => new(tag);

    /// <summary>
    ///     Creates a consistency tag from any tag with a specific SortableUniqueId
    /// </summary>
    public static ConsistencyTag FromTagWithSortableUniqueId(ITag tag, SortableUniqueId sortableUniqueId) =>
        new(tag) { SortableUniqueId = sortableUniqueId };
}
