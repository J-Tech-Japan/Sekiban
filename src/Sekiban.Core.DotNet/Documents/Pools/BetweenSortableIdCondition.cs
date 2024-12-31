using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Documents.Pools;

public record BetweenSortableIdCondition(SortableUniqueIdValue Start, SortableUniqueIdValue End) : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) =>
        Start.IsLaterThan(toCompare) || End.IsEarlierThan(toCompare);
}
