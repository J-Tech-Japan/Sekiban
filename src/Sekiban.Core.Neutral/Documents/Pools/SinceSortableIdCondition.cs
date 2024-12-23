using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Documents.Pools;

public record SinceSortableIdCondition(SortableUniqueIdValue SortableUniqueId) : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) => SortableUniqueId.IsLaterThan(toCompare);
}
public record BetweenSortableIdCondition(SortableUniqueIdValue Start, SortableUniqueIdValue End) : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) =>
        Start.IsLaterThan(toCompare) || End.IsEarlierThan(toCompare);
}
