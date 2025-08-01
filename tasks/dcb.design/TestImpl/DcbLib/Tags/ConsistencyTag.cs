using DcbLib.Common;
using ResultBoxes;

namespace DcbLib.Tags;

/// <summary>
/// A wrapper that ensures any ITag is treated as a consistency tag
/// </summary>
public record ConsistencyTag(ITag InnerTag) : ITag
{
    public OptionalValue<SortableUniqueId> SortableUniqueId { get; init; } = OptionalValue<SortableUniqueId>.None;

    public bool IsConsistencyTag() => true;

    public string GetTagGroup() => InnerTag.GetTagGroup();

    public string GetTag() => InnerTag.GetTag();

    /// <summary>
    /// Creates a consistency tag from any tag
    /// </summary>
    public static ConsistencyTag From(ITag tag) => new(tag);

    /// <summary>
    /// Creates a consistency tag from any tag with a specific SortableUniqueId
    /// </summary>
    public static ConsistencyTag FromTagWithSortableUniqueId(ITag tag, SortableUniqueId sortableUniqueId) => 
        new(tag) { SortableUniqueId = new OptionalValue<SortableUniqueId>(sortableUniqueId) };
}