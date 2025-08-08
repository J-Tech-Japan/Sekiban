using ResultBoxes;
using Sekiban.Dcb.Common;
namespace Sekiban.Dcb.Tags;

/// <summary>
///     A wrapper that ensures any ITag is treated as a consistency tag
/// </summary>
public record ConsistencyTag(ITagCommon InnerTag) : ITagCommon
{
    public OptionalValue<SortableUniqueId> SortableUniqueId { get; init; } = OptionalValue<SortableUniqueId>.None;

    public bool IsConsistencyTag() => true;

    public string GetTagGroup() => InnerTag.GetTagGroup();

    public string GetTagContent() => InnerTag.GetTagContent();

    /// <summary>
    ///     Creates a consistency tag from any tag
    /// </summary>
    public static ConsistencyTag From(ITagCommon tag) => new(tag);

    /// <summary>
    ///     Creates a consistency tag from any tag with a specific SortableUniqueId
    /// </summary>
    public static ConsistencyTag FromTagWithSortableUniqueId(ITagCommon tag, SortableUniqueId sortableUniqueId) =>
        new(tag) { SortableUniqueId = new OptionalValue<SortableUniqueId>(sortableUniqueId) };
}
